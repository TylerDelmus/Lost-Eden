using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// typeCode 2004 — stock <c>_GfxControlStars_t</c> (ctor <c>Gamecode 100f74ad</c>, Process
/// <c>FUN_100f8491</c>): up to 128 sprites drawn by one <c>GfxVisualDiaBill</c>.
///
/// starTypes 3 and 19 are recovered in full and replayed call-for-call by <see cref="StarsCase3"/> and
/// <see cref="StarsCase19"/>, and starTypes 7 and 8 by <see cref="StarsRing"/>. Every other starType below
/// is still the earlier approximation and has not been checked against FUN_100f8491.
///
/// Stock Stars never creates a light: no code in the class range (100f7164..100fc404) reaches a light
/// API, so none is attached here.
/// </summary>
public sealed class GfxControlStars : GfxControl
{
    const int MaxPoints = 128;
    const float ImmediateSpawn = -100f;

    /// <summary>
    /// Replayed stock Process calls allowed in one frame. A hitch loses time instead of fast-forwarding
    /// the swarm through several lifetimes at once.
    /// </summary>
    const int MaxStockStepsPerFrame = 8;

    /// <summary>
    /// What stock's base Process does with an expired duration for this starType (<c>100f84ed</c>):
    /// stop spawning and allow this much longer for the live particles to finish.
    /// </summary>
    const float StockDrainSeconds = 5f;

    readonly StarsCase3 _case3;
    readonly StarsCase19 _case19;
    readonly EffectHitLocation _hitLocation;
    readonly StarsRing _ring;
    bool _ringTerminating;

    /// <summary>The replayed starType 3 state, or null for other starTypes. For debug tooling.</summary>
    public StarsCase3 StockCase3 => _case3;

    /// <summary>The replayed starType 19 state, or null for other starTypes. For debug tooling.</summary>
    public StarsCase19 StockCase19 => _case19;

    /// <summary>A starType 19 trail built on a hit location: stock sets it no duration and lets it run.</summary>
    public bool IsHitLocationTracer => _case19 != null && _hitLocation != null;

    float _stockCarry;
    float _stockAge;
    float _stockDuration;
    bool _stockArmed;

    struct StarPoint
    {
        public Vector3 Position;
        public Vector3 Anchor;
        public Vector3 Velocity;
        public float SpawnTimer;
        public float Size;
        public float Alpha;
        /// <summary>0 at spawn → 1 at end of particle life. Used for start→end color.</summary>
        public float LifeFrac;
        public bool Active;
    }

    readonly Texture2D _atlas;
    readonly EffectAtlasFrames _frames;
    readonly int _cols;
    readonly int _rows;
    readonly int _firstFrame;
    readonly int _lastFrame;
    readonly int _starType;
    readonly float _sizeCurveScale;
    readonly float _spawnRadius;
    readonly float _spawnInterval;
    readonly float _particleLifetime;
    readonly float _turbulence;
    readonly int _field30Int;
    readonly int _field31Int;

    /// <summary>
    /// Fractional carry for <see cref="TakeSpawnBudget"/>, so a budget of less than one slot per
    /// frame still opens slots at the right rate rather than rounding away to nothing.
    /// </summary>
    float _spawnBudgetCarry;
    readonly Color _startColor;
    readonly Color _endColor;
    readonly Vector3[] _dirs;
    readonly StarPoint[] _points = new StarPoint[MaxPoints];
    readonly bool _looping;
    bool _seeded;

    public GfxControlStars(
        GfxTweakRecord record,
        EffectLocator locator,
        Texture2D atlas,
        EffectAtlasFrames frames,
        int cols,
        int rows,
        int firstFrame,
        int lastFrame,
        EffectHitLocation hitLocation = null)
        : base(record, locator)
    {
        _atlas = atlas;
        _frames = frames;
        _cols = Mathf.Max(1, cols);
        _rows = Mathf.Max(1, rows);
        _firstFrame = firstFrame;
        _lastFrame = lastFrame >= firstFrame ? lastFrame : firstFrame;

        _starType = record != null ? record.FieldInt(10, 6) : 6;

        if (_starType == 3 && record != null)
        {
            // Loader FUN_100f72a9: field 8 is read into the duration and then overwritten by field 26.
            _stockDuration = record.Field(26, 0f);
            _case3 = new StarsCase3(
                lifeSeconds: record.FieldInt(30, 0) / 1000f,
                sizeCurve: record.Field(28, 0f),
                radiusXZ: record.Field(29, 0f),
                startArgb: new[] { record.Field(18), record.Field(19), record.Field(20), record.Field(21) },
                endArgb: new[] { record.Field(22), record.Field(23), record.Field(24), record.Field(25) },
                rand: () => Random.Range(0, 0x8000));
            // Stock expiry is handled per replayed step, not by the base timer.
            base.SetDuration(InfiniteDuration);
            return;
        }

        if (_starType == 19 && record != null)
        {
            // Loader FUN_100f72a9: the duration is field 26. The hit location comes from
            // CreateGfxControl(id, hitLoc) (100f7d4a); without one the trail never spawns.
            _hitLocation = hitLocation;
            _stockDuration = record.Field(26, 0f);
            _case19 = new StarsCase19(
                lifeSeconds: record.FieldInt(30, 0) / 1000f,
                size: record.Field(28, 0f),
                radius: record.Field(29, 0f),
                startArgb: new[] { record.Field(18), record.Field(19), record.Field(20), record.Field(21) },
                endArgb: new[] { record.Field(22), record.Field(23), record.Field(24), record.Field(25) },
                rand: () => Random.Range(0, 0x8000));
            base.SetDuration(InfiniteDuration);
            return;
        }

        if (StarsRing.Handles(_starType) && record != null)
        {
            _ring = new StarsRing(_starType, record.Fields);
            // Loader FUN_100f72a9: the duration is field 26 (field 8 is read, then overwritten).
            base.SetDuration(record.Field(26, 0f));
            return;
        }

        _sizeCurveScale = record != null ? Mathf.Max(0.05f, record.Field(28, 0.35f)) : 0.35f;
        _spawnRadius = record != null ? Mathf.Max(0.05f, record.Field(29, 1f)) : 1f;
        // Field 30: case 3 life ms (→ /1000); case 8 radius scale int (→ *0.01*sqrt).
        _field30Int = record != null ? record.FieldInt(30, 0) : 0;
        float lifeMs = _field30Int;
        _particleLifetime = lifeMs > 0 ? lifeMs / 1000f : 0f;
        // Field 11 is a per-type tune float — only some cases treat it as a respawn interval.
        float interval = record != null ? record.Field(11, 0f) : 0f;
        if (interval < 0.05f || float.IsNaN(interval))
            interval = _particleLifetime > 0.05f ? _particleLifetime : 0.45f;
        _spawnInterval = interval;
        // Field 31: continuous turbulence (/100); case 8 cycle count (int as-is).
        _field31Int = record != null ? record.FieldInt(31, 10) : 10;
        _turbulence = _field31Int / 100f;

        _startColor = EffectColors.ReadArgbBlock(record, 18, Color.white);
        _endColor = EffectColors.ReadArgbBlock(record, 22, _startColor);
        _looping = _starType != 0x19;

        float duration = record != null ? record.Field(26, 2f) : 2f;

        if (duration < 0f)
            SetDuration(InfiniteDuration);
        else if (IsContinuousEmitter(_starType))
            SetDuration(duration > 0.05f ? duration : InfiniteDuration);
        else
            SetDuration(duration > 0.05f ? duration : 1.6f);

        _dirs = BuildDirectionTemplate();
    }

    /// <summary>
    /// Stock slot 8 (<c>100f7184</c>) just stores the duration. For starType 3 that is the replayed
    /// duration, which is what the nano cast's <c>SetDuration(6000)</c> lands on through the Meta.
    /// </summary>
    public override void SetDuration(float seconds)
    {
        if (_case3 != null || _case19 != null)
        {
            _stockDuration = seconds;
            return;
        }

        base.SetDuration(seconds);
    }

    static bool IsContinuousEmitter(int starType)
    {
        return starType is 3 or 4 or 5 or 6 or 9 or 10 or 11 or 12 or 14
               || (starType >= 16 && starType <= 20)
               || starType >= 24;
    }

    static Vector3[] BuildDirectionTemplate()
    {
        var dirs = new Vector3[MaxPoints];
        // Deterministic fibonacci-sphere directions (stock pre-bakes a shape into +0x48).
        const float Golden = 2.399963229728653f;
        for (int i = 0; i < MaxPoints; i++)
        {
            float y = 1f - (i / (float)(MaxPoints - 1)) * 2f;
            float radius = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
            float theta = Golden * i;
            dirs[i] = new Vector3(Mathf.Cos(theta) * radius, y, Mathf.Sin(theta) * radius);
        }
        return dirs;
    }

    protected override void OnProcess(float dt)
    {
        if (_case3 != null)
        {
            ProcessStockCase3(dt);
            return;
        }

        if (_case19 != null)
        {
            ProcessStockCase19(dt);
            return;
        }

        if (_ring != null)
        {
            // Slot 6 only raises +0x1648; the case's tail then readies the control (100fc2ff).
            if (_ringTerminating)
                ReadyFlag = true;
            return;
        }

        EnsureSeeded();

        switch (_starType)
        {
            case 0:
                ProcessCase0();
                break;
            case 4:
            case 5:
                ProcessContinuous(dt, sphere: true, stagger: false, turb: 0.1f);
                break;
            case 6:
            case 11:
            case 9:
            case 10:
                ProcessContinuous(dt, sphere: false, stagger: true, turb: _turbulence);
                break;
            case 19:
                ProcessCase19(dt);
                break;
            default:
                // Closest-matching stub for unread cases.
                if (IsContinuousEmitter(_starType))
                    ProcessContinuous(dt, sphere: false, stagger: true, turb: _turbulence);
                else
                    ProcessCase0();
                break;
        }
    }

    void EnsureSeeded()
    {
        if (_seeded)
            return;
        _seeded = true;

        bool continuous = IsContinuousEmitter(_starType) || _starType == 19;
        for (int i = 0; i < MaxPoints; i++)
        {
            _points[i].SpawnTimer = continuous ? ImmediateSpawn : 0f;
            _points[i].Active = false;
            _points[i].Alpha = 1f;
        }
    }

    void ProcessCase0()
    {
        float age = Age;
        float radiusSq = 1.6f - age;
        if (radiusSq < 0f)
        {
            ReadyFlag = true;
            return;
        }

        float radius = Mathf.Sqrt(radiusSq);
        float sizeBase = (1.5f - age) * 0.25f;
        float sizeScale = Mathf.Max(0.02f, sizeBase * _sizeCurveScale * 2f);
        float lifeFrac = Mathf.Clamp01(age / 1.6f);
        float alpha = Color.Lerp(_startColor, _endColor, lifeFrac).a;

        Matrix4x4 world = WorldMatrix;
        for (int i = 0; i < MaxPoints; i++)
        {
            Vector3 local = _dirs[i] * radius;
            _points[i].Position = world.MultiplyPoint3x4(local);
            _points[i].Size = sizeScale;
            _points[i].Alpha = alpha;
            _points[i].LifeFrac = lifeFrac;
            _points[i].Active = true;
        }
    }

    /// <summary>
    /// starType 3, replayed one stock Process call at a time at <see cref="EffectFrameRate.StockProcessHz"/>.
    /// The per-call behaviour is <see cref="StarsCase3"/>; this owns the stock clock and lifetime:
    /// <list type="bullet">
    /// <item>The first call arms the control at age 0 and still runs the case (<c>100d2a86</c> then
    /// <c>100f8526</c>), so two slots open on the frame the effect is created.</item>
    /// <item>When the duration (field 26, or whatever <see cref="SetDuration"/> set) runs out, stock
    /// clears the ready flag once, stops spawning and adds 5 s (<c>100f84ed</c>); if it runs out again
    /// the control goes.</item>
    /// <item>Once it is terminating and a call finds nothing spawned or alive, it is ready
    /// (<c>100f8c23</c>).</item>
    /// </list>
    /// The origin is the locator position, which follows the bone every frame (field 0 bit 0).
    /// </summary>
    void ProcessStockCase3(float dt)
    {
        Vector3 origin = WorldMatrix.GetColumn(3);

        if (!_stockArmed)
        {
            _stockArmed = true;
            _case3.Step(0f, origin.x, origin.y, origin.z);
            return;
        }

        float step = EffectFrameRate.StockProcessSeconds;
        int steps = EffectFrameRate.TakeFixedSteps(ref _stockCarry, dt, step, MaxStockStepsPerFrame);
        for (int s = 0; s < steps; s++)
        {
            _stockAge += step;

            bool expired = _stockDuration > 0f && _stockDuration < _stockAge;
            if (expired && !_case3.Terminating)
            {
                _case3.Terminating = true;
                _stockDuration += StockDrainSeconds;
                expired = false;
            }

            _case3.Step(_stockAge, origin.x, origin.y, origin.z);

            if (expired || _case3.Drained)
            {
                ReadyFlag = true;
                return;
            }
        }
    }

    /// <summary>
    /// starType 19, replayed like case 3 at <see cref="EffectFrameRate.StockProcessHz"/>. The first call
    /// arms at age 0 and still runs the case. Once <c>_GfxControl_t::Process</c> finds 0 &lt;= duration
    /// &lt; age the control is ready and starType 19 leaves at once (the <c>100f84d6</c> table sends it
    /// straight out, with no drain as case 3 gets), so the whole trail goes with it. Terminating stops
    /// the spawns, and a step that then finds nothing alive readies it (<c>100fc2f0</c>).
    /// </summary>
    void ProcessStockCase19(float dt)
    {
        if (!_stockArmed)
        {
            _stockArmed = true;
            StepStockCase19();
            if (_case19.Drained)
                ReadyFlag = true;
            return;
        }

        float step = EffectFrameRate.StockProcessSeconds;
        int steps = EffectFrameRate.TakeFixedSteps(ref _stockCarry, dt, step, MaxStockStepsPerFrame);
        for (int s = 0; s < steps; s++)
        {
            _stockAge += step;
            if (_stockDuration >= 0f && _stockDuration < _stockAge)
            {
                ReadyFlag = true;
                return;
            }

            StepStockCase19();
            if (_case19.Drained)
            {
                ReadyFlag = true;
                return;
            }
        }
    }

    void StepStockCase19()
    {
        Vector3 start = default, end = default;
        bool found = _hitLocation != null && _hitLocation.TryGetEndpoints(out start, out end);
        _case19.Step(_stockAge, _stockDuration, found, start.x, start.y, start.z, end.x, end.y, end.z);
    }

    /// <summary>
    /// Slots this frame may open, from stock's per-Process allowance. Stock opens at most a fixed
    /// couple of slots per call, which at ~30 FPS is a rate rather than a per-frame constant — used
    /// verbatim it let a 240 FPS client fill the cloud eight times faster than the original, so the
    /// whole thing appeared and burned out early.
    /// </summary>
    int TakeSpawnBudget(int stockPerFrame, float dt) =>
        EffectFrameRate.TakeBudget(ref _spawnBudgetCarry, stockPerFrame, dt);

    void ProcessContinuous(float dt, bool sphere, bool stagger, float turb)
    {
        int spawnBudget = TakeSpawnBudget(2, dt);
        float steps = StockFrameSteps(dt);
        Matrix4x4 world = WorldMatrix;
        Vector3 origin = world.GetColumn(3);
        bool fading = IsTerminating;

        for (int i = 0; i < MaxPoints; i++)
        {
            ref StarPoint p = ref _points[i];
            float due = p.SpawnTimer;
            if (stagger)
                due += i / 9.5f;

            if (due <= Age)
            {
                if (spawnBudget == 0 || fading)
                {
                    p.Active = false;
                    continue;
                }

                Vector3 dir = sphere ? RandomInSphere() : RandomOnCircle();
                dir *= _spawnRadius;
                p.Velocity = world.MultiplyVector(dir);
                p.Anchor = origin + p.Velocity;
                if (!sphere)
                    p.Anchor.y -= 1f;
                p.SpawnTimer = Age + _spawnInterval;
                p.Alpha = sphere ? 1f : 0.5f;
                p.LifeFrac = 0f;
                p.Active = true;
                spawnBudget--;
            }
            else if (p.Active)
            {
                // Turbulence drift (stock FUN_1008718b-style small offsets), per stock Process call.
                p.Anchor += Random.insideUnitSphere * (turb * 0.02f * steps);
                p.Anchor += p.Velocity.normalized * (0.01f * _spawnRadius * steps);
                p.Position = p.Anchor;

                float lifeFrac = (_spawnInterval + Age - p.SpawnTimer) / Mathf.Max(0.05f, _spawnInterval);
                lifeFrac = Mathf.Clamp01(lifeFrac);
                float size = (lifeFrac + 0.2f) * _sizeCurveScale * (1f - lifeFrac * lifeFrac);
                p.Size = Mathf.Max(0.02f, size);
                p.Alpha = Mathf.Clamp01(1f - lifeFrac);
                p.LifeFrac = lifeFrac;
            }
        }
    }

    void ProcessCase19(float dt)
    {
        // Hit-location blend: without a hit-loc system, expand from origin then contract toward it.
        int spawnBudget = TakeSpawnBudget(15, dt);
        Matrix4x4 world = WorldMatrix;
        Vector3 origin = world.GetColumn(3);
        float lifeRatio = Duration > 0f ? Mathf.Clamp01(Age / Duration) : Mathf.Clamp01(Age);
        float fade = 1f - lifeRatio;

        for (int i = 0; i < MaxPoints; i++)
        {
            ref StarPoint p = ref _points[i];
            if (p.SpawnTimer <= Age)
            {
                if (spawnBudget == 0)
                {
                    p.Active = false;
                    continue;
                }

                Vector3 dir = RandomInSphere() * _spawnRadius;
                p.Velocity = dir;
                p.Anchor = origin + dir;
                p.SpawnTimer = Age + _spawnInterval * 0.5f;
                p.Active = true;
                spawnBudget--;
            }

            if (!p.Active)
                continue;

            Vector3 start = origin + p.Velocity;
            Vector3 end = origin;
            p.Position = Vector3.Lerp(start, end, lifeRatio);
            float size = (fade + 0.2f) * _sizeCurveScale * (1f - lifeRatio * lifeRatio);
            p.Size = Mathf.Max(0.02f, size);
            p.Alpha = fade;
            p.LifeFrac = lifeRatio;
        }
    }

    static Vector3 RandomInSphere() => Random.onUnitSphere;

    static Vector3 RandomOnCircle()
    {
        float a = Random.Range(0f, Mathf.PI * 2f);
        return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
    }

    protected override void OnTerminateGracefully()
    {
        // Stock slot 6 (100f717c) only raises +0x1648: spawning stops and live particles run out.
        if (_case3 != null)
        {
            _case3.Terminating = true;
            return;
        }

        if (_case19 != null)
        {
            _case19.Terminating = true;
            return;
        }

        if (_ring != null)
        {
            _ringTerminating = true;
            return;
        }

        // Continuous types stop emitting; finite bursts finish current age window.
        if (IsContinuousEmitter(_starType) && (Duration < 0f || Age + 0.5f < Duration))
            SetDuration(Age + 0.5f);
        else
            ReadyFlag = true;
    }

    public override void CollectBillboards(List<EffectBillboardBatch.Quad> dest, Camera camera)
    {
        if (!IsAlive || dest == null || _atlas == null)
            return;

        if (_case3 != null || _case19 != null)
        {
            CollectStockSprites(dest);
            return;
        }

        if (_ring != null)
        {
            CollectRing(dest);
            return;
        }

        float t01 = Duration > 0f ? Mathf.Clamp01(Age / Duration) : Mathf.Clamp01(Age / 2f);
        int frameCount = _lastFrame - _firstFrame + 1;
        // Continuous emitters color per particle life so start+end hues coexist (stock DiaBill).
        // Burst case 0 keeps a shared lifeFrac so the whole shell shifts together.
        bool perParticleColor =
            IsContinuousEmitter(_starType) || _starType == 19;

        for (int i = 0; i < MaxPoints; i++)
        {
            ref StarPoint p = ref _points[i];
            if (!p.Active)
                continue;

            float life = perParticleColor ? Mathf.Clamp01(p.LifeFrac) : t01;
            Color c = Color.Lerp(_startColor, _endColor, life);
            c.a *= Mathf.Clamp01(p.Alpha);
            if (c.a < 0.01f)
                continue;

            int frame;
            if (!_looping && frameCount > 1)
                frame = _lastFrame;
            else if (frameCount <= 1)
                frame = _firstFrame;
            else
                frame = _firstFrame + Mathf.Clamp(Mathf.FloorToInt(t01 * frameCount), 0, frameCount - 1);

            Texture2D frameTex = _frames != null
                ? _frames.GetFrame(_atlas, _cols, _rows, frame)
                : _atlas;
            if (frameTex == null)
                continue;

            dest.Add(new EffectBillboardBatch.Quad
            {
                Matrix = Matrix4x4.TRS(p.Position, Quaternion.identity, Vector3.one),
                Scale = Mathf.Max(0.05f, p.Size),
                Color = c,
                Texture = frameTex,
                Additive = true,
            });
        }
    }

    /// <summary>
    /// Draws the stock sprite records the way <c>GfxVisualDiaBill</c> does (DisplaySystem
    /// <c>FUN_1001105e</c>): a camera-facing quad centred on the sprite, full width = full height =
    /// the sprite's size, atlas cell <c>frame % cols, frame / cols</c> counted from the top-left, tinted
    /// by the packed ARGB. The visual's own state (ctor <c>10010d70</c>) is SRCALPHA/ONE blending for every
    /// starType but 25, no Z write, no culling, texture modulated by vertex colour.
    /// </summary>
    void CollectStockSprites(List<EffectBillboardBatch.Quad> dest)
    {
        // Draw between the last two replayed steps: the time carried towards the next one says how far.
        float t = Mathf.Clamp01(_stockCarry / EffectFrameRate.StockProcessSeconds);
        StarsCase3.Sprite[] sprites = _case3 != null ? _case3.Sprites : _case19.Sprites;
        for (int i = 0; i < sprites.Length; i++)
        {
            if (!sprites[i].Visible)
                continue;
            StarsCase3.Sprite s = _case3 != null ? _case3.Blend(i, t) : _case19.Blend(i, t);

            Texture2D frameTex = _frames != null
                ? _frames.GetFrame(_atlas, _cols, _rows, s.Frame)
                : _atlas;
            if (frameTex == null)
                continue;

            uint argb = s.Argb;
            dest.Add(new EffectBillboardBatch.Quad
            {
                Matrix = Matrix4x4.TRS(new Vector3(s.X, s.Y, s.Z), Quaternion.identity, Vector3.one),
                // Case 19 can go slightly negative early in a spark's life; the unculled quad is then
                // only mirrored, the same size.
                Scale = Mathf.Abs(s.Size),
                Color = new Color(
                    ((argb >> 16) & 0xff) / 255f,
                    ((argb >> 8) & 0xff) / 255f,
                    (argb & 0xff) / 255f,
                    ((argb >> 24) & 0xff) / 255f),
                Texture = frameTex,
                // Stars passes (starType != 25) as the DiaBill's additive flag (100f7fb8).
                Additive = _starType != 0x19,
            });
        }
    }

    /// <summary>
    /// starTypes 7/8: every sprite at the locator plus its direction times the radius, unrotated
    /// (FUN_1010640a plus the visual placed by FUN_10106306), size and colour shared, frame 0.
    /// </summary>
    void CollectRing(List<EffectBillboardBatch.Quad> dest)
    {
        _ring.Step(Age, Duration);

        Texture2D frameTex = _frames != null ? _frames.GetFrame(_atlas, _cols, _rows, 0) : _atlas;
        if (frameTex == null)
            return;

        uint argb = _ring.Argb;
        var color = new Color(
            ((argb >> 16) & 0xff) / 255f,
            ((argb >> 8) & 0xff) / 255f,
            (argb & 0xff) / 255f,
            ((argb >> 24) & 0xff) / 255f);
        Vector3 origin = WorldMatrix.GetColumn(3);
        float[] d = _ring.Directions;
        float r = _ring.Radius;
        for (int j = 0; j < StarsRing.Count; j++)
        {
            var pos = new Vector3(origin.x + d[j * 3] * r, origin.y + d[j * 3 + 1] * r, origin.z + d[j * 3 + 2] * r);
            dest.Add(new EffectBillboardBatch.Quad
            {
                Matrix = Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one),
                Scale = _ring.Size,
                Color = color,
                Texture = frameTex,
                Additive = _starType != 0x19,
            });
        }
    }
}
