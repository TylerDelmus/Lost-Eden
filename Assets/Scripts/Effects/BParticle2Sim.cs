using System;

/// <summary>
/// Stock <c>GfxControlBParticle2_t</c> (type 3028, 0xbd4): N particles, each drawn by its own DisplaySystem
/// <c>GfxVisualBParticle2</c> (a rotated camera-facing quad). Loader <c>1010b196</c>, init <c>1010bf63</c>,
/// Process <c>1010b499</c>, slot 6 <c>1010af68</c>.
///
/// Fields, read in order from 9 by the loader:
/// <list type="bullet">
/// <item>0 flags, 8 duration, 9 mode (0 = one burst, 1 = emitter), 10 material, 11 count N</item>
/// <item>12 emit interval (s), 13 spawn cube half-size, 14 drag, 15 spawns per interval, 16 gravity (y)</item>
/// <item>17/18, 19/20, 21/22 velocity x, y, z ranges; 23/24 spin range (degrees/s, stored as radians)</item>
/// <item>25/26 life range (s), 27/28 size range, 29 bounce, 30 growth, 31 frame speed, 32 frame mode</item>
/// <item>33 aspect (the quad's half-height is size / field 33), 34 passed to the visual (+0x1a4)</item>
/// <item>35.. a <see cref="StockColorCurve"/> over the particle's age fraction</item>
/// </list>
/// Flags: 0x200 additive, 0x400 emitter on the ground, 0x4000 no random start angle, 0x8000 bounce off
/// the ground, 0x10000 respawns at ground + locator offset y, 0x20000 random start frame, 0x100000
/// particles stick to the emitter, 0x200000 particles live until terminated, 0x800000 fade from 3 m to
/// 6 m above the ground. (0x400000, a time-of-day fade, and 0x1000000, the host's body scale, are not
/// modelled.)
///
/// Spawn (init and emitter): size, life (= total), frame, position (emitter + (2r - 1) * field 13 per
/// axis), velocity, acceleration (0, field 16, 0), angle (r * 2pi), spin, in that order of draws, then
/// velocity and acceleration go through the locator's turn. Draws come from the client's shared R250
/// generator (<c>1013dec9</c> on <c>0x102ead20</c>); the port takes any uniform [0, 1).
///
/// Per call with delta dt, for each live particle:
/// <list type="bullet">
/// <item>life -= dt (not with 0x200000 until terminating); a particle at life &lt;= 0 is hidden.</item>
/// <item>0x8000 and ground &gt;= y: v = (vx * b, -vy * b, vz * b) with b = field 29, spin redrawn.</item>
/// <item>p += v dt, then v += a dt (0x100000: p = the emitter instead).</item>
/// <item>angle += spin dt; size += (field 30 - 1) * size * dt * 100; v += v * (field 14 - 1) * dt * 100.</item>
/// <item>colour = the curve at 1 - life / total; frame += field 31 * dt, wrapped by field 32
/// (1 loop to first, 2 hold last, 3 ping-pong).</item>
/// </list>
/// Mode 0 is ready once nothing is alive. Mode 1 respawns dead particles, at most field 15 per call,
/// once field 12 seconds have gathered, while age &lt; duration - field 26 (or the duration is negative).
/// Mode 1 starts with every particle dead; mode 0 spawns them all at once.
///
/// No Unity dependency so the recovered maths can be asserted from plain unit tests.
/// </summary>
public sealed class BParticle2Sim
{
    public const int FlagAdditive = 0x200;
    public const int FlagGroundEmitter = 0x400;
    public const int FlagNoStartAngle = 0x4000;
    public const int FlagBounce = 0x8000;
    public const int FlagGroundRespawn = 0x10000;
    public const int FlagRandomFrame = 0x20000;
    public const int FlagStick = 0x100000;
    public const int FlagLiveUntilTerminated = 0x200000;
    public const int FlagHeightFade = 0x800000;

    // 1010b2c1: degrees to radians as the loader does it.
    const double QuarterPi = 0.7853981852531433;

    // 1010c2cc.
    const double TwoPi = 6.2831854820251465;

    public struct Particle
    {
        public float X, Y, Z;
        public float VX, VY, VZ;
        public float AX, AY, AZ;
        public float Angle, Spin, SpinAccel;
        public float Size;
        public float Life, LifeTotal;
        public float Frame;
        public bool Visible;
        public uint Argb;
    }

    public readonly int Flags;
    public readonly int Mode;
    readonly float _interval;
    readonly float _jitter;
    readonly float _drag;
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
    public readonly float Aspect;
    readonly float _firstFrame;
    readonly float _lastFrame;
    readonly StockColorCurve _curve;
    readonly Func<float> _random;

    readonly Particle[] _particles;
    float _spawnTimer;
    float _frameSpeedLive;

    public Particle[] Particles => _particles;
    public bool Terminating { get; set; }
    public int LastAlive { get; private set; }
    public float Duration { get; }
    public bool Additive => (Flags & FlagAdditive) != 0;

    /// <param name="fields">The template's fields.</param>
    /// <param name="firstFrame">The material's first frame (<c>100cdfa1</c>).</param>
    /// <param name="lastFrame">The material's last frame (<c>100cdfb7</c>).</param>
    /// <param name="random">A uniform draw in [0, 1).</param>
    public BParticle2Sim(float[] fields, float firstFrame, float lastFrame, Func<float> random)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
        Flags = Int(fields, 0);
        Duration = F(fields, 8);
        Mode = Int(fields, 9);
        int count = Math.Max(0, Math.Min(Int(fields, 11), 4096));
        _interval = F(fields, 12);
        _jitter = F(fields, 13);
        _drag = F(fields, 14);
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
        Aspect = F(fields, 33);
        int index = 35;
        _curve = StockColorCurve.Read(fields, ref index);
        _firstFrame = firstFrame;
        _lastFrame = lastFrame;
        _particles = new Particle[count];
    }

    public StockColorCurve Curve => _curve;

    /// <summary>
    /// Init <c>1010bf63</c>: every particle spawned at the emitter <paramref name="px"/>..; mode 1 then
    /// kills them all so the emitter brings them in. <paramref name="turn"/> maps a local direction to the
    /// world (row-major 3x3, <c>v' = turn * v</c>, already times the body scale).
    /// </summary>
    public void Init(float px, float py, float pz, float[] turn)
    {
        _spawnTimer = 0f;
        for (int i = 0; i < _particles.Length; i++)
            Spawn(ref _particles[i], px, py, pz, turn);

        if (Mode == 1)
        {
            for (int i = 0; i < _particles.Length; i++)
                _particles[i].Life = 0f;
        }
    }

    void Spawn(ref Particle p, float px, float py, float pz, float[] turn)
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
        p.Angle = (Flags & FlagNoStartAngle) != 0 ? 0f : (float)(_random() * TwoPi + 0.0);
        p.Spin = Range(_spin0, _spin1);
        p.SpinAccel = 0f;
        Turn(turn, ref p.VX, ref p.VY, ref p.VZ);
        Turn(turn, ref p.AX, ref p.AY, ref p.AZ);
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
    /// One Process body. Returns false once stock would be ready (mode 0 with nothing alive).
    /// <paramref name="ground"/> gives the ground height under a point, or NaN for none.
    /// </summary>
    public bool Step(float dt, float age, float duration, float px, float py, float pz, float[] turn, Func<float, float, float, float> ground, float locatorOffsetY)
    {
        // 1010b56c: the height fade measures the emitter before the ground snap.
        float height = float.NaN;
        if ((Flags & FlagHeightFade) != 0)
        {
            float g = Ground(ground, px, py, pz);
            if (!float.IsNaN(g))
                height = py - g;
        }

        if ((Flags & FlagGroundEmitter) != 0)
        {
            float g = Ground(ground, px, py, pz);
            if (!float.IsNaN(g))
                py = g;
        }

        _spawnTimer += dt;
        bool ageing = (Flags & FlagLiveUntilTerminated) == 0 || Terminating;
        int alive = 0;

        for (int i = 0; i < _particles.Length; i++)
        {
            ref Particle p = ref _particles[i];
            if (ageing)
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

            if ((Flags & FlagStick) != 0)
            {
                p.X = px;
                p.Y = py;
                p.Z = pz;
            }
            else
            {
                p.X += p.VX * dt;
                p.Y += p.VY * dt;
                p.Z += p.VZ * dt;
                p.VX += p.AX * dt;
                p.VY += p.AY * dt;
                p.VZ += p.AZ * dt;
            }

            p.Angle += p.Spin * dt;
            p.Spin += p.SpinAccel * dt;
            p.Size = (float)((_growth - 1.0) * p.Size * dt * 100.0 + p.Size);

            float dragScale = (float)((_drag - 1.0) * dt * 100.0);
            p.VX += p.VX * dragScale;
            p.VY += p.VY * dragScale;
            p.VZ += p.VZ * dragScale;

            uint argb = _curve.Evaluate((float)(1.0 - p.Life / p.LifeTotal));
            if (!float.IsNaN(height))
                argb = HeightFade(argb, height);
            p.Argb = argb;
            p.Visible = true;

            AdvanceFrame(ref p, dt);
        }

        LastAlive = alive;

        if (Mode == 0)
            return alive != 0;

        if (Mode == 1)
            Emit(age, duration, px, py, pz, turn, ground, locatorOffsetY);
        return true;
    }

    /// <summary>1010ba0c..1010bac8.</summary>
    void AdvanceFrame(ref Particle p, float dt)
    {
        if (_frameMode == 0)
            return;

        p.Frame += _frameSpeedLive * dt;
        if (_lastFrame < p.Frame)
        {
            switch (_frameMode)
            {
                case 1:
                    p.Frame = _firstFrame;
                    break;
                case 2:
                    p.Frame = _lastFrame;
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

    /// <summary>
    /// 0x800000 (1010b8e8..1010b96a): full colour below 3 m above the ground, blended toward nothing up to
    /// 6 m, nothing above (exactly 6 m keeps the colour, as stock's compare does). The height is the emitter's.
    /// </summary>
    static uint HeightFade(uint argb, float h)
    {
        if (h >= 3f && h < 6f)
            return StockColorCurve.Interpolate(0u, argb, (float)(1.0 - (h - 3.0) / 3.0));
        return h > 6f ? 0u : argb;
    }

    /// <summary>1010bb23..1010bf56.</summary>
    void Emit(float age, float duration, float px, float py, float pz, float[] turn, Func<float, float, float, float> ground, float locatorOffsetY)
    {
        if (!(duration < 0f) && !(age < duration - _life1))
            return;
        if (_interval < 0f || _spawnTimer < _interval)
            return;

        int spawned = 0;
        for (int i = 0; i < _particles.Length; i++)
        {
            ref Particle p = ref _particles[i];
            if (0f < p.Life)
                continue;

            _spawnTimer = 0f;
            Spawn(ref p, px, py, pz, turn);
            if ((Flags & FlagGroundRespawn) != 0)
            {
                float g = Ground(ground, p.X, p.Y, p.Z);
                if (!float.IsNaN(g))
                    p.Y = g + locatorOffsetY;
            }

            if (++spawned >= _perInterval)
                return;
        }
    }

    static float Ground(Func<float, float, float, float> ground, float x, float y, float z)
        => ground != null ? ground(x, y, z) : float.NaN;

    static float F(float[] f, int i) => f != null && i >= 0 && i < f.Length ? f[i] : 0f;

    static int Int(float[] f, int i) => f != null && i >= 0 && i < f.Length ? GfxBits.Of(f, i) : 0;
}
