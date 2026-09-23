using System;

/// <summary>
/// Stock <c>GfxControlTParticle2_t</c> (type 3031, 0xbd7; vftable <c>Gamecode 1016fa34</c>, loader
/// <c>10113750</c>, init <c>10114310</c>, Process <c>10113b55</c>, cleanup <c>1011364c</c>): N streaks
/// flying out of an emitter, each drawn by its own DisplaySystem <c>GfxVisualTParticle2</c>
/// (ctor <c>1002a9e7</c>, geometry <c>1002ac11</c>). It is <see cref="BParticle2Sim"/>'s streak twin —
/// the same spawn, motion, life and frame machinery, drawn as a ribbon along the velocity instead of a
/// camera-facing quad — and its ribbon is <see cref="TParticleSim"/>'s.
///
/// Fields, read in order from 9 by the loader:
/// <list type="bullet">
/// <item>0 flags, 8 duration, 9 mode (0 = one burst, 1 = emitter), 10 material, 11 count N</item>
/// <item>12 emit interval (s), 13 spawn cube half-size, 14 trail, 15 spawns per interval, 16 gravity (y)</item>
/// <item>17/18, 19/20, 21/22 velocity x, y, z ranges; 23/24 spin range (degrees/s, stored as radians)</item>
/// <item>25/26 life range (s), 27/28 size range, 29 bounce, 30 growth per call, 31 frame speed,
/// 32 frame mode, 33 the trailing half-width divisor</item>
/// <item>34.. a <see cref="StockColorCurve"/> for the near end, then one for the trailing end</item>
/// </list>
/// Flags: 0x100 swaps the ribbon's texture axes, 0x200 additive, 0x400 emitter on the ground, 0x4000 no
/// random start roll, 0x8000 bounce off the ground, 0x10000 spawn on the ground, 0x20000 random start
/// frame, 0x40000 fade the ribbon as it turns edge-on, 0x80000 flip the ribbon's v.
///
/// Spawn (init <c>101144b2</c> and emitter <c>10114025</c>, same order of draws): size, life (= total),
/// frame, position (emitter + (2r - 1) * field 13 per axis), velocity, acceleration (0, field 16, 0),
/// roll (r * 2pi), spin, then velocity and acceleration go through the locator's turn; the emitter spawn
/// also drops the particle to the ground with 0x10000. Draws come from the client's shared R250 generator
/// (<c>1013dec9</c> on <c>0x102ead20</c>); the port takes any uniform [0, 1).
///
/// Per call with delta dt, for each live particle: life -= dt (a particle at life &lt;= 0 is hidden);
/// 0x8000 and ground &gt;= y bounces v by field 29 and redraws the spin; p += v dt, then v += a dt;
/// roll += spin dt; spin += spin accel dt; <b>size *= field 30</b> (a bare per-call multiply, no dt —
/// so the growth ran with the frame rate, see Docs §3.7); the near colour is curve A and the trailing
/// colour curve B, both at 1 - life / total; the frame advances by field 31 * dt under field 32
/// (1 loop to first, 2 stop at <see cref="StoppedFrame"/>, 3 ping-pong).
///
/// Mode 0 is ready once nothing is alive. Mode 1 respawns dead particles, at most field 15 per call, once
/// field 12 seconds have gathered, while age &lt; duration - field 26. Mode 1 starts with every particle
/// dead (<c>10114776</c>); mode 0 spawns them all in the control's constructor.
///
/// The roll (+0x24) is kept and advanced but never reaches the visual, exactly as in
/// <see cref="TParticleSim"/>.
///
/// No Unity dependency so the recovered maths can be asserted from plain unit tests.
/// </summary>
public sealed class TParticle2Sim
{
    public const int FlagSwapUv = 0x100;
    public const int FlagAdditive = 0x200;
    public const int FlagGroundEmitter = 0x400;
    public const int FlagNoStartRoll = 0x4000;
    public const int FlagBounce = 0x8000;
    public const int FlagGroundSpawn = 0x10000;
    public const int FlagRandomFrame = 0x20000;
    public const int FlagEdgeFade = 0x40000;
    public const int FlagFlipV = 0x80000;

    /// <summary>Frame mode 2 parks the frame here (<c>1016fa88</c>) and stops advancing it.</summary>
    public const float StoppedFrame = -999f;

    // 10113865: degrees to radians as the loader does it.
    const double QuarterPi = 0.7853981852531433;

    // 101141cc.
    const double TwoPi = 6.2831854820251465;

    public struct Particle
    {
        public float X, Y, Z;
        public float VX, VY, VZ;
        public float AX, AY, AZ;
        public float Roll, Spin, SpinAccel;
        public float Size;
        public float Life, LifeTotal;
        public float Frame;
        public bool Visible;
        public uint NearArgb, FarArgb;
    }

    public readonly int Flags;
    public readonly int Mode;

    /// <summary>Field 14: the trailing end sits at position + velocity * this (visual +0x240).</summary>
    public readonly float Trail;

    /// <summary>Field 33: the trailing half-width is size / this (visual +0x238).</summary>
    public readonly float TailDivisor;

    readonly float _interval;
    readonly float _jitter;
    readonly int _perInterval;
    readonly float _gravity;
    readonly float _vx0, _vx1, _vy0, _vy1, _vz0, _vz1;
    readonly float _spin0, _spin1;
    readonly float _life0, _life1;
    readonly float _size0, _size1;
    readonly float _bounce;
    readonly float _growth;
    readonly float _frameSpeed;
    readonly int _frameMode;
    readonly float _firstFrame;
    readonly float _lastFrame;
    readonly StockColorCurve _near;
    readonly StockColorCurve _far;
    readonly Func<float> _random;

    readonly Particle[] _particles;
    float _spawnTimer;
    float _frameSpeedLive;

    public Particle[] Particles => _particles;
    public int LastAlive { get; private set; }
    public float Duration { get; }
    public bool Additive => (Flags & FlagAdditive) != 0;
    public StockColorCurve NearCurve => _near;
    public StockColorCurve FarCurve => _far;

    /// <param name="fields">The template's fields.</param>
    /// <param name="firstFrame">The material's first frame (<c>100cdfa1</c>).</param>
    /// <param name="lastFrame">The material's last frame (<c>100cdfb7</c>).</param>
    /// <param name="random">A uniform draw in [0, 1).</param>
    public TParticle2Sim(float[] fields, float firstFrame, float lastFrame, Func<float> random)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
        Flags = Int(fields, 0);
        Duration = F(fields, 8);
        Mode = Int(fields, 9);
        int count = Math.Max(0, Math.Min(Int(fields, 11), 4096));
        _interval = F(fields, 12);
        _jitter = F(fields, 13);
        Trail = F(fields, 14);
        _perInterval = Int(fields, 15);
        _gravity = F(fields, 16);
        _vx0 = F(fields, 17);
        _vx1 = F(fields, 18);
        _vy0 = F(fields, 19);
        _vy1 = F(fields, 20);
        _vz0 = F(fields, 21);
        _vz1 = F(fields, 22);
        _spin0 = (float)(F(fields, 23) * QuarterPi / 45.0);
        _spin1 = (float)(F(fields, 24) * QuarterPi / 45.0);
        _life0 = F(fields, 25);
        _life1 = F(fields, 26);
        _size0 = F(fields, 27);
        _size1 = F(fields, 28);
        _bounce = F(fields, 29);
        _growth = F(fields, 30);
        _frameSpeed = F(fields, 31);
        _frameSpeedLive = _frameSpeed;
        _frameMode = Int(fields, 32);
        TailDivisor = F(fields, 33);
        int index = 34;
        _near = StockColorCurve.Read(fields, ref index);
        _far = StockColorCurve.Read(fields, ref index);
        _firstFrame = firstFrame;
        _lastFrame = lastFrame;
        _particles = new Particle[count];
    }

    /// <summary>
    /// Init <c>10114310</c>, which runs in the control's constructor: every particle spawned at the
    /// emitter <paramref name="px"/>.., then mode 1 kills them all (<c>10114776</c>) so the emitter
    /// brings them in. <paramref name="turn"/> maps a local direction to the world (row-major 3x3,
    /// <c>v' = turn * v</c>, its rows already set to length 1 by <c>10113a45</c>).
    ///
    /// Stock leaves a fresh visual's two vertex colours uninitialised; the port seeds them from the
    /// curves at t = 0 so the first frame is defined (Docs §9).
    /// </summary>
    public void Init(float px, float py, float pz, float[] turn)
    {
        _spawnTimer = 0f;
        for (int i = 0; i < _particles.Length; i++)
        {
            Spawn(ref _particles[i], px, py, pz, turn, ground: null);
            // 10114758: every visual is created and shown, mode 1 included.
            _particles[i].Visible = true;
            _particles[i].NearArgb = _near.Evaluate(0f);
            _particles[i].FarArgb = _far.Evaluate(0f);
        }

        if (Mode == 1)
        {
            for (int i = 0; i < _particles.Length; i++)
                _particles[i].Life = 0f;
        }
    }

    /// <summary>
    /// <c>101144b2</c> (init) and <c>10114025</c> (emitter). The emitter spawn additionally drops the
    /// particle to the ground with 0x10000 (<c>1011423c</c>); the init has no such step, so it passes
    /// no <paramref name="ground"/>.
    /// </summary>
    void Spawn(ref Particle p, float px, float py, float pz, float[] turn, Func<float, float, float, float> ground)
    {
        p.Size = Range(_size0, _size1);
        p.Life = Range(_life0, _life1);
        p.LifeTotal = p.Life;
        p.Frame = (Flags & FlagRandomFrame) != 0 ? Range(_firstFrame, _lastFrame) : 0f;
        p.X = (float)((_random() * 2.0 - 1.0) * _jitter + px);
        p.Y = (float)((_random() * 2.0 - 1.0) * _jitter + py);
        p.Z = (float)((_random() * 2.0 - 1.0) * _jitter + pz);
        p.VX = Range(_vx0, _vx1);
        p.VY = Range(_vy0, _vy1);
        p.VZ = Range(_vz0, _vz1);
        p.AX = 0f;
        p.AY = _gravity;
        p.AZ = 0f;
        p.Roll = (Flags & FlagNoStartRoll) != 0 ? 0f : (float)(_random() * TwoPi + 0.0);
        p.Spin = Range(_spin0, _spin1);
        p.SpinAccel = 0f;
        Turn(turn, ref p.VX, ref p.VY, ref p.VZ);
        Turn(turn, ref p.AX, ref p.AY, ref p.AZ);

        if (ground != null && (Flags & FlagGroundSpawn) != 0)
        {
            float g = Ground(ground, p.X, p.Y, p.Z);
            if (!float.IsNaN(g))
                p.Y = g;
        }
    }

    float Range(float lo, float hi) => (float)(_random() * (hi - lo) + lo);

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

    /// <summary>
    /// One Process body (<c>10113b55</c>). Returns false once stock would be ready (mode 0 with nothing
    /// alive). <paramref name="ground"/> gives the ground height under a point, or NaN for none.
    /// </summary>
    public bool Step(
        float dt, float age, float duration,
        float px, float py, float pz,
        float[] turn, Func<float, float, float, float> ground)
    {
        // 10113bb2: with 0x400 the emitter sits on the ground.
        if ((Flags & FlagGroundEmitter) != 0)
        {
            float g = Ground(ground, px, py, pz);
            if (!float.IsNaN(g))
                py = g;
        }

        _spawnTimer += dt;
        int alive = 0;

        for (int i = 0; i < _particles.Length; i++)
        {
            ref Particle p = ref _particles[i];

            // 10113c1f: a hidden visual is a dead particle.
            if (!p.Visible)
            {
                p.Life = 0f;
                continue;
            }

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
                float g = Ground(ground, p.X, p.Y, p.Z);
                if (!float.IsNaN(g) && !(g < p.Y))
                {
                    p.VX *= _bounce;
                    p.VY = -p.VY * _bounce;
                    p.VZ *= _bounce;
                    p.Spin = Range(_spin0, _spin1);
                }
            }

            p.X += p.VX * dt;
            p.Y += p.VY * dt;
            p.Z += p.VZ * dt;
            p.VX += p.AX * dt;
            p.VY += p.AY * dt;
            p.VZ += p.AZ * dt;

            p.Roll += p.Spin * dt;
            p.Spin += p.SpinAccel * dt;

            // 10113d7c: a bare per-call multiply.
            p.Size *= _growth;

            float t = (float)(1.0 - p.Life / p.LifeTotal);
            p.FarArgb = _far.Evaluate(t);
            p.NearArgb = _near.Evaluate(t);

            AdvanceFrame(ref p, dt);
        }

        LastAlive = alive;

        // 10113f64: mode 0 is a burst and ends with its last particle.
        if (Mode == 0)
            return alive != 0;

        if (Mode == 1)
            Emit(age, duration, px, py, pz, turn, ground);
        return true;
    }

    /// <summary>
    /// <c>10113f8d</c>..<c>101142ea</c>: once field 12 has gathered, refill dead slots, at most field 15
    /// in a call, and only while there is a full life left before the duration.
    /// </summary>
    void Emit(float age, float duration, float px, float py, float pz, float[] turn, Func<float, float, float, float> ground)
    {
        if (!(age < duration - _life1))
            return;
        if (_interval < 0f)
            return;
        if (_spawnTimer < _interval)
            return;

        int spawned = 0;
        for (int i = 0; i < _particles.Length; i++)
        {
            ref Particle p = ref _particles[i];
            if (0f < p.Life)
                continue;

            _spawnTimer = 0f;
            // Stock does not touch the visual's colours here, so a recycled particle carries the last
            // one it had until the next call sets it (Docs §9).
            Spawn(ref p, px, py, pz, turn, ground);
            p.Visible = true;

            spawned++;
            if (spawned >= _perInterval)
                return;
        }
    }

    /// <summary><c>10113e64</c>..<c>10113f31</c>; the rate flips on the control, so for every particle.</summary>
    void AdvanceFrame(ref Particle p, float dt)
    {
        if (_frameMode == 0)
            return;

        // 10113e7d: the stop sentinel holds the frame where it is.
        if (p.Frame != StoppedFrame)
            p.Frame += _frameSpeedLive * dt;

        if (_lastFrame < p.Frame)
        {
            switch (_frameMode)
            {
                case 1:
                    p.Frame = _firstFrame;
                    break;
                case 2:
                    p.Frame = StoppedFrame;
                    break;
                case 3:
                    _frameSpeedLive = -_frameSpeedLive;
                    p.Frame += dt * _frameSpeedLive;
                    break;
            }
        }

        if (!(_firstFrame <= p.Frame) && _frameMode == 3)
        {
            _frameSpeedLive = -_frameSpeedLive;
            p.Frame += dt * _frameSpeedLive;
        }
    }

    /// <summary><c>100d33f3</c>: the ground under a point, or NaN where there is none.</summary>
    static float Ground(Func<float, float, float, float> ground, float x, float y, float z)
        => ground == null ? float.NaN : ground(x, y, z);

    static float F(float[] f, int i) => f != null && i >= 0 && i < f.Length ? f[i] : 0f;

    static int Int(float[] f, int i) => f != null && i >= 0 && i < f.Length ? GfxBits.Of(f, i) : 0;
}
