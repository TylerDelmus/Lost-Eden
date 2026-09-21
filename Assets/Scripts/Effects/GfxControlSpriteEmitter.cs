using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stock pooled sprite emitter shared by <c>_GfxControlFlare_t</c> (typeCode 1005/1006, spawn at
/// FUN_100dd0c4) and <c>_GfxControlSparks_t</c> (typeCode 1018, spawn at FUN_100f1997). Both drive
/// the same NewSprite call, so the mechanics live here once.
///
/// Emission follows stock exactly: the spawn counter starts at −field31 and each Process compares it
/// against round(field10 × age). Templates with field10 = 0 therefore release the whole pool on the
/// first frame and never emit again — Flare 43721 (16 sprites) and Sparks 43720 (120) are bursts.
///
/// Every sprite starts at the locator. Velocity is a spherical direction built from an elevation in
/// fields 25–26 and an azimuth in fields 27–28, scaled by a magnitude in fields 29–30 and then by
/// field 32. Flare 43721 has field 32 = 0, which is what keeps its fireball stacked on the locator
/// instead of scattering.
/// </summary>
public abstract class GfxControlSpriteEmitter : GfxControl
{
    const int MaxSprites = 256;
    /// <summary>field0 bit: control finishes once every sprite has expired.</summary>
    const int FlagDieWhenEmpty = 0x200;

    struct Sprite
    {
        /// <summary>
        /// World space. Stock NewSprite is handed the locator's world position and integrates from
        /// there, so sprites are left behind rather than parented to a moving locator.
        /// </summary>
        /// <summary>
        /// World position, or an offset in the emitter's local frame when the emitter is
        /// <see cref="_emitterLocal"/>. See that field for which templates use which.
        /// </summary>
        public Vector3 Position;

        public Vector3 Velocity;

        /// <summary>
        /// The spike's other end, in the same frame as <see cref="Position"/>. Only used when the
        /// control draws segments; see <see cref="_spikeSegment"/>.
        /// </summary>
        public Vector3 PositionB;

        public Vector3 VelocityB;

        public float Age;
        public float Life;
        public float Size0;
        public float Size0Rate;
        public float Size1;
        public float Size1Rate;
        public Color Color;
        public Color ColorRate;
        public bool Active;
    }

    readonly Texture2D _atlas;
    readonly EffectAtlasFrames _frames;
    readonly int _cols;
    readonly int _rows;
    readonly int _firstFrame;
    readonly int _lastFrame;
    readonly EffectLightPool _lights;
    EffectLightLease _lightLease;

    readonly int _flags;
    readonly bool _additive;
    readonly float _emitRate;
    readonly int _randMask;
    readonly float _elevationA;
    readonly float _elevationB;
    readonly float _azimuthA;
    readonly float _azimuthB;
    readonly float _magnitudeA;
    readonly float _magnitudeB;
    readonly float _speedScale;
    /// <summary>
    /// Whether <see cref="Sprite.Position"/> is an offset in the emitter's frame rather than a world
    /// position, so the sprites follow the locator instead of being left behind by it.
    /// </summary>
    readonly bool _emitterLocal;

    /// <summary>
    /// Whether each sprite is a growing line segment rooted on the emitter rather than a point that
    /// travels. Flare (1005/1006) is; Sparks and Cord are not. See
    /// <see cref="SpriteEmitterMath.SpikeEndScales"/> for why, and for how the two ends move.
    /// </summary>
    readonly bool _spikeSegment;

    readonly float _spikeEndA;
    readonly float _spikeEndB;

    readonly float _lifeA;
    readonly float _lifeB;
    readonly float _maxSpriteLife;
    readonly float _size0A;
    readonly float _size0B;
    readonly float _size1A;
    readonly float _size1B;
    readonly Color _startColor;
    readonly Color _endColor;
    readonly Vector3 _gravity;

    /// <summary>
    /// Logs one line per emitter shortly after it starts, reporting its template fields alongside
    /// what it actually produced, so a template that spawns nothing can be told apart from one that
    /// spawns sprites which render to nothing. Diagnostic only; set false to silence.
    /// </summary>
    public static bool LogEmitters = true;

    /// <summary>
    /// Draw Flare spikes the way stock does — a <c>2 * size0</c> square centred on the sprite's first
    /// point, rolled to its screen-space heading — instead of a quad stretched between its two points.
    /// See <see cref="SpriteEmitterMath.SpikeQuadSize"/>. Set false to get the stretched version back
    /// for comparison; nothing else in the pipeline changes.
    /// </summary>
    public static bool StockSpikeGeometry = true;

    readonly Sprite[] _sprites;
    int _spawnCounter;
    int _liveCount;
    bool _diagLogged;

    protected GfxControlSpriteEmitter(
        GfxTweakRecord record,
        EffectLocator locator,
        Texture2D atlas,
        EffectAtlasFrames frames,
        int cols,
        int rows,
        int firstFrame,
        int lastFrame,
        Color tint,
        EffectLightPool lights,
        float fallbackDuration,
        bool spikeSegment = false)
        : base(record, locator)
    {
        _spikeSegment = spikeSegment;
        _atlas = atlas;
        _frames = frames;
        _cols = Mathf.Max(1, cols);
        _rows = Mathf.Max(1, rows);
        _firstFrame = firstFrame;
        _lastFrame = lastFrame >= firstFrame ? lastFrame : firstFrame;
        _lights = lights;

        _flags = record != null ? record.FieldInt(0, 0) : 0;
        _emitRate = record != null ? record.Field(10, 0f) : 0f;
        _randMask = record != null ? record.FieldInt(24, 0) : 0;

        _size0A = record != null ? record.Field(12, 1f) : 1f;
        _size0B = record != null ? record.Field(13, _size0A) : _size0A;
        _size1A = record != null ? record.Field(14, _size0A) : _size0A;
        _size1B = record != null ? record.Field(15, _size1A) : _size1A;

        Color start = EffectColors.ReadArgbBlock(record, 16, Color.white);
        Color end = EffectColors.ReadArgbBlock(record, 20, start);
        if (EffectColors.IsOverrideTint(tint))
        {
            start = EffectColors.ApplyTint(start, tint);
            end = EffectColors.ApplyTint(end, tint);
        }
        _startColor = start;
        _endColor = end;

        _elevationA = record != null ? record.Field(25, 0f) : 0f;
        _elevationB = record != null ? record.Field(26, 0f) : 0f;
        _azimuthA = record != null ? record.Field(27, 0f) : 0f;
        _azimuthB = record != null ? record.Field(28, Mathf.PI * 2f) : Mathf.PI * 2f;
        _magnitudeA = record != null ? record.Field(29, 0f) : 0f;
        _magnitudeB = record != null ? record.Field(30, _magnitudeA) : _magnitudeA;
        _speedScale = record != null ? record.Field(32, 0f) : 0f;
        float endScale = record != null ? record.Field(33, 1f) : 1f;
        SpriteEmitterMath.SpikeEndScales(
            _flags, _speedScale, endScale, out _spikeEndA, out _spikeEndB);

        _lifeA = record != null ? Mathf.Max(0.02f, record.Field(34, 0.5f)) : 0.5f;
        _lifeB = record != null ? Mathf.Max(_lifeA, record.Field(35, _lifeA)) : _lifeA;
        _maxSpriteLife = Mathf.Max(_lifeA, _lifeB);

        // Sparks extends the shared layout with gravity at field 40; Flare templates stop at 35.
        // This is world space and must never be rotated by the locator: hit and beam locators are
        // oriented at the target, so a local-space gravity makes sprites fall forward, not down.
        float gravity = record != null && record.FieldCount > 40 ? record.Field(40, 0f) : 0f;
        _gravity = new Vector3(0f, gravity, 0f);

        // A template that gives every sprite zero speed and no gravity has nothing that could carry
        // its sprites away from the emitter, so holding them in world space only freezes them where
        // the emitter happened to be. Cord 8000/8001 — the hand cast — is exactly that: magnitude 0,
        // 60 a second into a pool of 32 on a 1 second life, so the pool fills in half a second and
        // the cluster then sits still while the hand walks away from it, trailing the leftovers as
        // loose filaments. In stock it stays on the hand, so these sprites live in the emitter frame.
        //
        // Gravity is the one field that is unmistakably world space — ProcessSprites adds it to the
        // velocity's Y without rotating it — so a template that uses it has to be simulated in world
        // space. Everything else is expressed in the locator's own basis, including the spawn cone,
        // so those templates belong in the emitter's frame and stay attached to it.
        //
        // Sparks 43720 has gravity at field 40 and keeps world space so its particles fall away.
        // Cord 8000/8001 and the Flare spike ladder 46000..46017 have no gravity, and in stock they
        // stay on the caster's hand rather than being left behind by it.
        _emitterLocal = gravity == 0f;

        int burstCount = record != null ? record.FieldInt(31, 1) : 1;
        _sprites = new Sprite[
            SpriteEmitterMath.PoolCapacity(burstCount, _emitRate, _maxSpriteLife, MaxSprites)];
        _spawnCounter = SpriteEmitterMath.InitialSpawnCounter(burstCount);

        SetDurationFromTemplate(8, fallbackDuration);

        // Only additive families glow, so only they contribute a light. Sparks and FlareAlt are
        // alpha-blended in stock and must stay matte.
        _additive = SpriteEmitterMath.IsAdditive(_flags);
        if (_additive && _lights != null)
            _lights.TryAcquire(out _lightLease);
    }

    protected override void OnProcess(float dt)
    {
        bool gateOpen = SpriteEmitterMath.RandMaskPasses(_randMask, Random.Range(int.MinValue, int.MaxValue))
            && SpriteEmitterMath.WithinEmitWindow(Duration, Age, _maxSpriteLife);

        if (gateOpen && !IsTerminating)
        {
            int target = SpriteEmitterMath.TargetSpawnCount(_emitRate, Age);
            while (_spawnCounter < target && TrySpawn())
                _spawnCounter++;
        }

        _liveCount = 0;
        for (int i = 0; i < _sprites.Length; i++)
        {
            ref Sprite s = ref _sprites[i];
            if (!s.Active)
                continue;

            s.Age += dt;
            if (s.Age >= s.Life)
            {
                s.Active = false;
                continue;
            }

            s.Velocity += _gravity * dt;
            s.Position += s.Velocity * dt;
            if (_spikeSegment)
                s.PositionB += s.VelocityB * dt;
            s.Size0 += s.Size0Rate * dt;
            s.Size1 += s.Size1Rate * dt;
            s.Color += s.ColorRate * dt;
            _liveCount++;
        }

        // Stock: flag 0x200 ends the control once the pool has drained.
        if ((_flags & FlagDieWhenEmpty) != 0 && _liveCount == 0 && _spawnCounter >= 0)
            ReadyFlag = true;
    }

    bool TrySpawn()
    {
        for (int i = 0; i < _sprites.Length; i++)
        {
            ref Sprite s = ref _sprites[i];
            if (s.Active)
                continue;

            float life = SpriteEmitterMath.Range(_lifeA, _lifeB, Random.value);
            float elevation = SpriteEmitterMath.Range(_elevationA, _elevationB, Random.value);
            float azimuth = SpriteEmitterMath.Range(_azimuthA, _azimuthB, Random.value);
            float magnitude = SpriteEmitterMath.Range(_magnitudeA, _magnitudeB, Random.value);
            // A spike wants the bare direction: field 32 scales one of its ends rather than the whole
            // vector, so folding it in here would shorten the spike and misplace both ends.
            float speedScale = _spikeSegment ? 1f : _speedScale;
            SpriteEmitterMath.SpawnVelocity(
                elevation, azimuth, magnitude, speedScale, out float lx, out float ly, out float lz);

            if (_emitterLocal)
            {
                // The cone is already expressed in the locator's basis, so in the emitter's frame it
                // needs no rotating: the offset is transformed at draw time instead, which lets a
                // spike swing with the hand it is rooted in.
                s.Position = Vector3.zero;
                s.Velocity = new Vector3(lx, ly, lz);
            }
            else
            {
                // Stock orients the cone by the locator's basis axes but hands NewSprite a world
                // position and world velocity, so bake both once at spawn.
                Matrix4x4 world = WorldMatrix;
                Vector3 right = world.GetColumn(0);
                Vector3 up = world.GetColumn(1);
                Vector3 forward = world.GetColumn(2);
                SpriteEmitterMath.ComposeWorldVelocity(
                    right.x, right.y, right.z,
                    up.x, up.y, up.z,
                    forward.x, forward.y, forward.z,
                    lx, ly, lz,
                    out float vx, out float vy, out float vz);

                s.Position = world.GetColumn(3);
                s.Velocity = new Vector3(vx, vy, vz);
            }

            if (_spikeSegment)
            {
                // Both ends start on the emitter and separate along the spawn direction, reaching
                // their own multiple of it after exactly one lifetime.
                Vector3 direction = s.Velocity;
                s.PositionB = s.Position;
                s.Velocity = direction * (_spikeEndA / life);
                s.VelocityB = direction * (_spikeEndB / life);
            }

            s.Age = 0f;
            s.Life = life;
            s.Size0 = _size0A;
            s.Size0Rate = SpriteEmitterMath.RatePerSecond(_size0A, _size0B, life);
            s.Size1 = _size1A;
            s.Size1Rate = SpriteEmitterMath.RatePerSecond(_size1A, _size1B, life);
            s.Color = _startColor;
            s.ColorRate = new Color(
                SpriteEmitterMath.RatePerSecond(_startColor.r, _endColor.r, life),
                SpriteEmitterMath.RatePerSecond(_startColor.g, _endColor.g, life),
                SpriteEmitterMath.RatePerSecond(_startColor.b, _endColor.b, life),
                SpriteEmitterMath.RatePerSecond(_startColor.a, _endColor.a, life));
            s.Active = true;
            return true;
        }
        return false;
    }

    protected override void OnTerminateGracefully()
    {
        // Stock lets in-flight sprites finish: duration = age + max sprite life.
        SetDuration(Age + _maxSpriteLife);
    }

    protected override void OnReleased(bool immediate)
    {
        _lightLease?.Release();
        _lightLease = null;
    }

    public override void CollectBillboards(List<EffectBillboardBatch.Quad> dest, Camera camera)
    {
        if (!IsAlive || dest == null)
        {
            _lightLease?.Release();
            _lightLease = null;
            return;
        }

        int frameCount = _lastFrame - _firstFrame + 1;
        int quads = 0;
        bool diagCaptured = false;
        float diagWidth = 0f, diagHeight = 0f, diagLocal = 0f, diagTravel = 0f, diagLength = 0f;
        bool diagSpike = false;
        float diagMinTravel = float.MaxValue, diagMaxTravel = 0f;
        int diagInFront = 0;
        Color brightest = Color.clear;
        float brightestScale = 0f;
        Matrix4x4 emitter = _emitterLocal ? WorldMatrix : Matrix4x4.identity;

        for (int i = 0; i < _sprites.Length; i++)
        {
            ref Sprite s = ref _sprites[i];
            if (!s.Active)
                continue;

            float t01 = s.Life > 1e-4f ? Mathf.Clamp01(s.Age / s.Life) : 0f;

            // Channel 0 is width, channel 1 is height. Collapsing them with Max turned 46002's
            // 0.05 x 1.0 hand spikes into 1.0 squares, which is why the plasma read as a circle.
            SpriteEmitterMath.ResolveSizeChannels(s.Size0, s.Size1, out float width, out float height);

            // Where the sprite sits, and how far it has travelled from the emitter since it spawned.
            Vector3 anchor;
            Vector3 offset;
            if (_emitterLocal)
            {
                anchor = emitter.GetColumn(3);
                offset = emitter.MultiplyVector(s.Position);
            }
            else
            {
                anchor = s.Position;
                offset = Vector3.zero;
            }

            // A spike spans its two ends; everything else is a plain camera-aligned quad of
            // width by height centred on where the sprite has travelled to.
            //
            // Stock's non-spike builder (DisplaySystem FUN_10025391, used by Sparks) emits the four
            // corners as P -+ A*size0 -+ B*size1, where A and B are the camera's right and up axes
            // carried into the emitter's local space. It reads exactly four things per sprite:
            // position, size0, size1 and the packed colour — never the velocity, and never the three
            // random values NewSprite stores at spawn, so nothing there can rotate the quad.
            Vector3 position = anchor + offset;
            Vector3 stretch = Vector3.zero;
            float roll = 0f;
            if (_spikeSegment)
            {
                Vector3 endB = _emitterLocal
                    ? anchor + emitter.MultiplyVector(s.PositionB)
                    : s.PositionB;

                if (StockSpikeGeometry)
                {
                    // Stock centres the square on the first point and only measures the roll from
                    // the second, so the sprite stays a small square that flies outward.
                    width = SpriteEmitterMath.SpikeQuadSize(s.Size0);
                    height = width;
                    roll = ScreenRoll(camera, position, endB);
                }
                else
                {
                    stretch = position - endB;
                    position = (position + endB) * 0.5f;
                }
            }

            float scale = Mathf.Max(width, height);
            Color color = s.Color;
            color.a = Mathf.Clamp01(color.a);
            if (color.a < 0.01f)
                continue;

            if (color.a > brightest.a)
            {
                brightest = color;
                brightestScale = scale;
            }

            int frame = frameCount <= 1
                ? _firstFrame
                : _firstFrame + Mathf.Clamp(Mathf.FloorToInt(t01 * frameCount), 0, frameCount - 1);

            Texture2D tex = _frames != null
                ? _frames.GetFrame(_atlas, _cols, _rows, frame)
                : _atlas;
            if (tex == null)
                continue;

            dest.Add(new EffectBillboardBatch.Quad
            {
                Matrix = Matrix4x4.TRS(position, Quaternion.identity, Vector3.one),
                Scale = width,
                Height = height,
                Stretch = stretch,
                Roll = roll,
                Color = color,
                Texture = tex,
                Additive = _additive,
            });
            quads++;

            float travel = offset.magnitude;
            if (travel < diagMinTravel) diagMinTravel = travel;
            if (travel > diagMaxTravel) diagMaxTravel = travel;
            if (camera != null
                && Vector3.Dot(camera.transform.forward, position - camera.transform.position) > 0f)
                diagInFront++;

            if (!diagCaptured)
            {
                diagCaptured = true;
                diagWidth = width;
                diagHeight = height;
                diagLocal = s.Position.magnitude;
                diagTravel = travel;
                // Under stock geometry a spike has no stretch, so report the square's side instead.
                diagLength = _spikeSegment && !StockSpikeGeometry ? stretch.magnitude : height;
                diagSpike = _spikeSegment;
            }
        }

        LogOnce(quads, dest.Count, emitter, camera, diagCaptured, diagWidth, diagHeight,
            diagLocal, diagTravel, diagLength, diagSpike, diagMinTravel, diagMaxTravel, diagInFront);
        UpdateLight(brightest, brightestScale);
    }

    /// <summary>
    /// Screen-space heading from <paramref name="from"/> to <paramref name="to"/>, in degrees, which is
    /// what stock's spike builder measures after projecting both of a sprite's points.
    /// </summary>
    static float ScreenRoll(Camera camera, Vector3 from, Vector3 to)
    {
        if (camera == null)
            return 0f;
        Vector3 a = camera.WorldToScreenPoint(from);
        Vector3 b = camera.WorldToScreenPoint(to);
        return SpriteEmitterMath.SpikeRollDegrees(b.x - a.x, b.y - a.y);
    }

    /// <summary>See <see cref="LogEmitters"/>.</summary>
    void LogOnce(
        int quads, int destTotal, Matrix4x4 emitter, Camera camera, bool captured,
        float width, float height, float localDist, float travel, float length, bool spike,
        float minTravel, float maxTravel, int inFront)
    {
        if (_diagLogged || !LogEmitters || Age < 0.4f)
            return;
        _diagLogged = true;

        // The emitter basis lengths: a scale baked into the attractor would shrink every offset,
        // which would silently drop sprites below the spike threshold.
        Vector3 bx = emitter.GetColumn(0), by = emitter.GetColumn(1), bz = emitter.GetColumn(2);

        Debug.Log(
            $"[Effects] emitter id={(Record != null ? Record.Id : 0)} "
            + $"type={(Record != null ? Record.TypeCode : 0)} age={Age:0.00} dur={Duration:0.00} "
            + $"pool={_sprites.Length} live={_liveCount} quads={quads} destTotal={destTotal} "
            + $"counter={_spawnCounter} target={SpriteEmitterMath.TargetSpawnCount(_emitRate, Age)} "
            + $"rate={_emitRate:0.##} life={_lifeA:0.##}..{_lifeB:0.##} "
            + $"size0={_size0A:0.###}..{_size0B:0.###} size1={_size1A:0.###}..{_size1B:0.###} "
            + $"mag={_magnitudeA:0.##}..{_magnitudeB:0.##} spd={_speedScale:0.##} "
            + $"local={_emitterLocal} additive={_additive} "
            + $"gate={SpriteEmitterMath.WithinEmitWindow(Duration, Age, _maxSpriteLife)} "
            + $"atlas={(_atlas != null ? _atlas.name : "<null>")} "
            + $"start=({_startColor.r:0.##},{_startColor.g:0.##},{_startColor.b:0.##},{_startColor.a:0.##}) "
            + $"end=({_endColor.r:0.##},{_endColor.g:0.##},{_endColor.b:0.##},{_endColor.a:0.##}) "
            + $"basis=({bx.magnitude:0.###},{by.magnitude:0.###},{bz.magnitude:0.###}) "
            + $"reach={(minTravel == float.MaxValue ? 0f : minTravel):0.###}..{maxTravel:0.###} "
            + $"inFrontOfCam={inFront}/{quads} "
            + $"camDist={(camera != null ? Vector3.Distance(camera.transform.position, emitter.GetColumn(3)) : -1f):0.##} "
            + (captured
                ? $"| sprite0 w={width:0.####} h={height:0.####} localDist={localDist:0.####} "
                  + $"travel={travel:0.####} drawLen={length:0.####} "
                  + $"aspect={(width > 0f ? length / width : 0f):0.#} spike={spike} "
                  + $"streak={SpriteEmitterMath.IsStreak(width, height)}"
                : "| sprite0 <none drawn>"));
    }

    /// <summary>
    /// Drives the pooled light every frame, including down to black when nothing is visible.
    /// Skipping the update on an empty pool used to leave the slot at its last brightness, so the
    /// light lingered for the rest of the control's duration after the sprites had gone.
    /// </summary>
    void UpdateLight(Color brightest, float brightestScale)
    {
        if (_lightLease == null)
            return;

        if (brightestScale <= 0f)
        {
            // Once emission has closed and the pool has drained, nothing can light up again, so
            // hand the slot back. Endless emitters keep theirs and just go dark between bursts.
            if (_liveCount == 0 && !SpriteEmitterMath.WithinEmitWindow(Duration, Age, _maxSpriteLife))
            {
                _lightLease.Release();
                _lightLease = null;
                return;
            }

            _lightLease.Update(WorldMatrix.GetColumn(3), Color.black, 0f, 1f);
            return;
        }

        _lightLease.Update(
            WorldMatrix.GetColumn(3),
            brightest,
            EffectLightPool.NanoIntensity(brightest),
            EffectLightPool.NanoRange(brightestScale));
    }
}
