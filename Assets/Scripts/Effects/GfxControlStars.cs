using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// typeCode 2004 — stock <c>_GfxControlStars_t</c> (ctor <c>Gamecode 100f74ad</c>, Process
/// <c>FUN_100f8491</c>): up to 128 sprites drawn by one <c>GfxVisualDiaBill</c>.
///
/// starTypes 2, 3, 4, 5, 6, 10, 11, 15, 16, 17, 18, 19, 20 and 22 are recovered in full and replayed call-for-call by
/// <see cref="StarsCase2"/>, <see cref="StarsCase3"/>, <see cref="StarsCase4"/>, <see cref="StarsSwirl"/> (5, 6, 11), <see cref="StarsCase10"/>,
/// <see cref="StarsBodySparks"/> (15), <see cref="StarsLineSparks"/> (16 to 20) and <see cref="StarsLimbSparks"/> (22), and starTypes 7 and 8
/// by <see cref="StarsRing"/>. Every other starType below is still the earlier approximation and has not
/// been checked against FUN_100f8491.
///
/// Stock Stars never creates a light: no code in the class range (100f7164..100fc404) reaches a light
/// API, so none is attached here.
/// </summary>
public sealed class GfxControlStars : GfxControl, ICatVertexReader
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
    readonly StarsCase4 _case4;
    readonly StarsSwirl _swirl;
    readonly StarsCase10 _case10;
    readonly StarsCase2 _case2;

    // Cases stepped from the locator position alone (4, 5, 6, 10, 11), and whether expiry drains them.
    readonly System.Action<float, Vector3> _originStep;
    readonly bool _originDrains;
    readonly StarsLineSparks _line;
    readonly StarsLimbSparks _limbs;
    readonly StarsBodySparks _body;
    readonly EffectLocator _bodyFrame;
    CatMeshDeformHost _bodyHost;
    readonly IStarsStockCase _stock;
    readonly EffectHitLocation _hitLocation;
    readonly EffectLocator[] _limbLocators;
    readonly float[] _limbAttach = new float[StarsLimbSparks.AttachIds.Length * 3];
    readonly StarsRing _ring;
    bool _ringTerminating;

    /// <summary>The replayed starType 3 state, or null for other starTypes. For debug tooling.</summary>
    public StarsCase3 StockCase3 => _case3;

    /// <summary>The replayed starType 4 state, or null for other starTypes. For debug tooling.</summary>
    public StarsCase4 StockCase4 => _case4;

    /// <summary>The replayed starType 5/6/11 state, or null. For debug tooling.</summary>
    public StarsSwirl StockSwirl => _swirl;

    /// <summary>The replayed starType 10 state, or null. For debug tooling.</summary>
    public StarsCase10 StockCase10 => _case10;

    /// <summary>The replayed starType 2 state, or null. For debug tooling.</summary>
    public StarsCase2 StockCase2 => _case2;

    /// <summary>The replayed starType 16-20 state, or null for other starTypes. For debug tooling.</summary>
    public StarsLineSparks StockLineSparks => _line;

    /// <summary>The replayed starType 22 state, or null for other starTypes. For debug tooling.</summary>
    public StarsLimbSparks StockLimbSparks => _limbs;

    /// <summary>The replayed starType 15 state, or null for other starTypes. For debug tooling.</summary>
    public StarsBodySparks StockBodySparks => _body;

    /// <summary>A starType 16-20 trail built on a hit location: stock sets it no duration and lets it run.</summary>
    public bool IsHitLocationTracer => _line != null && _hitLocation != null;

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
            _stock = _case3;
            // Stock expiry is handled per replayed step, not by the base timer.
            base.SetDuration(InfiniteDuration);
            return;
        }

        if (_starType == 4 && record != null)
        {
            // Loader FUN_100f72a9: 26 duration, 28 size (+0x16d4), 29 launch speed (+0x16d8), 30 life ms.
            _stockDuration = record.Field(26, 0f);
            _case4 = new StarsCase4(
                lifeSeconds: record.FieldInt(30, 0) / 1000f,
                size: record.Field(28, 0f),
                speed: record.Field(29, 0f),
                startArgb: new[] { record.Field(18), record.Field(19), record.Field(20), record.Field(21) },
                endArgb: new[] { record.Field(22), record.Field(23), record.Field(24), record.Field(25) },
                rand: () => Random.Range(0, 0x8000));
            _stock = _case4;
            _originStep = (age, o) => _case4.Step(age, o.x, o.y, o.z);
            _originDrains = true;
            base.SetDuration(InfiniteDuration);
            return;
        }

        if (StarsSwirl.Handles(_starType) && record != null)
        {
            // Loader FUN_100f72a9: 26 duration, 28 size curve, 29 ring radius, 30 life ms, 31 spring (int).
            _stockDuration = record.Field(26, 0f);
            _swirl = new StarsSwirl(
                _starType,
                lifeSeconds: record.FieldInt(30, 0) / 1000f,
                sizeCurve: record.Field(28, 0f),
                radius: record.Field(29, 0f),
                spring: record.FieldInt(31, 0),
                startArgb: new[] { record.Field(18), record.Field(19), record.Field(20), record.Field(21) },
                endArgb: new[] { record.Field(22), record.Field(23), record.Field(24), record.Field(25) },
                rand: () => Random.Range(0, 0x8000));
            _stock = _swirl;
            _originStep = (age, o) => _swirl.Step(age, o.x, o.y, o.z);
            _originDrains = true;
            base.SetDuration(InfiniteDuration);
            return;
        }

        if (_starType == 10 && record != null)
        {
            // Loader FUN_100f72a9: 11 base size, 26 duration, 28 life (s), 29 shell radius, 30 palette, 31 swing.
            _stockDuration = record.Field(26, 0f);
            _case10 = new StarsCase10(
                lifeSeconds: record.Field(28, 0f),
                baseSize: record.Field(11, 0f),
                radius: record.Field(29, 0f),
                paletteFlag: record.FieldInt(30, 0),
                swing: record.FieldInt(31, 0),
                startArgb: new[] { record.Field(18), record.Field(19), record.Field(20), record.Field(21) },
                endArgb: new[] { record.Field(22), record.Field(23), record.Field(24), record.Field(25) },
                rand: () => Random.Range(0, 0x8000));
            _stock = _case10;
            _originStep = (age, o) => _case10.Step(age, _stockDuration, o.x, o.y, o.z);
            _originDrains = false;
            base.SetDuration(InfiniteDuration);
            return;
        }

        if (_starType == 2 && record != null)
        {
            // Loader FUN_100f72a9: 26 duration, 28 size (+0x16d4). The lazy init (100f80ba) then sets the
            // duration to 1.5 s on the first call; a SetDuration after that call still lands.
            _stockDuration = record.Field(26, 0f);
            _case2 = new StarsCase2(
                size: record.Field(28, 0f),
                startArgb: new[] { record.Field(18), record.Field(19), record.Field(20), record.Field(21) },
                endArgb: new[] { record.Field(22), record.Field(23), record.Field(24), record.Field(25) },
                rand: () => Random.Range(0, 0x8000));
            _stock = _case2;
            _originStep = (age, o) =>
            {
                if (!_case2.Initialised)
                    _stockDuration = StarsCase2.LazyDuration;
                _case2.Step(age, _stockDuration, o.x, o.y, o.z);
            };
            _originDrains = false;
            base.SetDuration(InfiniteDuration);
            return;
        }

        if (StarsLineSparks.Handles(_starType) && record != null)
        {
            // Loader FUN_100f72a9: the duration is field 26. The hit location comes from
            // CreateGfxControl(id, hitLoc) (100f7d4a); without one the trail never spawns.
            _hitLocation = hitLocation;
            _stockDuration = record.Field(26, 0f);
            _line = new StarsLineSparks(
                _starType,
                lifeSeconds: record.FieldInt(30, 0) / 1000f,
                size: record.Field(28, 0f),
                radius: record.Field(29, 0f),
                startArgb: new[] { record.Field(18), record.Field(19), record.Field(20), record.Field(21) },
                endArgb: new[] { record.Field(22), record.Field(23), record.Field(24), record.Field(25) },
                rand: () => Random.Range(0, 0x8000));
            _stock = _line;
            base.SetDuration(InfiniteDuration);
            return;
        }

        if (_starType == 22 && record != null)
        {
            // Loader FUN_100f72a9: 26 duration, 28 radius (+0x16d4), 29 size (+0x16d8), 30 life ms,
            // 31 spawns per call (+0x16e0). The limbs are read through the locator's own dynel (10106078).
            _stockDuration = record.Field(26, 0f);
            _limbs = new StarsLimbSparks(
                lifeSeconds: record.FieldInt(30, 0) / 1000f,
                radius: record.Field(28, 0f),
                size: record.Field(29, 0f),
                allowance: record.FieldInt(31, 0),
                startArgb: new[] { record.Field(18), record.Field(19), record.Field(20), record.Field(21) },
                endArgb: new[] { record.Field(22), record.Field(23), record.Field(24), record.Field(25) },
                rand: () => Random.Range(0, 0x8000));
            _stock = _limbs;
            _limbLocators = new EffectLocator[StarsLimbSparks.AttachIds.Length];
            for (int i = 0; i < _limbLocators.Length; i++)
                _limbLocators[i] = locator?.WithAttach(StarsLimbSparks.AttachIds[i]);
            base.SetDuration(InfiniteDuration);
            return;
        }

        if (_starType == StarsBodySparks.StarType && record != null)
        {
            // Loader FUN_100f72a9: 26 duration, 28 size (+0x16d4), 29 speed (+0x16d8), 30 life ms. The
            // render's scale (+0x16ec) is 1 here: the baked vertices already carry the renderer's scale.
            _stockDuration = record.Field(26, 0f);
            _body = new StarsBodySparks(
                lifeSeconds: record.FieldInt(30, 0) / 1000f,
                size: record.Field(28, 0f),
                speed: record.Field(29, 0f),
                scale: 1f,
                startArgb: new[] { record.Field(18), record.Field(19), record.Field(20), record.Field(21) },
                endArgb: new[] { record.Field(22), record.Field(23), record.Field(24), record.Field(25) },
                rand: () => Random.Range(0, 0x8000));
            _stock = _body;
            // The dynel's own position and rotation (Vehicle_t::GetGlobalPos / n3Dynel_t::GetGlobalRot):
            // attach 0, the CAT root frame, without the record's template.
            _bodyFrame = locator?.WithAttach(0);
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
        if (_stock != null)
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

        if (_originStep != null)
        {
            ProcessStockAtOrigin(dt);
            return;
        }

        if (_line != null)
        {
            ProcessStockLine(dt);
            return;
        }

        if (_limbs != null)
        {
            ProcessStockLimbs(dt);
            return;
        }

        if (_body != null)
        {
            ProcessStockBody(dt);
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
    /// starTypes 4, 6, 10 and 11, replayed like case 3 at <see cref="EffectFrameRate.StockProcessHz"/>
    /// from the locator position (<c>1010640a</c>), which follows the bone. The first call arms at age 0
    /// and still runs the case. When 0 &lt;= duration &lt; age, types whose <c>0x100fc374</c> entry is 0
    /// (4, 6, 11) stop spawning and get 5 s more (<c>100f84ed</c>) and go the next time; type 10 is
    /// ready at once. Terminating with nothing alive readies any of them (<c>100f884e</c> / <c>100f90c1</c>
    /// / <c>100f8c23</c>).
    /// </summary>
    void ProcessStockAtOrigin(float dt)
    {
        Vector3 origin = WorldMatrix.GetColumn(3);

        if (!_stockArmed)
        {
            _stockArmed = true;
            _originStep(0f, origin);
            return;
        }

        float step = EffectFrameRate.StockProcessSeconds;
        int steps = EffectFrameRate.TakeFixedSteps(ref _stockCarry, dt, step, MaxStockStepsPerFrame);
        for (int s = 0; s < steps; s++)
        {
            _stockAge += step;

            bool expired = _stockDuration >= 0f && _stockDuration < _stockAge;
            if (expired && !_originDrains)
            {
                ReadyFlag = true;
                return;
            }
            if (expired && !_stock.Terminating)
            {
                _stock.Terminating = true;
                _stockDuration += StockDrainSeconds;
                expired = false;
            }

            _originStep(_stockAge, origin);

            if (expired || _stock.Drained)
            {
                ReadyFlag = true;
                return;
            }
        }
    }

    /// <summary>
    /// starTypes 16 to 20, replayed like case 3 at <see cref="EffectFrameRate.StockProcessHz"/>. The
    /// first call arms at age 0 and still runs the case. Once <c>_GfxControl_t::Process</c> finds
    /// 0 &lt;= duration &lt; age the control is ready and all of them leave at once (the <c>100f84d6</c>
    /// table sends them straight out, with no drain as case 3 gets), so the whole trail goes with it.
    /// Terminating stops the spawns, and a step that then finds nothing alive readies it (<c>100fc2f0</c>).
    /// </summary>
    void ProcessStockLine(float dt)
    {
        if (!_stockArmed)
        {
            _stockArmed = true;
            StepStockLine();
            if (_line.Drained)
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

            StepStockLine();
            if (_line.Drained)
            {
                ReadyFlag = true;
                return;
            }
        }
    }

    void StepStockLine()
    {
        Vector3 start = default, end = default;
        bool found = _hitLocation != null && _hitLocation.TryGetEndpoints(out start, out end);
        _line.Step(_stockAge, _stockDuration, found, start.x, start.y, start.z, end.x, end.y, end.z);
    }

    /// <summary>
    /// starType 22, replayed like case 3 at <see cref="EffectFrameRate.StockProcessHz"/>, with case 3's
    /// expiry: the first time 0 &lt;= duration &lt; age it stops spawning and gets 5 s more
    /// (<c>100f84ed</c>), the second time it goes; terminating with nothing alive readies it
    /// (<c>100f884e</c>). The twelve limb points are read once per frame; stock reads them every call.
    /// </summary>
    void ProcessStockLimbs(float dt)
    {
        ReadLimbs();

        if (!_stockArmed)
        {
            _stockArmed = true;
            _limbs.Step(0f, _limbAttach);
            return;
        }

        float step = EffectFrameRate.StockProcessSeconds;
        int steps = EffectFrameRate.TakeFixedSteps(ref _stockCarry, dt, step, MaxStockStepsPerFrame);
        for (int s = 0; s < steps; s++)
        {
            _stockAge += step;

            bool expired = _stockDuration >= 0f && _stockDuration < _stockAge;
            if (expired && !_limbs.Terminating)
            {
                _limbs.Terminating = true;
                _stockDuration += StockDrainSeconds;
                expired = false;
            }

            _limbs.Step(_stockAge, _limbAttach);

            if (expired || _limbs.Drained)
            {
                ReadyFlag = true;
                return;
            }
        }
    }

    /// <summary>
    /// starType 15, replayed like case 22 at <see cref="EffectFrameRate.StockProcessHz"/>, with case 3's
    /// expiry (the <c>0x100fc374</c> entry is 0): stop spawning and 5 s more (<c>100f84ed</c>), then go;
    /// terminating with nothing alive readies it (<c>100f90c1</c>). The lazy init (<c>100f811f</c>) hooks the
    /// host's CAT mesh; while there is none, stock returns before the age moves and tries again next call.
    /// The dynel's frame is read once per frame; stock reads it every call.
    /// </summary>
    void ProcessStockBody(float dt)
    {
        if (_bodyHost == null)
        {
            if (Locator == null || !Locator.TryGetHighlightRoot(out GameObject root) || root == null
                || !CatMeshDeformHost.HasCatMesh(root))
                return;
            _bodyHost = CatMeshDeformHost.For(root);
            _bodyHost.AddReader(this);
        }

        StarsBodySparks.Frame frame = BodyFrame();
        if (!_stockArmed)
        {
            _stockArmed = true;
            _body.Step(0f, frame);
            return;
        }

        float step = EffectFrameRate.StockProcessSeconds;
        int steps = EffectFrameRate.TakeFixedSteps(ref _stockCarry, dt, step, MaxStockStepsPerFrame);
        for (int s = 0; s < steps; s++)
        {
            _stockAge += step;

            bool expired = _stockDuration >= 0f && _stockDuration < _stockAge;
            if (expired && !_body.Terminating)
            {
                _body.Terminating = true;
                _stockDuration += StockDrainSeconds;
                expired = false;
            }

            _body.Step(_stockAge, frame);

            if (expired || _body.Drained)
            {
                ReadyFlag = true;
                return;
            }
        }
    }

    /// <summary>The dynel's position and rotation as the body sparks use them; the origin if it's gone.</summary>
    StarsBodySparks.Frame BodyFrame()
    {
        if (_bodyFrame == null || !_bodyFrame.TryResolve(out Matrix4x4 m))
            return StarsBodySparks.Frame.At(0f, 0f, 0f);

        Quaternion q = m.rotation;
        Vector3 p = m.GetColumn(3), x = q * Vector3.right, y = q * Vector3.up, z = q * Vector3.forward;
        return new StarsBodySparks.Frame
        {
            Px = p.x, Py = p.y, Pz = p.z,
            Xx = x.x, Xy = x.y, Xz = x.z,
            Yx = y.x, Yy = y.y, Yz = y.z,
            Zx = z.x, Zy = z.y, Zz = z.z,
        };
    }

    // The group being read, for SampleVertex.
    List<Vector3> _groupPositions;
    List<Vector3> _groupNormals;
    Matrix4x4 _groupToBody;
    System.Func<int, StarsBodySparks.Vertex> _vertexAt;

    /// <summary>
    /// The vertex callback <c>100f740b</c>. Stock gets the skinned vertices in the mesh's own space, which
    /// is the dynel's frame; the port bakes them in world space, so each one kept is taken back into the
    /// dynel's frame as it stands now. The next call places it with the frame it has then, as stock does.
    /// </summary>
    public void ReadGroup(int count, int baseIndex, int total, List<Vector3> positions, List<Vector3> normals, Matrix4x4 toWorld)
    {
        if (_body == null || _bodyFrame == null || !_bodyFrame.TryResolve(out Matrix4x4 m))
            return;

        _groupPositions = positions;
        _groupNormals = normals;
        _groupToBody = Matrix4x4.TRS(m.GetColumn(3), m.rotation, Vector3.one).inverse * toWorld;
        _vertexAt ??= SampleVertex;
        _body.ReadGroup(count, baseIndex, total, _vertexAt);
    }

    StarsBodySparks.Vertex SampleVertex(int index)
    {
        Vector3 p = _groupToBody.MultiplyPoint3x4(_groupPositions[index]);
        Vector3 n = _groupToBody.MultiplyVector(_groupNormals[index]);
        return new StarsBodySparks.Vertex { X = p.x, Y = p.y, Z = p.z, NX = n.x, NY = n.y, NZ = n.z };
    }

    protected override void OnReleased(bool immediate) => _bodyHost?.RemoveReader(this);

    /// <summary>
    /// <c>10106078</c> for each of <see cref="StarsLimbSparks.AttachIds"/> on the locator's dynel. A point
    /// the host cannot resolve falls back to the locator itself.
    /// </summary>
    void ReadLimbs()
    {
        Vector3 fallback = WorldMatrix.GetColumn(3);
        for (int i = 0; i < _limbLocators.Length; i++)
        {
            Vector3 p = fallback;
            EffectLocator limb = _limbLocators[i];
            if (limb != null && limb.TryResolve(out Matrix4x4 m))
                p = m.GetColumn(3);
            _limbAttach[i * 3] = p.x;
            _limbAttach[i * 3 + 1] = p.y;
            _limbAttach[i * 3 + 2] = p.z;
        }
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
        if (_stock != null)
        {
            _stock.Terminating = true;
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

        if (_stock != null)
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
        StarsCase3.Sprite[] sprites = _stock.Sprites;
        for (int i = 0; i < sprites.Length; i++)
        {
            if (!sprites[i].Visible)
                continue;
            StarsCase3.Sprite s = _stock.Blend(i, t);

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
