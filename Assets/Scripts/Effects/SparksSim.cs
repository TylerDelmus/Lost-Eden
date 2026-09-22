using System;

/// <summary>
/// Stock <c>_GfxControlSparks_t</c> (typeCode 1018, 0x3fa; vftable <c>Gamecode 1016db8c</c>, loader
/// <c>100f1758</c>, init <c>100f1939</c>, spawn <c>100f1bbe</c>, Process <c>100f24c8</c>) and the
/// DisplaySystem <c>GfxVisualSprite2Type0</c> it fills (<see cref="Sprite2Type0Visual"/>). One
/// <see cref="Step"/> is one stock Process call.
///
/// Fields: 0 flags (0x200 readies once the pool is empty, 0x800 alpha blend instead of additive),
/// 8 duration, 9 material, 10 sprites per second, 12/13 width start/end, 14/15 height start/end,
/// 16-19 / 20-23 start / end colour A,R,G,B, 24 <c>rand()</c> mask gating a spawn call, 25/26 angle a,
/// 27/28 angle b, 29/30 speed, 31 burst, 32 speed scale, 34/35 life, 36/37 the wind band's bottom and top
/// above the emitter, 38 wind mode, 39 wind scale, 40 gravity (y, per second²). 11 and 33 are loaded but
/// unused.
///
/// Init: a pool of max(_ftol(field 10 * 1.5 * field 35), field 31), and the spawn counter starts at
/// -field 31. Per call: when (rand() &amp; field 24) == 0 and (duration &lt; 0 or age &lt; duration -
/// field 35), sprites are spawned until the counter reaches _ftol(field 10 * age); then every sprite
/// moves. So a record with rate 0 is one burst of field 31 on the first call.
///
/// Spawn, from the R250 source (<c>1013dec9</c>) in this order: a, b, speed s, life, wind scale. The
/// velocity is (X cos a cos b + Y sin b + Z sin a cos b) * s * field 32, X/Y/Z the locator's world axes
/// (unit axes in local mode), from the locator's world position (zero in local mode). Width, height and
/// colour ramp linearly to their end values over the life; the frame runs first to last over it.
///
/// Each sprite takes the wind mode (field 38), its band (fields 36/37 above the emitter's y at spawn) and
/// its scale, and field 40 is the visual's gravity; the visual moves them (ProcessSprites). The wind is
/// the effect handler's GetWind (<see cref="EffectWindSim"/>).
///
/// No Unity dependency so the recovered maths can be asserted from plain unit tests.
/// </summary>
public sealed class SparksSim
{
    public const int FlagLocal = 2;
    public const int FlagReadyWhenEmpty = 0x200;
    public const int FlagAlphaBlend = 0x800;

    readonly int _flags;
    readonly float _rate;
    readonly float _w0, _w1, _h0, _h1;
    readonly float[] _start = new float[4];
    readonly float[] _end = new float[4];
    readonly int _randMask;
    readonly float _a0, _a1, _b0, _b1, _s0, _s1;
    readonly int _burst;
    readonly float _speedScale;
    readonly float _life0, _life1;
    readonly float _windScale;
    readonly float _firstFrame, _lastFrame;
    readonly Func<float> _rand01;
    readonly Func<int> _crtRand;
    readonly Sprite2Type0Visual _visual;
    int _spawned;

    /// <summary>Stock +0x10: field 8, or what SetDuration / TerminateGracefully put there.</summary>
    public float Duration { get; set; }

    /// <summary>Visual +0x228: sprites alive after the last <see cref="Step"/>.</summary>
    public int LiveCount => _visual.LiveCount;

    public int Flags => _flags;
    public bool Additive => (_flags & FlagAlphaBlend) == 0;
    public bool Local => (_flags & FlagLocal) != 0;
    public float Life1 => _life1;
    public Sprite2Type0Visual.Sprite[] Sprites => _visual.Sprites;

    /// <summary>Fields 36-38: the wind band and mode.</summary>
    public float WindLow { get; }
    public float WindHigh { get; }
    public int WindMode { get; }

    /// <param name="rand01">Stock's R250 source at 0x102ead20: uniform in [0, 1).</param>
    /// <param name="crtRand">Stock <c>rand()</c>, 0..0x7fff.</param>
    public SparksSim(float[] fields, int firstFrame, int lastFrame, Func<float> rand01, Func<int> crtRand)
    {
        _rand01 = rand01 ?? throw new ArgumentNullException(nameof(rand01));
        _crtRand = crtRand ?? throw new ArgumentNullException(nameof(crtRand));

        _flags = Int(fields, 0);
        Duration = F(fields, 8);
        _rate = F(fields, 10);
        _w0 = F(fields, 12);
        _w1 = F(fields, 13);
        _h0 = F(fields, 14);
        _h1 = F(fields, 15);
        for (int i = 0; i < 4; i++)
        {
            _start[i] = F(fields, 16 + i);
            _end[i] = F(fields, 20 + i);
        }
        _randMask = Int(fields, 24);
        _a0 = F(fields, 25);
        _a1 = F(fields, 26);
        _b0 = F(fields, 27);
        _b1 = F(fields, 28);
        _s0 = F(fields, 29);
        _s1 = F(fields, 30);
        _burst = Int(fields, 31);
        _speedScale = F(fields, 32);
        _life0 = F(fields, 34);
        _life1 = F(fields, 35);
        WindLow = F(fields, 36);
        WindHigh = F(fields, 37);
        WindMode = Int(fields, 38);
        _windScale = F(fields, 39);
        _firstFrame = firstFrame;
        _lastFrame = lastFrame;

        // 100f1aa5: the pool.
        int pool = (int)(_rate * 1.5 * _life1);
        if (pool < _burst)
            pool = _burst;
        // 100f199e: field 40 is the visual's gravity.
        _visual = new Sprite2Type0Visual(pool, _rand01) { Gravity = F(fields, 40) };
        // 100f1ba5: the counter starts a burst below zero.
        _spawned = -_burst;
    }

    /// <summary>Slot 11 (<c>100f14a2</c>): the start colour, for later spawns.</summary>
    public void SetStartColor(float a, float r, float g, float b)
    {
        _start[0] = a; _start[1] = r; _start[2] = g; _start[3] = b;
    }

    /// <summary>Slot 12 (<c>100f1549</c>): the end colour, for later spawns.</summary>
    public void SetStopColor(float a, float r, float g, float b)
    {
        _end[0] = a; _end[1] = r; _end[2] = g; _end[3] = b;
    }

    /// <summary>Slot 6 (<c>100f1488</c>): duration = age + field 35.</summary>
    public void TerminateGracefully(float age) => Duration = _life1 + age;

    /// <summary>
    /// One Process body after the base timer, at <paramref name="age"/> with delta <paramref name="dt"/>.
    /// <paramref name="ox"/>.. is the locator's world-mode position and <paramref name="axes"/> its
    /// world-mode X, Y, Z axes (9 floats); pass zero and null in local mode. Returns true once stock
    /// would be ready (flag 0x200 with nothing alive).
    /// </summary>
    public bool Step(float age, float dt, float ox, float oy, float oz, float[] axes)
        => Step(age, dt, ox, oy, oz, axes, 0f, 0f, 0f);

    /// <summary>
    /// As above, with the effect handler's wind (<c>GetWind</c>) for the wind modes.
    /// </summary>
    public bool Step(float age, float dt, float ox, float oy, float oz, float[] axes, float windX, float windY, float windZ)
    {
        if ((_crtRand() & _randMask) == 0
            && (!(0f <= Duration) || age < Duration - _life1))
        {
            int target = (int)(_rate * age);
            while (_spawned < target)
            {
                _spawned++;
                Spawn(ox, oy, oz, axes);
            }
        }

        _visual.ProcessSprites(dt, windX, windY, windZ);
        return (_flags & FlagReadyWhenEmpty) != 0 && LiveCount == 0;
    }

    void Spawn(float ox, float oy, float oz, float[] axes)
    {
        float a = Range(_a0, _a1);
        float b = Range(_b0, _b1);
        float s = Range(_s0, _s1);
        double cbs = (float)Math.Cos(b) * (double)s;
        float kz = (float)((float)Math.Sin(a) * cbs);
        float ky = (float)Math.Sin(b) * s;
        float kx = (float)((float)Math.Cos(a) * cbs);

        float vx, vy, vz;
        if (axes == null)
        {
            vx = kx; vy = ky; vz = kz;
        }
        else
        {
            vx = axes[0] * kx + axes[3] * ky + axes[6] * kz;
            vy = axes[1] * kx + axes[4] * ky + axes[7] * kz;
            vz = axes[2] * kx + axes[5] * ky + axes[8] * kz;
        }
        vx *= _speedScale;
        vy *= _speedScale;
        vz *= _speedScale;

        float life = Range(_life0, _life1);
        float inv = 1f / life;
        float wind = (float)((_rand01() * 0.8999999761581421 + 0.10000000149011612) * _windScale);

        // NewSprite 100261de.
        _visual.NewSprite(new Sprite2Type0Visual.Sprite
        {
            X = ox, Y = oy, Z = oz,
            VX = vx, VY = vy, VZ = vz,
            Width = _w0,
            Height = _h0,
            DWidth = (_w1 - _w0) * inv,
            DHeight = (_h1 - _h0) * inv,
            Life = life,
            A = _start[0], R = _start[1], G = _start[2], B = _start[3],
            DA = (_end[0] - _start[0]) * inv,
            DR = (_end[1] - _start[1]) * inv,
            DG = (_end[2] - _start[2]) * inv,
            DB = (_end[3] - _start[3]) * inv,
            Frame = _firstFrame,
            FrameRate = (_lastFrame - _firstFrame) * inv,
            WindMode = WindMode,
            // 100f1dbb: the band sits on the emitter's (world-mode) height at spawn.
            WindLow = WindLow + oy,
            WindHigh = WindHigh + oy,
            WindScale = wind,
        });
    }

    float Range(float lo, float hi) => (float)(_rand01() * (hi - lo) + lo);

    static float F(float[] f, int i) => f != null && i >= 0 && i < f.Length ? f[i] : 0f;

    static int Int(float[] f, int i) => f != null && i >= 0 && i < f.Length ? BitConverter.SingleToInt32Bits(f[i]) : 0;
}
