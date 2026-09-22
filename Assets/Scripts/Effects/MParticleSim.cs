using System;

/// <summary>
/// Stock <c>GfxControlMParticle_t</c> (type 3027, 0xbd3; vftable <c>Gamecode 1016f674</c>, loader
/// <c>1010f5ee</c>, init <c>10110229</c>, Process <c>1010f92b</c>): N ABIFF models thrown about as debris,
/// each its own DisplaySystem <c>VisualMesh_t</c> with the model's own material (no rendering effect).
///
/// Fields, read by the loader: 0 flags, 8 duration, 9 mode (0 one burst, 1 emitter; 10 is read and
/// dropped), 11 count N, 12 emit interval, 13 spawn cube half-size, 14 fade-in and 15 fade-out (fractions
/// of life), 16 gravity (y), 17/18, 19/20, 21/22 velocity x, y, z ranges, 23-25 spin axis (zero = one
/// random axis for every particle, drawn at load), 26/27 spin range (degrees/s, stored as radians),
/// 28/29 life range, 30/31 scale range, 32 bounce, 33 model count M and 34.. the models (indices into
/// the first ten of <see cref="EffectMeshSim.ModelNames"/>).
/// Flags: 0x100 the emitter on the ground, 0x800 bounce off the ground.
///
/// Init gives each particle a model, <c>rand() % M</c> (the port draws it from the uniform source).
/// The first Process call spawns them all (<c>1010f9e2</c>): scale, life (= total), position (emitter +
/// (2r - 1) * field 13 per axis), velocity, acceleration (0, field 16, 0), angle (r * 2pi), spin, in that
/// order of draws, no turn; mode 1 then keeps only particle 0, at life = field 29. The emitter respawns
/// one dead particle per call, turned by the locator, once field 12 seconds have gathered while
/// age &lt; duration - field 29 (never with a negative duration).
///
/// Per call with delta dt, for each particle: life -= dt; at life &lt;= 0 it is hidden. Else with 0x800
/// and ground &gt;= y: v.x -= b v.x dt 100, v.y = -b v.y dt 100, v.z -= b v.z dt 100 (b = field 32; a
/// frame-rate dependent bounce, restitution 100 b dt), spin = r-spin * |0.1 v.y| * b. Then p += v dt,
/// v += a dt, angle += spin dt, scale follows its (zero) rates. Alpha, with x = 1 - life / total:
/// x / fade-in below fade-in, 1 - (x - (1 - fade-out)) / fade-out past 1 - fade-out, else 1.
/// Mode 0 is ready once nothing is alive.
///
/// No Unity dependency so the recovered maths can be asserted from plain unit tests.
/// </summary>
public sealed class MParticleSim
{
    public const int FlagLocalMode = 2;
    public const int FlagGround = 0x100;
    public const int FlagBounce = 0x800;

    // 1010f6ed: degrees to radians as the loader does it.
    const double QuarterPi = 0.7853981852531433;

    // 1010fb6f.
    const double TwoPi = 6.2831854820251465;

    // 1010fd69.
    const double Tenth = 0.10000000149011612;

    public struct Particle
    {
        public float X, Y, Z;
        public float VX, VY, VZ;
        public float AX, AY, AZ;
        public float AxisX, AxisY, AxisZ;
        public float Angle, Spin, SpinAccel;
        public float Scale, ScaleRate, ScaleRateRate;
        public float Life, LifeTotal;
        public int Model;
        public bool Visible;
        public float Alpha;
    }

    public readonly int Flags;
    public readonly float Duration;
    public readonly int Mode;
    readonly float _interval;
    readonly float _jitter;
    readonly float _fadeIn;
    readonly float _fadeOut;
    readonly float _fadeOutStart;
    readonly float _gravity;
    readonly float _vx0, _vx1, _vy0, _vy1, _vz0, _vz1;
    readonly float _axisX, _axisY, _axisZ;
    readonly float _spin0, _spin1;
    readonly float _life0, _life1;
    readonly float _scale0, _scale1;
    readonly float _bounce;
    readonly int[] _models;
    readonly Particle[] _particles;
    readonly Func<float> _random;
    float _timer;
    bool _spawned;

    public Particle[] Particles => _particles;
    public int[] Models => _models;
    public int LastAlive { get; private set; }

    /// <param name="random">A uniform draw in [0, 1).</param>
    public MParticleSim(float[] fields, Func<float> random)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
        Flags = Int(fields, 0);
        Duration = F(fields, 8);
        Mode = Int(fields, 9);
        int count = Math.Max(0, Math.Min(Int(fields, 11), 4096));
        _interval = F(fields, 12);
        _jitter = F(fields, 13);
        _fadeIn = F(fields, 14);
        _fadeOut = F(fields, 15);
        _gravity = F(fields, 16);
        _vx0 = F(fields, 17);
        _vx1 = F(fields, 18);
        _vy0 = F(fields, 19);
        _vy1 = F(fields, 20);
        _vz0 = F(fields, 21);
        _vz1 = F(fields, 22);
        _spin0 = (float)(F(fields, 26) * QuarterPi / 45.0);
        _spin1 = (float)(F(fields, 27) * QuarterPi / 45.0);
        _life0 = F(fields, 28);
        _life1 = F(fields, 29);
        _scale0 = F(fields, 30);
        _scale1 = F(fields, 31);
        _bounce = F(fields, 32);
        int models = Math.Max(0, Math.Min(Int(fields, 33), 64));
        _models = new int[models];
        for (int i = 0; i < models; i++)
            _models[i] = Int(fields, 34 + i);

        // 1010f7af: a zero axis is three draws in [-1, 1); either way set to length 1.
        float x = F(fields, 23), y = F(fields, 24), z = F(fields, 25);
        if (x == 0f && y == 0f && z == 0f)
        {
            x = (float)(_random() * 2.0 - 1.0);
            y = (float)(_random() * 2.0 - 1.0);
            z = (float)(_random() * 2.0 - 1.0);
        }
        double len = Math.Sqrt((double)x * x + (double)y * y + (double)z * z);
        _axisX = (float)(x / len);
        _axisY = (float)(y / len);
        _axisZ = (float)(z / len);

        // 1010f815: +0xc4 = 1 - fade-out.
        _fadeOutStart = 1f - _fadeOut;
        _particles = new Particle[count];
    }

    /// <summary>
    /// Init <c>10110229</c>: each particle's model, <c>rand() % M</c> as an index into
    /// <see cref="Models"/>. False with no models (stock goes ready).
    /// </summary>
    public bool Init()
    {
        _spawned = false;
        _timer = 0f;
        if (_models.Length <= 0)
            return false;
        for (int i = 0; i < _particles.Length; i++)
            _particles[i].Model = Math.Min((int)(_random() * _models.Length), _models.Length - 1);
        return true;
    }

    void Spawn(ref Particle p, float px, float py, float pz)
    {
        p.Scale = Range(_scale0, _scale1);
        p.Life = Range(_life0, _life1);
        p.LifeTotal = p.Life;
        p.X = (float)((_random() * 2.0 - 1.0) * _jitter + px);
        p.Y = (float)((_random() * 2.0 - 1.0) * _jitter + py);
        p.Z = (float)((_random() * 2.0 - 1.0) * _jitter + pz);
        p.VX = Range(_vx0, _vx1);
        p.VY = Range(_vy0, _vy1);
        p.VZ = Range(_vz0, _vz1);
        p.AX = 0f;
        p.AY = _gravity;
        p.AZ = 0f;
        p.AxisX = _axisX;
        p.AxisY = _axisY;
        p.AxisZ = _axisZ;
        p.Angle = (float)(_random() * TwoPi + 0.0);
        p.Spin = Range(_spin0, _spin1);
        p.SpinAccel = 0f;
        p.Alpha = 0f;
        p.Visible = true;
    }

    float Range(float lo, float hi) => (float)(_random() * (hi - lo) + lo);

    /// <summary>
    /// One Process body. <paramref name="px"/>.. is the emitter (the locator's local-mode position,
    /// already on the ground with 0x100); <paramref name="turn"/> maps a local direction to the world
    /// (row-major 3x3, <c>v' = turn * v</c>); <paramref name="ground"/> gives the ground height under a
    /// point, or NaN for none. Returns false once stock would be ready (mode 0 with nothing alive).
    /// </summary>
    public bool Step(float dt, float age, float px, float py, float pz, float[] turn, Func<float, float, float, float> ground)
    {
        if (!_spawned)
        {
            for (int i = 0; i < _particles.Length; i++)
                Spawn(ref _particles[i], px, py, pz);
            if (Mode == 1 && _particles.Length > 0)
            {
                _particles[0].Life = _life1;
                for (int i = 1; i < _particles.Length; i++)
                    _particles[i].Life = 0f;
            }
            _spawned = true;
        }

        _timer += dt;
        int alive = 0;
        for (int i = 0; i < _particles.Length; i++)
        {
            ref Particle p = ref _particles[i];
            p.Life -= dt;
            if (!(0f < p.Life))
            {
                p.Life = 0f;
                p.Visible = false;
                continue;
            }
            alive++;

            if ((Flags & FlagBounce) != 0)
            {
                float g = ground != null ? ground(p.X, p.Y, p.Z) : float.NaN;
                if (g >= p.Y)
                {
                    p.VX = (float)(p.VX - _bounce * (double)p.VX * dt * 100.0);
                    p.VY = (float)(_bounce * (double)p.VY * dt * -100.0);
                    p.VZ = (float)(p.VZ - _bounce * (double)p.VZ * dt * 100.0);
                    double s = Math.Abs((float)(p.VY * Tenth));
                    p.Spin = (float)(Range(_spin0, _spin1) * s * _bounce);
                }
            }

            p.X += p.VX * dt;
            p.Y += p.VY * dt;
            p.Z += p.VZ * dt;
            p.VX += p.AX * dt;
            p.VY += p.AY * dt;
            p.VZ += p.AZ * dt;
            p.Angle = p.Spin * dt + p.Angle;
            p.Spin = p.SpinAccel * dt + p.Spin;
            p.Scale = p.ScaleRate * dt + p.Scale;
            p.ScaleRate = p.ScaleRateRate * dt + p.ScaleRate;
            p.Alpha = FadeAlpha(1f - p.Life / p.LifeTotal);
        }
        LastAlive = alive;

        if (Mode == 0)
            return alive != 0;

        if (Mode == 1)
            Emit(age, px, py, pz, turn);
        return true;
    }

    /// <summary><c>1010feb5</c>..<c>1010ff0c</c>: alpha at <paramref name="x"/> = 1 - life / total.</summary>
    public float FadeAlpha(float x)
    {
        if (_fadeIn > x)
            return x / _fadeIn;
        if (_fadeOutStart < x)
            return 1f - (x - _fadeOutStart) / _fadeOut;
        return 1f;
    }

    /// <summary><c>1010ff4c</c>..<c>10110224</c>: at most one respawn per call.</summary>
    void Emit(float age, float px, float py, float pz, float[] turn)
    {
        if (!(age < Duration - _life1))
            return;
        if (!(_interval >= 0f) || !(_timer >= _interval))
            return;

        for (int i = 0; i < _particles.Length; i++)
        {
            ref Particle p = ref _particles[i];
            if (0f < p.Life)
                continue;

            _timer = 0f;
            Spawn(ref p, px, py, pz);
            Turn(turn, ref p.AxisX, ref p.AxisY, ref p.AxisZ);
            Turn(turn, ref p.VX, ref p.VY, ref p.VZ);
            Turn(turn, ref p.AX, ref p.AY, ref p.AZ);
            return;
        }
    }

    /// <summary><c>1010aef4</c>: each row of the matrix dotted with the vector.</summary>
    static void Turn(float[] m, ref float x, ref float y, ref float z)
    {
        if (m == null)
            return;
        float nx = m[0] * x + m[1] * y + m[2] * z;
        float ny = m[3] * x + m[4] * y + m[5] * z;
        float nz = m[6] * x + m[7] * y + m[8] * z;
        x = nx;
        y = ny;
        z = nz;
    }

    static float F(float[] f, int i) => f != null && i >= 0 && i < f.Length ? f[i] : 0f;

    static int Int(float[] f, int i) => f != null && i >= 0 && i < f.Length ? BitConverter.SingleToInt32Bits(f[i]) : 0;
}
