using System;

/// <summary>
/// Stock <c>_GfxControlElectra_t</c> (typeCode 2006 / 0x7d6, vftable <c>Gamecode 1016cbbc</c>), mode 1:
/// a shell of flat, textured sparks around the host (43452, nano 150501's Nullity Sphere buff). Process
/// <c>100d9de5</c>, mode 1 at <c>100da0c0</c>; drawn by DisplaySystem <c>GfxVisualElectra</c> (build
/// <c>100118d3</c>).
///
/// Fields (loader <c>100d97ba</c>): 0 flags, 9 material, 10 mode, 11-17 (not read by mode 1), 18-25 colour
/// ramp, 26 duration (field 8 is read, then overwritten), 27 (unused here), 28 spark size, 29 shell
/// radius, 30 spark life in ms (int bits), 31 (int, unused here). Modes above 2 get duration 0.
///
/// Mode 1, per Process at age a: n = _ftol(a * 28 / life) sparks are owed in total and at most 2 open
/// per call. A spark picks a random unit direction d (<c>100d3005</c>), stretches its y by 1.5, sits
/// at the locator plus d * field 29, and faces along the normalised stretched d: its quad spans
/// w' * field 28 and (w' x d) * field 28, where w' is <c>FindPerpendicular(d)</c> turned by -r about d,
/// r = (rand() &amp; 0x7fff) / 9990.2, and the first axis is negated on a coin flip. It lives field 30
/// / 1000 seconds and plays atlas frames _ftol(t * 16), t its life fraction. Every spark takes the
/// colour ramp at a / duration. Like Stars, the spawn call leaves the sprite record as it was.
///
/// Expiry (top of Process): once 0 &lt;= duration &lt; age, a mode 0/1 control that is not terminating yet
/// terminates and gets one spark life more (<c>100d9e13</c>); the next expiry ends it. Terminating
/// (slot 6, <c>100d968d</c>) only stops the spawns.
///
/// No Unity dependency so the recovered maths can be asserted from plain unit tests.
/// </summary>
public sealed class ElectraSim
{
    public const int ShellMode = 1;
    public const int SlotCount = 64;

    /// <summary>Slots one Process may open: <c>100da0eb PUSH 2</c>.</summary>
    public const int MaxSpawnsPerStep = 2;

    /// <summary>Sparks owed per spark life: <c>100da0cf FDIVR 28.0</c>.</summary>
    const double SparksPerLife = 28.0;

    /// <summary>The spin divisor, <c>100da2e4</c>: rand() &amp; 0x7fff over this is 0..3.28 radians.</summary>
    const double SpinScale = 9990.2001953125;

    const double StretchY = 1.5;
    const float UnspawnedTimer = -100f;

    /// <summary>One <c>Electra_n::Sprite_t</c> (0x30 bytes): centre, two full-length axes, colour, frame.</summary>
    public struct Sprite
    {
        public float X, Y, Z;
        /// <summary>The quad's first axis (+0xc); the visual spans half of it each way.</summary>
        public float Ax, Ay, Az;
        /// <summary>The quad's second axis (+0x18), up in the texture.</summary>
        public float Cx, Cy, Cz;
        public uint Argb;
        public int Frame;
        public bool Visible;
    }

    readonly float _life;
    readonly float _size;
    readonly float _radius;
    readonly float[] _start = new float[4];
    readonly float[] _end = new float[4];
    readonly Func<int> _rand;

    // +0x38 offsets, +0x338 per-slot frames (rows w', d, u), +0xd38 timers, +0xe38 mirror flags.
    readonly float[] _ox = new float[SlotCount], _oy = new float[SlotCount], _oz = new float[SlotCount];
    readonly float[] _wx = new float[SlotCount], _wy = new float[SlotCount], _wz = new float[SlotCount];
    readonly float[] _ux = new float[SlotCount], _uy = new float[SlotCount], _uz = new float[SlotCount];
    readonly float[] _timer = new float[SlotCount];
    readonly bool[] _mirror = new bool[SlotCount];
    readonly Sprite[] _sprites = new Sprite[SlotCount];
    int _owed;

    public int Mode { get; }

    /// <summary>Stock +0x10: field 26, or whatever SetDuration set.</summary>
    public float Duration { get; set; }

    /// <summary>Stock +0xe78.</summary>
    public bool Terminating { get; set; }

    public float Life => _life;
    public Sprite[] Sprites => _sprites;

    /// <param name="rand">Stock <c>rand()</c>: 0..0x7fff.</param>
    public ElectraSim(float[] fields, Func<int> rand)
    {
        _rand = rand ?? throw new ArgumentNullException(nameof(rand));
        Mode = Int(fields, 10);
        Duration = F(fields, 26);
        _size = F(fields, 28);
        _radius = F(fields, 29);
        _life = (float)(Int(fields, 30) / 1000.0);
        for (int c = 0; c < 4; c++)
        {
            _start[c] = F(fields, 18 + c);
            _end[c] = F(fields, 22 + c);
        }

        if (Mode > 2)
        {
            Duration = 0f;
            return;
        }

        for (int i = 0; i < SlotCount; i++)
        {
            _timer[i] = UnspawnedTimer;
            _sprites[i].Argb = 0xffffffffu;
        }
    }

    /// <summary>Stock slot 11 (<c>100d96a2</c>).</summary>
    public void SetStartColor(float a, float r, float g, float b)
    {
        _start[0] = a; _start[1] = r; _start[2] = g; _start[3] = b;
    }

    /// <summary>Stock slot 12 (<c>100d96d5</c>).</summary>
    public void SetStopColor(float a, float r, float g, float b)
    {
        _end[0] = a; _end[1] = r; _end[2] = g; _end[3] = b;
    }

    /// <summary>
    /// The expiry at the top of one Process at <paramref name="age"/>. True when the control is done.
    /// </summary>
    public bool Expire(float age)
    {
        if (!(0f <= Duration && Duration < age))
            return false;
        if (Mode <= 1 && !Terminating)
        {
            Terminating = true;
            Duration = (float)((double)_life + Duration);
            return false;
        }
        return true;
    }

    /// <summary>
    /// The mode 1 body of one Process at <paramref name="age"/>, with the locator at (lx, ly, lz).
    /// </summary>
    public void Step(float age, float lx, float ly, float lz)
    {
        float progress = (float)((double)age / Duration);
        float rate = (float)(SparksPerLife / _life);
        int owed = (int)((double)rate * age);
        int budget = owed - _owed;
        _owed = owed;
        if (budget > MaxSpawnsPerStep)
            budget = MaxSpawnsPerStep;

        for (int i = 0; i < SlotCount; i++)
        {
            if (age < _timer[i])
            {
                float t = (float)(((double)_life - ((double)_timer[i] - age)) / _life);
                ref Sprite s = ref _sprites[i];
                float ax = _wx[i] * _size, ay = _wy[i] * _size, az = _wz[i] * _size;
                if (_mirror[i])
                {
                    ax = -ax;
                    ay = -ay;
                    az = -az;
                }

                s.X = lx + _ox[i];
                s.Y = ly + _oy[i];
                s.Z = lz + _oz[i];
                s.Ax = ax;
                s.Ay = ay;
                s.Az = az;
                s.Cx = _ux[i] * _size;
                s.Cy = _uy[i] * _size;
                s.Cz = _uz[i] * _size;
                s.Argb = StockColorRamp.Eval(_start, _end, progress);
                s.Frame = (int)((double)t * 16.0);
                s.Visible = true;
                continue;
            }

            if (budget == 0 || Terminating)
            {
                _sprites[i].Visible = false;
                _timer[i] = UnspawnedTimer;
                continue;
            }

            Spawn(i, age);
            budget--;
        }
    }

    /// <summary>
    /// Port-only: puts every visible spark back on the locator at (lx, ly, lz), as the next
    /// <see cref="Step"/> would, without advancing anything. The port steps on a fixed clock and calls
    /// this every frame so the shell keeps up with a moving host between steps.
    /// </summary>
    public void Place(float lx, float ly, float lz)
    {
        for (int i = 0; i < SlotCount; i++)
        {
            ref Sprite s = ref _sprites[i];
            if (!s.Visible)
                continue;
            s.X = lx + _ox[i];
            s.Y = ly + _oy[i];
            s.Z = lz + _oz[i];
        }
    }

    void Spawn(int i, float age)
    {
        RandomUnitVector(_rand, out float dx, out float dy, out float dz);
        dy = (float)(dy * StretchY);
        _ox[i] = dx * _radius;
        _oy[i] = dy * _radius;
        _oz[i] = dz * _radius;

        // 100439aa: scale to length 1.
        float scale = (float)(1.0 / (float)Math.Sqrt((float)(dx * dx + dy * dy + dz * dz)));
        dx *= scale;
        dy *= scale;
        dz *= scale;

        float spin = (float)((_rand() & 0x7fff) / SpinScale);
        Tracer1Sim.FindPerpendicular(dx, dy, dz, out float px, out float py, out float pz);

        // Q^-1 (w, 0) Q with Q = (d sin(r/2), cos(r/2)) (1007ca22 multiplies the other way round):
        // w turned by -r about d.
        double cos = Math.Cos(spin), sin = Math.Sin(spin);
        double kx = dy * pz - dz * py, ky = dz * px - dx * pz, kz = dx * py - dy * px;
        double dot = dx * px + dy * py + dz * pz;
        float wx = (float)(px * cos - kx * sin + dx * dot * (1.0 - cos));
        float wy = (float)(py * cos - ky * sin + dy * dot * (1.0 - cos));
        float wz = (float)(pz * cos - kz * sin + dz * dot * (1.0 - cos));

        _wx[i] = wx;
        _wy[i] = wy;
        _wz[i] = wz;
        // 1003e057: u = w' x d.
        _ux[i] = wy * dz - wz * dy;
        _uy[i] = wz * dx - wx * dz;
        _uz[i] = wx * dy - wy * dx;

        _timer[i] = (float)((double)_life + age);
        _mirror[i] = (_rand() & 1) != 0;
    }

    /// <summary>
    /// <c>_GfxControl_t</c> <c>100d3005</c> hands out entries of a 2048-vector table it fills once with
    /// points drawn like this: each axis (rand() &amp; 0x7fff) / 16384 - 1, redrawn until inside the unit
    /// ball and not zero, then scaled to length 1. The port draws a fresh one each time: the same
    /// distribution, without the shared table and its walk.
    /// </summary>
    public static void RandomUnitVector(Func<int> rand, out float x, out float y, out float z)
    {
        const double Scale = 6.103515625e-05;
        while (true)
        {
            x = (float)((rand() & 0x7fff) * Scale - 1.0);
            y = (float)((rand() & 0x7fff) * Scale - 1.0);
            z = (float)((rand() & 0x7fff) * Scale - 1.0);
            float lengthSq = (float)(x * x + y * y + z * z);
            if (lengthSq >= 1f)
                continue;
            float length = (float)Math.Sqrt(lengthSq);
            if (length == 0f)
                continue;
            x /= length;
            y /= length;
            z /= length;
            return;
        }
    }

    static float F(float[] f, int i) => f != null && i < f.Length ? f[i] : 0f;

    static int Int(float[] f, int i) => f != null && i < f.Length ? GfxBits.Of(f, i) : 0;
}
