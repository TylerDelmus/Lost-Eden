using System;

/// <summary>
/// Stock <c>GfxControlTParticle_t</c> (type 3020, 0xbcc; vftable <c>Gamecode 1016f9dc</c>, loader
/// <c>10112ccd</c>, visual build <c>10112b4e</c>, Process <c>10113101</c>) and the DisplaySystem
/// <c>GfxVisualTParticle</c> it drives (ctor <c>10029de9</c>, ProcessParticles <c>10029c81</c>, per-particle
/// geometry <c>1002a350</c>): N streaks flying out of the emitter, each drawn from its head to a tail
/// that trails behind by its velocity.
///
/// Fields: 0 flags, 8 duration, 9 material, 10 mode, 11 count, 12 radius, 15 trail time, 16 gravity (y),
/// 17/18, 19/20, 21/22 velocity x, y, z ranges, 23/25/27 tail colour at the start / middle / end,
/// 24/26/28 head colour, 32 spin range (degrees/s; the angle is kept but never drawn), 34 = 4 eases the
/// blends as t^8, 35/37/39 tail half-width, 36/38/40 head half-width. Flag 0x100 swaps the texture
/// axes, 0x200 is additive.
///
/// Spawn (in the visual's ctor, all at once, in the emitter's frame), by mode:
/// <list type="bullet">
/// <item>0: p = a random direction (a cube draw, set to length 1 when not zero) times r * radius,
/// v = a draw in the velocity box, a = (0, gravity, 0).</item>
/// <item>1: only the tail, at (2r - 1) * radius per axis; nothing moves.</item>
/// <item>2: with angle a = r * 2pi, p = (sin a * radius, r * 600, cos a * radius),
/// v = (sin(a + 2) * vx max, vy max, cos(a + 2) * vz max), a = (0, gravity, 0).</item>
/// <item>3: everything zero but v.z = 1; nothing moves.</item>
/// </list>
/// ProcessParticles(dt): modes 0 and 2 move <c>p += v dt; v += a dt</c>, then tail = p - v * trail.
///
/// The control, each call, with u = time / duration (ready past 1): colours and half-widths blend
/// start to middle over the first half (2u) and middle to end over the second (2u - 1).
///
/// No Unity dependency so the recovered maths can be asserted from plain unit tests.
/// </summary>
public sealed class TParticleSim
{
    public const int FlagSwapUv = 0x100;
    public const int FlagAdditive = 0x200;

    // 10029fed.
    const double ColumnHeight = 600.0;

    // 1002a2d2 / 10029cac.
    const double FullTurn = 360.0;

    public struct Particle
    {
        public float X, Y, Z;
        public float VX, VY, VZ;
        public float AX, AY, AZ;
        public float TX, TY, TZ;
        public float Angle, Spin;
    }

    public readonly int Flags;
    public readonly int Mode;
    public readonly float Duration;
    readonly float _radius;
    readonly float _trail;
    readonly float _gravity;
    readonly float _vx0, _vy0, _vz0, _vx1, _vy1, _vz1;
    readonly float _spin;
    readonly bool _ease;
    readonly uint[] _tailColour = new uint[3];
    readonly uint[] _headColour = new uint[3];
    readonly float[] _tailWidth = new float[3];
    readonly float[] _headWidth = new float[3];
    readonly Func<float> _random;
    readonly Particle[] _particles;
    float _time;

    public Particle[] Particles => _particles;
    public bool Additive => (Flags & FlagAdditive) != 0;
    public bool SwapUv => (Flags & FlagSwapUv) != 0;

    /// <summary>This frame's head and tail colour (D3DCOLOR) and half-widths, set by <see cref="Advance"/>.</summary>
    public uint HeadArgb { get; private set; } = 0xffffffffu;
    public uint TailArgb { get; private set; } = 0xffffffffu;
    public float HeadWidth { get; private set; }
    public float TailWidth { get; private set; }

    public TParticleSim(float[] fields, Func<float> random)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
        Flags = Int(fields, 0);
        Duration = F(fields, 8);
        Mode = Int(fields, 10);
        int count = Math.Max(0, Math.Min(Int(fields, 11), 4096));
        _radius = F(fields, 12);
        _trail = F(fields, 15);
        _gravity = F(fields, 16);
        _vx0 = F(fields, 17);
        _vx1 = F(fields, 18);
        _vy0 = F(fields, 19);
        _vy1 = F(fields, 20);
        _vz0 = F(fields, 21);
        _vz1 = F(fields, 22);
        for (int k = 0; k < 3; k++)
        {
            _tailColour[k] = unchecked((uint)Int(fields, 23 + 2 * k));
            _headColour[k] = unchecked((uint)Int(fields, 24 + 2 * k));
            _tailWidth[k] = F(fields, 35 + 2 * k);
            _headWidth[k] = F(fields, 36 + 2 * k);
        }
        _spin = F(fields, 32);
        _ease = Int(fields, 34) == 4;
        _particles = new Particle[count];
        Spawn();
    }

    /// <summary>The visual's ctor, <c>10029f1d</c>..<c>1002a330</c>.</summary>
    void Spawn()
    {
        for (int i = 0; i < _particles.Length; i++)
        {
            ref Particle p = ref _particles[i];
            switch (Mode)
            {
                case 0:
                {
                    float x = (float)(_random() * 2.0 - 1.0);
                    float y = (float)(_random() * 2.0 - 1.0);
                    float z = (float)(_random() * 2.0 - 1.0);
                    float length = (float)Math.Sqrt((float)(x * x + y * y + z * z));
                    if (length != 0f)
                    {
                        x /= length;
                        y /= length;
                        z /= length;
                    }
                    float scale = (float)(_random() * (_radius - 0.0) + 0.0);
                    p.X = scale * x;
                    p.Y = scale * y;
                    p.Z = scale * z;
                    p.VX = Range(_vx0, _vx1);
                    p.VY = Range(_vy0, _vy1);
                    p.VZ = Range(_vz0, _vz1);
                    p.AX = 0f;
                    p.AY = _gravity;
                    p.AZ = 0f;
                    p.Angle = (float)(_random() * FullTurn + 0.0);
                    p.Spin = (float)(_random() * (_spin - -_spin) + -_spin);
                    break;
                }
                case 1:
                    p.TX = (float)((_random() * 2.0 - 1.0) * _radius);
                    p.TY = (float)((_random() * 2.0 - 1.0) * _radius);
                    p.TZ = (float)((_random() * 2.0 - 1.0) * _radius);
                    break;
                case 2:
                {
                    float a = (float)(_random() * 6.2831854820251465 + 0.0);
                    p.X = (float)(Math.Sin(a) * _radius);
                    p.Y = (float)(_random() * ColumnHeight);
                    p.Z = (float)(Math.Cos(a) * _radius);
                    float b = (float)(a + 2.0);
                    p.VX = (float)(Math.Sin(b) * _vx1);
                    p.VY = _vy1;
                    p.VZ = (float)(Math.Cos(b) * _vz1);
                    p.AX = 0f;
                    p.AY = _gravity;
                    p.AZ = 0f;
                    break;
                }
                case 3:
                    p = default;
                    p.VZ = 1f;
                    break;
            }
        }
    }

    float Range(float lo, float hi) => (float)(_random() * (hi - lo) + lo);

    /// <summary>
    /// The control's Process for one call (<c>10113123</c>..<c>101133dc</c>), then the visual's
    /// ProcessParticles. Returns false once the time passes the duration (ready).
    /// </summary>
    public bool Advance(float dt)
    {
        _time += dt;
        float u = 0f;
        if (!(Duration < 0f))
        {
            u = _time / Duration;
            if (1f < u)
                return false;
        }

        // 101131a0: the first half runs while u < 0.5.
        if (u < 0.5f)
        {
            float t = Ease((float)(u + (double)u));
            TailArgb = StockColorCurve.Interpolate(_tailColour[0], _tailColour[1], t);
            HeadArgb = StockColorCurve.Interpolate(_headColour[0], _headColour[1], t);
            TailWidth = (_tailWidth[1] - _tailWidth[0]) * t + _tailWidth[0];
            HeadWidth = (_headWidth[1] - _headWidth[0]) * t + _headWidth[0];
        }
        else
        {
            float t = Ease((float)(u + (double)u - 1.0));
            TailArgb = StockColorCurve.Interpolate(_tailColour[1], _tailColour[2], t);
            HeadArgb = StockColorCurve.Interpolate(_headColour[1], _headColour[2], t);
            TailWidth = (_tailWidth[2] - _tailWidth[1]) * t + _tailWidth[1];
            HeadWidth = (_headWidth[2] - _headWidth[1]) * t + _headWidth[1];
        }

        ProcessParticles(dt);
        return true;
    }

    /// <summary>Field 34 = 4: <c>1003dfc0(t, 8)</c>, an integer power.</summary>
    float Ease(float t)
    {
        if (!_ease)
            return t;
        float r = t;
        for (int k = 0; k < 3; k++)
            r *= r;
        return r;
    }

    /// <summary><c>10029c81</c>.</summary>
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
        }

        if (Mode != 0 && Mode != 2)
            return;

        for (int i = 0; i < _particles.Length; i++)
        {
            ref Particle p = ref _particles[i];
            p.X += p.VX * dt;
            p.Y += p.VY * dt;
            p.Z += p.VZ * dt;
            p.VX += p.AX * dt;
            p.VY += p.AY * dt;
            p.VZ += p.AZ * dt;
            p.TX = p.X - p.VX * _trail;
            p.TY = p.Y - p.VY * _trail;
            p.TZ = p.Z - p.VZ * _trail;
        }
    }

    static float F(float[] f, int i) => f != null && i >= 0 && i < f.Length ? f[i] : 0f;

    static int Int(float[] f, int i) => f != null && i >= 0 && i < f.Length ? GfxBits.Of(f, i) : 0;
}
