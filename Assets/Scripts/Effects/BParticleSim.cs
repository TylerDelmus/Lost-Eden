using System;

/// <summary>
/// Stock <c>GfxControlBParticle_t</c> (type 3024, 0xbd0; vftable <c>Gamecode 1016efa4</c>, loader
/// <c>1010a644</c>, visual build <c>1010a412</c>, Process <c>1010a909</c>) and the DisplaySystem
/// <c>GfxVisualBParticle</c> it drives (ctor <c>10009938</c>, ProcessParticles <c>10008bce</c>, per-particle
/// draw <c>1000a70f</c>). Only the particle mode 8 path is recovered: all 16 mode-8 records use motion 0,
/// quad 0, life policy 0 and frame mode 1, and that is what this models. The other modes (0-7, 9-13)
/// are still missing.
///
/// Control fields: 0 flags, 8 duration, 9 material, 10 mode, 11 count, 12 spawn cube half-size, 16 delay
/// rate, 17 life rate, 25/26/27 start / middle / end colour, 31/32, 33/34, 35/36 width and height at the
/// start / middle / end, 13 = 4 eases the sizes as t^8, 37 frame... (38 frame speed, 39 frame mode),
/// 40 first key time, 41 last key time (stored as 1 - field 41), 28-30 camera-distance fade.
///
/// Control Process, with u = time / duration (-1 without a duration; ready past 1): below 0 the middle
/// colour and size; before the first key (field 40, 0.5 when negative) start to middle over u / key;
/// past field 41 middle to end over (u - field 41) / (1 - field 41); between them the middle.
///
/// Mode 8 particles: spawned in the ctor at (2r - 1) * field 12 per axis, life 999 (a sentinel), a
/// delay timer r * 0.2 (particle 0's is 0). Each ProcessParticles(dt): while life is 999 it takes
/// field 17 * dt off it (so it leaves the sentinel on the first call); else while the timer isn't 999 it
/// takes field 16 * dt off the timer, and once that is below 0 the particle pops: life = r * 0.5 + 0.5,
/// timer 999, angle r * 360, a new point in the cube. After that the life runs down at field 17 per
/// second. Drawn only with 0 &lt; life and life != 999, scaled in size and alpha by sin(life * pi / 2).
///
/// No Unity dependency so the recovered maths can be asserted from plain unit tests.
/// </summary>
public sealed class BParticleSim
{
    public const int FlagSwapUv = 0x100;
    public const int FlagAdditive = 0x200;
    public const int FlagGround = 0x400;

    /// <summary>Stock's "no life yet" / "no timer" marker.</summary>
    public const float Sentinel = 999f;

    public struct Particle
    {
        public float X, Y, Z;
        public float VX, VY, VZ;
        public float AX, AY, AZ;
        public float Angle, Spin;
        public float Frame;
        public float Scale;
        public float Life;
        public float Timer;
    }

    public readonly int Flags;
    public readonly int Mode;
    public readonly float Duration;
    readonly float _radius;
    readonly bool _ease;
    readonly float _delayRate;
    readonly float _lifeRate;
    readonly uint _c0, _c1, _c2;
    readonly float _w0, _h0, _w1, _h1, _w2, _h2;
    readonly float _frameSpeedInit;
    readonly int _frameMode;
    readonly float _keyIn;
    readonly float _keyOutSpan;
    readonly float _keyOut;
    public readonly float FadeNear, FadeMid, FadeFar;
    readonly float _firstFrame, _lastFrame;
    readonly Func<float> _random;
    readonly Particle[] _particles;
    float _time;
    float _frameSpeed;

    public Particle[] Particles => _particles;
    public bool Additive => (Flags & FlagAdditive) != 0;
    public bool SwapUv => (Flags & FlagSwapUv) != 0;
    public bool Supported => Mode == 8;

    public uint Argb { get; private set; } = 0xffffffffu;
    public float Width { get; private set; }
    public float Height { get; private set; }

    public BParticleSim(float[] fields, float firstFrame, float lastFrame, Func<float> random)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
        Flags = Int(fields, 0);
        Duration = F(fields, 8);
        Mode = Int(fields, 10);
        int count = Math.Max(0, Math.Min(Int(fields, 11), 4096));
        _radius = F(fields, 12);
        _ease = Int(fields, 13) == 4;
        _delayRate = F(fields, 16);
        _lifeRate = F(fields, 17);
        _c0 = unchecked((uint)Int(fields, 25));
        _c1 = unchecked((uint)Int(fields, 26));
        _c2 = unchecked((uint)Int(fields, 27));
        FadeNear = F(fields, 28);
        FadeMid = F(fields, 29);
        FadeFar = F(fields, 30);
        _w0 = F(fields, 31); _h0 = F(fields, 32);
        _w1 = F(fields, 33); _h1 = F(fields, 34);
        _w2 = F(fields, 35); _h2 = F(fields, 36);
        _frameSpeedInit = F(fields, 38);
        _frameSpeed = _frameSpeedInit;
        _frameMode = Int(fields, 39);
        _keyIn = F(fields, 40);
        // 1010a80f: +0xac = 1 - field 41, +0xe0 = 1 - +0xac.
        _keyOutSpan = (float)(1.0 - F(fields, 41));
        _keyOut = (float)(1.0 - _keyOutSpan);
        _firstFrame = firstFrame;
        _lastFrame = lastFrame;
        _particles = new Particle[count];
        Width = _w1;
        Height = _h1;
        Argb = _c1;
        Spawn();
    }

    /// <summary>Ctor mode 8, <c>1000a39b</c>.</summary>
    void Spawn()
    {
        if (Mode != 8)
            return;
        for (int i = 0; i < _particles.Length; i++)
        {
            ref Particle p = ref _particles[i];
            p.X = Cube();
            p.Y = Cube();
            p.Z = Cube();
            p.Scale = 1f;
            p.Life = Sentinel;
            p.Timer = (float)(_random() * 0.20000000298023224 + 0.0);
        }
        if (_particles.Length > 0)
            _particles[0].Timer = 0f;
    }

    float Cube() => (float)((_random() * 2.0 - 1.0) * _radius);

    /// <summary>
    /// The control's Process (<c>1010a917</c>..<c>1010abed</c>) then the visual's ProcessParticles.
    /// Returns false once the time is past the duration.
    /// </summary>
    public bool Advance(float dt)
    {
        _time += dt;
        float u = -1f;
        if (!(Duration < 0f))
        {
            u = _time / Duration;
            if (1f < u)
                return false;
        }

        Keys(u);
        ProcessParticles(dt);
        return true;
    }

    /// <summary>1010a9a3..1010ab67.</summary>
    void Keys(float u)
    {
        float keyIn = _keyIn;
        if (keyIn < 0f)
            keyIn = 0.5f;
        float span = _keyOutSpan;
        if (span < 0f)
            span = 0.5f;

        Argb = _c1;
        Width = _w1;
        Height = _h1;
        if (u < 0f)
            return;

        if (u < keyIn)
        {
            float f = u / (keyIn == 0f ? 1f : keyIn);
            Argb = StockColorCurve.Interpolate(_c0, _c1, f);
            if (_ease)
                f = Pow8(f);
            Width = (_w1 - _w0) * f + _w0;
            Height = (_h1 - _h0) * f + _h0;
        }
        else if (_keyOut < u)
        {
            float f = (u - _keyOut) / (span == 0f ? 1f : span);
            Argb = StockColorCurve.Interpolate(_c1, _c2, f);
            if (_ease)
                f = Pow8(f);
            Width = (_w2 - _w1) * f + _w1;
            Height = (_h2 - _h1) * f + _h1;
        }
    }

    static float Pow8(float t)
    {
        float r = t;
        for (int k = 0; k < 3; k++)
            r *= r;
        return r;
    }

    /// <summary><c>10008bce</c>, the parts mode 8 with motion 0 and life policy 0 reaches.</summary>
    void ProcessParticles(float dt)
    {
        for (int i = 0; i < _particles.Length; i++)
        {
            ref Particle p = ref _particles[i];
            p.Angle += p.Spin * dt;
            if (!(p.Angle < 360f))
                p.Angle -= 360f;
            else if (!(0f <= p.Angle))
                p.Angle += 360f;

            if (_frameMode != 0)
                AdvanceFrame(ref p, dt);

            // Motion 0: ballistic.
            p.X += p.VX * dt;
            p.Y += p.VY * dt;
            p.Z += p.VZ * dt;
            p.VX += p.AX * dt;
            p.VY += p.AY * dt;
            p.VZ += p.AZ * dt;
        }

        if (Mode != 8)
            return;

        for (int i = 0; i < _particles.Length; i++)
        {
            ref Particle p = ref _particles[i];
            if (p.Life == Sentinel || p.Timer == Sentinel)
            {
                p.Life -= _lifeRate * dt;
                continue;
            }

            p.Timer -= _delayRate * dt;
            if (!(p.Timer < 0f))
                continue;

            p.Life = (float)(_random() * 0.5 + 0.5);
            p.Timer = Sentinel;
            p.Angle = (float)(_random() * 360.0 + 0.0);
            p.X = Cube();
            p.Y = Cube();
            p.Z = Cube();
        }
    }

    /// <summary>10008c5d..10008d2c: -999 marks a finished one-shot.</summary>
    void AdvanceFrame(ref Particle p, float dt)
    {
        if (p.Frame != -999f)
            p.Frame += _frameSpeed * dt;
        if (_lastFrame < p.Frame)
        {
            switch (_frameMode)
            {
                case 1:
                    p.Frame = _firstFrame;
                    break;
                case 2:
                    p.Frame = -999f;
                    break;
                case 3:
                    _frameSpeed = -_frameSpeed;
                    p.Frame += _frameSpeed * dt;
                    break;
            }
        }
        if (!(_firstFrame <= p.Frame) && _frameMode == 3)
        {
            _frameSpeed = -_frameSpeed;
            p.Frame += _frameSpeed * dt;
        }
    }

    /// <summary>
    /// The draw's fade and size for a mode-8 particle (<c>1000a919</c>..<c>1000a974</c>), or false when it
    /// isn't drawn. <paramref name="distanceFade"/> is the camera-distance fade already worked out.
    /// </summary>
    public bool Drawn(in Particle p, float distanceFade, out float fade, out float width, out float height)
    {
        fade = distanceFade;
        width = Width;
        height = Height;
        if (!(0f < p.Scale) || p.Frame < 0f)
            return false;
        if (!(0f < p.Life) || p.Life == Sentinel)
            return false;
        float s = (float)Math.Sin(p.Life * 1.5707963267948966);
        fade *= s;
        width *= s;
        height *= s;
        return true;
    }

    /// <summary>
    /// 1000a79f..1000a84c: 1 when fields 28-30 are all 0; else not drawn outside [28, 30], and
    /// 1 - |d - field 29| / (the half it falls in) inside.
    /// </summary>
    public bool DistanceFade(float d, out float fade)
    {
        fade = 1f;
        if (FadeNear == 0f && FadeMid == 0f && FadeFar == 0f)
            return true;
        if (FadeNear > d || FadeFar < d)
            return false;
        float off = Math.Abs(d - FadeMid);
        float half = FadeMid <= d ? FadeFar - FadeMid : FadeMid - FadeNear;
        fade = 1f - off / half;
        return true;
    }

    static float F(float[] f, int i) => f != null && i >= 0 && i < f.Length ? f[i] : 0f;

    static int Int(float[] f, int i) => f != null && i >= 0 && i < f.Length ? BitConverter.SingleToInt32Bits(f[i]) : 0;
}
