using System;

/// <summary>
/// Stock <c>GfxControlBParticle_t</c> (type 3024, 0xbd0; vftable <c>Gamecode 1016efa4</c>, loader
/// <c>1010a644</c>, visual build <c>1010a412</c>, Process <c>1010a909</c>) and the DisplaySystem
/// <c>GfxVisualBParticle</c> it drives (ctor <c>10009938</c>, ProcessParticles <c>10008bce</c>, per-particle
/// draw <c>1000a70f</c>). The generic per-frame path is modelled in full; of the thirteen particle modes
/// only the ones with a recovered spawn are drawn, which is 8 and 1 (<see cref="Supported"/>).
///
/// Control fields: 0 flags, 8 duration, 9 material, 10 mode, 11 count, 12 spawn cube half-size,
/// 13 motion, 14 quad type, 15 life policy, 16 rate (the orbit's in motion 1, the pop delay in mode 8),
/// 17 life rate, 25/26/27 start / middle / end colour, 31/32, 33/34, 35/36 width and height at the
/// start / middle / end, motion 4 eases the sizes as t^8, 37 frame... (38 frame speed, 39 frame mode),
/// 40 first key time, 41 last key time (stored as 1 - field 41), 28-30 camera-distance fade.
///
/// Fields 18-24 (a respawn acceleration and a velocity box) are byte-identical in all 33 shipped
/// records and are only read by mode 12's out-of-line respawn at <c>10009754</c>, so nothing here uses
/// them. The first and last frame are not record fields at all: they come from the material's frame
/// count (<c>1010a44a</c>), which is why they arrive as constructor arguments.
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
        /// <summary>+0x34: motion 1's orbit angle, and mode 8's pop delay. Stock shares the slot.</summary>
        public float Timer;
        public float Life;
        /// <summary>+0x3c and +0x40, the per-particle UV offset mode 1 draws with.</summary>
        public float UvU, UvV;
        /// <summary>+0x4c..+0x54, the centre motion 1 orbits.</summary>
        public float CX, CY, CZ;
    }

    public readonly int Flags;
    public readonly int Mode;
    /// <summary>Field 13, stock's +0x204: 0 ballistic, 1 a sin/cos orbit, anything else still.</summary>
    public readonly int Motion;
    /// <summary>Field 14, stock's +0x1e8: 0 a camera-facing quad, 2 a tapered plume.</summary>
    public readonly int QuadType;
    /// <summary>Field 15, stock's +0x1ec: how the life at +0x38 runs and restarts.</summary>
    public readonly int LifePolicy;
    public readonly float Duration;
    readonly float _radius;
    readonly bool _ease;
    readonly float _rate;
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
    /// <summary>The modes whose spawn is recovered. The rest run but draw nothing.</summary>
    public bool Supported => Mode == 8 || Mode == 1;

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
        Motion = Int(fields, 13);
        QuadType = Int(fields, 14);
        LifePolicy = Int(fields, 15);
        _ease = Motion == 4;
        _rate = F(fields, 16);
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

    /// <summary>
    /// The ctor's per-mode spawn, through the jump table at <c>1000a6db</c>. Everything is zero to
    /// start with: stock news the array then memsets it (<c>10009ab1</c>).
    /// </summary>
    void Spawn()
    {
        switch (Mode)
        {
            case 1:
                SpawnOne();
                break;
            case 8:
                SpawnEight();
                break;
        }
    }

    /// <summary>Mode 1, <c>10009e46</c>: every particle starts on the emitter, aimed at random.</summary>
    void SpawnOne()
    {
        for (int i = 0; i < _particles.Length; i++)
        {
            ref Particle p = ref _particles[i];
            p.Angle = (float)(_random() * 360.0 + 0.0);
            p.Spin = 0f;
            p.Scale = 1f;
            p.Life = (float)(_random() * 2.0 - 1.0);
            p.UvU = (float)(_random() + 0.0);
            p.UvV = 0f;
        }
    }

    /// <summary>Mode 8, <c>1000a39b</c>.</summary>
    void SpawnEight()
    {
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

    /// <summary>
    /// <c>10008bce</c>: the spin and the frame machine, then the motion, then the life policy, then
    /// whatever the mode itself adds. Stock walks the array once per stage, so this does too.
    /// </summary>
    void ProcessParticles(float dt)
    {
        // 10008c17.
        for (int i = 0; i < _particles.Length; i++)
        {
            ref Particle p = ref _particles[i];
            p.Angle += p.Spin * dt;
            if (360f < p.Angle)
                p.Angle -= 360f;
            else if (0f > p.Angle)
                p.Angle += 360f;

            if (_frameMode != 0)
                AdvanceFrame(ref p, dt);
        }

        Move(dt);
        Age(dt);

        if (Mode == 8)
            PopDelayed(dt);
    }

    /// <summary>10008d3e: field 13 picks the motion, and anything but 0 or 1 leaves it where it is.</summary>
    void Move(float dt)
    {
        if (Motion == 0)
        {
            for (int i = 0; i < _particles.Length; i++)
            {
                ref Particle p = ref _particles[i];
                p.X += p.VX * dt;
                p.Y += p.VY * dt;
                p.Z += p.VZ * dt;
                p.VX += p.AX * dt;
                p.VY += p.AY * dt;
                p.VZ += p.AZ * dt;
            }
            return;
        }

        if (Motion != 1)
            return;

        // 10008d57: an orbit whose amplitudes are the constants 1, 0.4 and 0.7, not record fields.
        for (int i = 0; i < _particles.Length; i++)
        {
            ref Particle p = ref _particles[i];
            double radians = p.Timer * (0.7853981633974483 / 45.0);
            p.X = (float)(Math.Sin(radians) + p.CX);
            p.Y = (float)(Math.Sin(radians) * 0.4000000059604645 + p.CY);
            p.Z = (float)(Math.Cos(radians) * 0.699999988079071 + p.CZ);
            p.Timer += _rate * dt;
            if (360f < p.Timer)
                p.Timer -= 360f;
        }
    }

    /// <summary>10008ec8: field 15 picks how the life runs down and what happens when it runs out.</summary>
    void Age(float dt)
    {
        for (int i = 0; i < _particles.Length; i++)
        {
            ref Particle p = ref _particles[i];
            switch (LifePolicy)
            {
                case 1: // 10009248: a random walk kept inside [0, 1].
                    p.Life += (float)((_random() * 2.0 - 1.0) * _lifeRate * dt);
                    if (1f < p.Life || 0f > p.Life)
                        p.Life = (float)(_random() + 0.0);
                    break;

                case 2: // 100091ed: a sawtooth over [0, 2).
                    p.Life += _lifeRate * dt;
                    if (2f < p.Life)
                        p.Life -= 2f;
                    break;

                case 3: // 1000914e: restarts at a full life, re-aimed, but stays where it is.
                    p.Life -= _lifeRate * dt;
                    if (-1f > p.Life)
                    {
                        p.Life = 1f;
                        p.UvU = (float)(_random() + 0.0);
                        p.Angle = (float)(_random() * 360.0 + 0.0);
                    }
                    break;

                case 4: // 10009022 and 10008ef6 are the same code twice: restarts somewhere new.
                case 5:
                    p.Life -= _lifeRate * dt;
                    if (-1f > p.Life)
                    {
                        p.Life = (float)(_random() * 0.5 + 0.5);
                        p.UvU = (float)(_random() + 0.0);
                        p.Angle = (float)(_random() * 360.0 + 0.0);
                        p.X = Cube();
                        p.Y = Cube();
                        p.Z = Cube();
                    }
                    break;
            }
        }
    }

    /// <summary>Mode 8's own stage, <c>100093d9</c>: a staggered pop.</summary>
    void PopDelayed(float dt)
    {
        for (int i = 0; i < _particles.Length; i++)
        {
            ref Particle p = ref _particles[i];
            if (p.Life == Sentinel || p.Timer == Sentinel)
            {
                p.Life -= _lifeRate * dt;
                continue;
            }

            p.Timer -= _rate * dt;
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
        if (_firstFrame > p.Frame && _frameMode == 3)
        {
            _frameSpeed = -_frameSpeed;
            p.Frame += _frameSpeed * dt;
        }
    }

    /// <summary>
    /// The draw's fade and size for one particle, or false when it isn't drawn. Stock applies the life
    /// policy's rule first (<c>1000a855</c>) and then the mode's own (<c>1000a919</c>).
    /// <paramref name="distanceFade"/> is the camera-distance fade already worked out.
    /// </summary>
    public bool Drawn(in Particle p, float distanceFade, out float fade, out float width, out float height)
    {
        fade = distanceFade;
        width = Width;
        height = Height;
        // 1000a720 and 1000a72e.
        if (!(0f < p.Scale) || p.Frame < 0f)
            return false;

        switch (LifePolicy)
        {
            case 1: // 1000a900: the life caps the fade.
                if (!(fade < p.Life))
                    fade = p.Life;
                break;

            case 3: // 1000a8bd: the life is the fade, and nothing shows once it is spent.
                if (!(0f < p.Life))
                    return false;
                fade *= p.Life;
                break;

            case 4: // 1000a87f: a half sine over the life, on the size as well as the fade.
            {
                if (!(0f < p.Life))
                    return false;
                float half = (float)Math.Sin(p.Life * Math.PI);
                fade *= half;
                width *= half;
                height *= half;
                break;
            }
        }

        if (Mode == 8)
        {
            if (!(0f < p.Life) || p.Life == Sentinel)
                return false;
            float s = (float)Math.Sin(p.Life * 1.5707963267948966);
            fade *= s;
            width *= s;
            height *= s;
        }

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
