using System;

/// <summary>
/// Stock <c>_GfxControlNano0_t</c> (typeCode 1007, 0x3ef; vftable <c>Gamecode 1016d47c</c>, loader
/// <c>100e67f8</c>, init <c>100e7146</c>, spawns <c>100e69ca</c> / <c>100e6ca4</c>, Process <c>100e6f76</c>)
/// and the DisplaySystem <c>GfxVisualSprite2Type0</c> it fills (<see cref="Sprite2Type0Visual"/>). One
/// <see cref="Step"/> is one stock Process call. Tracer3 (<see cref="Tracer3Sim"/>) drives one along its
/// line, and the sprites it drops on the way are the tracer's trail.
///
/// It is Sparks (<see cref="SparksSim"/>) with three differences:
/// <list type="bullet">
/// <item>The wind fields sit one lower: the loader reads field 35 twice (into the life, +0xa0, and the
/// band bottom, +0xa4), then 36 the band top, 37 the wind mode and 38 the wind scale. There's no gravity
/// field; the visual's stays 0.</item>
/// <item>Init fires the burst (field 31) at once, from the locator along its axes, as Sparks' spawn
/// does (<c>100e69ca</c>). The Process counter starts at 0, not at -field 31.</item>
/// <item>Process spreads its due sprites (_ftol(field 10 * age) less those spawned) evenly along the
/// path the emitter moved since the last call: with d the move and n the count, sprite k (1..n) starts at
/// prev + unit(d) * k |d| / n (<c>100e7041</c>..<c>100e7099</c>), its direction turned by the locator's
/// world matrix (<c>100dcd23</c>).</item>
/// </list>
/// A lost locator sets duration = age + field 35 on every call. Flags: 0x200 readies it once the pool is
/// empty, 0x800 alpha blend instead of additive; field 0 bits 0-2 are the locator's.
///
/// No Unity dependency so the recovered maths can be asserted from plain unit tests.
/// </summary>
public sealed class Nano0Sim
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
    readonly float _speedScale;
    readonly float _life0, _life1;
    readonly float _windScale;
    readonly float _firstFrame, _lastFrame;
    readonly Func<float> _rand01;
    readonly Func<int> _crtRand;
    readonly Sprite2Type0Visual _visual;
    int _spawned;
    float _prevX, _prevY, _prevZ;

    /// <summary>Stock +0x10: field 8, or what SetDuration / TerminateGracefully put there.</summary>
    public float Duration { get; set; }

    /// <summary>Visual +0x228: sprites alive after the last <see cref="Step"/>.</summary>
    public int LiveCount => _visual.LiveCount;

    public int Flags => _flags;
    public bool Additive => (_flags & FlagAlphaBlend) == 0;
    public bool Local => (_flags & FlagLocal) != 0;
    public float Life1 => _life1;
    public Sprite2Type0Visual.Sprite[] Sprites => _visual.Sprites;

    /// <summary>Fields 35 (read a second time), 36 and 37: the wind band and mode.</summary>
    public float WindLow { get; }
    public float WindHigh { get; }
    public int WindMode { get; }

    /// <summary>
    /// Load and init. <paramref name="ox"/>.. is the locator's world-mode position (zero in local mode)
    /// and <paramref name="axes"/> its world X, Y, Z axes (9 floats; null for the unit axes).
    /// </summary>
    /// <param name="rand01">Stock's R250 source at 0x102ead20: uniform in [0, 1).</param>
    /// <param name="crtRand">Stock <c>rand()</c>, 0..0x7fff.</param>
    public Nano0Sim(
        float[] fields,
        int firstFrame,
        int lastFrame,
        Func<float> rand01,
        Func<int> crtRand,
        float ox,
        float oy,
        float oz,
        float[] axes)
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
        int burst = Int(fields, 31);
        _speedScale = F(fields, 32);
        _life0 = F(fields, 34);
        _life1 = F(fields, 35);
        // 100e6985 / 100e698b: field 35 goes to +0xa0 and again to +0xa4.
        WindLow = F(fields, 35);
        WindHigh = F(fields, 36);
        WindMode = Int(fields, 37);
        _windScale = F(fields, 38);
        _firstFrame = firstFrame;
        _lastFrame = lastFrame;

        // 100e72ac: the pool.
        int pool = (int)(_rate * 1.5 * _life1);
        if (pool < burst)
            pool = burst;
        _visual = new Sprite2Type0Visual(pool, _rand01);

        // 100e73ac: the previous position, then the burst (100e73c6).
        _prevX = ox;
        _prevY = oy;
        _prevZ = oz;
        for (int i = 0; i < burst; i++)
            SpawnAtLocator(ox, oy, oz, axes);
    }

    /// <summary>Slot 11 (<c>100e66cf</c>): the start colour, for later spawns.</summary>
    public void SetStartColor(float a, float r, float g, float b)
    {
        _start[0] = a; _start[1] = r; _start[2] = g; _start[3] = b;
    }

    /// <summary>Slot 12 (<c>100e66ee</c>): the end colour, for later spawns.</summary>
    public void SetStopColor(float a, float r, float g, float b)
    {
        _end[0] = a; _end[1] = r; _end[2] = g; _end[3] = b;
    }

    /// <summary>Slot 6 (<c>100e66b5</c>), and a lost locator every call: duration = age + field 35.</summary>
    public void TerminateGracefully(float age) => Duration = _life1 + age;

    /// <summary>
    /// One Process body after the base timer, at <paramref name="age"/> with delta <paramref name="dt"/>.
    /// <paramref name="ox"/>.. is the locator's world-mode position now, <paramref name="axes"/> its world
    /// axes (null for the unit axes), and the wind the handler's GetWind. Returns true once stock would be
    /// ready (flag 0x200 with nothing alive).
    /// </summary>
    public bool Step(float age, float dt, float ox, float oy, float oz, float[] axes, float windX, float windY, float windZ)
    {
        if ((_crtRand() & _randMask) == 0
            && (!(0f <= Duration) || age < Duration - _life1))
        {
            int target = (int)(_rate * age);
            float dx = ox - _prevX, dy = oy - _prevY, dz = oz - _prevZ;
            float dist = (float)Math.Sqrt((float)(dx * dx + dy * dy + dz * dz));
            if (!(dx == 0f && dy == 0f && dz == 0f))
            {
                // 100439aa: set to length 1.
                float scale = (float)(1.0 / dist);
                dx *= scale;
                dy *= scale;
                dz *= scale;
            }

            int n = target - _spawned;
            float step = dist;
            if (n > 0)
                step = (float)((double)dist / n);
            float along = step;
            for (int k = 0; k < n; k++)
            {
                SpawnAt(_prevX + dx * along, _prevY + dy * along, _prevZ + dz * along, axes);
                along = (float)((double)along + step);
            }
            _spawned = target;
        }

        _visual.ProcessSprites(dt, windX, windY, windZ);

        _prevX = ox;
        _prevY = oy;
        _prevZ = oz;
        return (_flags & FlagReadyWhenEmpty) != 0 && LiveCount == 0;
    }

    /// <summary>
    /// <c>100e69ca</c>, the init burst: at the locator's world-mode position, along its axes, as Sparks'
    /// spawn (the cos b * speed product kept in double).
    /// </summary>
    void SpawnAtLocator(float ox, float oy, float oz, float[] axes)
    {
        float a = Range(_a0, _a1);
        float b = Range(_b0, _b1);
        float s = Range(_s0, _s1);
        double cbs = (float)Math.Cos(b) * (double)s;
        float kz = (float)((float)Math.Sin(a) * cbs);
        float ky = (float)Math.Sin(b) * s;
        float kx = (float)((float)Math.Cos(a) * cbs);
        NewSprite(ox, oy, oz, kx, ky, kz, axes);
    }

    /// <summary>
    /// <c>100e6ca4</c>, Process' spawn at a point of the path: the direction in the locator's frame
    /// (the cos b * speed product stored as a float), turned by its world matrix.
    /// </summary>
    void SpawnAt(float px, float py, float pz, float[] axes)
    {
        float a = Range(_a0, _a1);
        float b = Range(_b0, _b1);
        float s = Range(_s0, _s1);
        float cbs = (float)((float)Math.Cos(b) * (double)s);
        float kx = (float)((float)Math.Cos(a) * (double)cbs);
        float ky = (float)((float)Math.Sin(b) * (double)s);
        float kz = (float)((float)Math.Sin(a) * (double)cbs);
        NewSprite(px, py, pz, kx, ky, kz, axes);
    }

    void NewSprite(float px, float py, float pz, float kx, float ky, float kz, float[] axes)
    {
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
            X = px, Y = py, Z = pz,
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
            // The band sits on the spawn point's height.
            WindLow = WindLow + py,
            WindHigh = WindHigh + py,
            WindScale = wind,
        });
    }

    float Range(float lo, float hi) => (float)(_rand01() * (hi - lo) + lo);

    static float F(float[] f, int i) => f != null && i >= 0 && i < f.Length ? f[i] : 0f;

    static int Int(float[] f, int i) => f != null && i >= 0 && i < f.Length ? BitConverter.SingleToInt32Bits(f[i]) : 0;
}
