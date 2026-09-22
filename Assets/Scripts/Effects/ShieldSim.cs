using System;

/// <summary>
/// Stock <c>_GfxControlShield_t</c> (typeCode 3003 / 0xbbb, vftable <c>Gamecode 1016d9a4</c>) and the
/// per-vertex rules of its visual, DisplaySystem <c>GfxVisualShield</c> (ctor <c>1001ce93</c>, vertex
/// build <c>1001c94f</c>): a textured shell over the host's own skinned CAT mesh (43608, nano 56213's
/// hit and, through Sequencer 43613, its buff).
///
/// Control fields (loader <c>100edeed</c>, visual build <c>100edf78</c>): 0 flags, 8 duration, 9 material,
/// 10 colour (D3DCOLOR), 11 offset along the normals, 12-14 wave point (flag 0x800), 15-17 wave
/// direction (flag 0x1000, scaled to length 1), 18 UV mode, 19/20 U/V scale, 21 fade-in rate, 22 wave
/// speed, 23 fade-in limit, 24/25 U/V scroll, 26 pulse mode, 27/28 pulse range, 29 fade mode, 30 fade
/// time, 31 fade mode 3's start. Flag 0x400 goes to the visual as its blend flag.
///
/// Process (<c>100edcf4</c>): the visual's time T is the age, bent by the pulse (mode 1: sawtooth over
/// [27, 28]; mode 2: triangle) once it passes field 27. Terminating (slot 6, <c>100edc9c</c>, and by
/// itself once 0 &lt; duration and duration - fade time &lt; age) starts the fade: with d the time since,
/// mode 1 fades the alpha by 1 - d / fade time, mode 2 runs T back from where it was, mode 3 runs it
/// back from field 31; the control is ready once d reaches the fade time.
///
/// Per vertex (the skinned position p, normal n and UV): position p + n * field 11; UV by mode (0: source
/// UV scrolled then scaled, 1: cylindrical, 2: planar); colour field 10's RGB with alpha
/// A * fade * sin²(clamp(field 21 * T, 0, field 23)). Flag 0x800 makes that a wave from the field
/// 12-14 point (or along the 15-17 direction with 0x1000) moving at field 21 / field 22, and 0x2000
/// ripples it by sin²(10 (x + y + z) + 3T) over the bind positions; both need per-vertex alpha.
///
/// Stock's fmod (<c>1013f2ec</c>) keeps the dividend's sign, as C#'s % on doubles does.
///
/// No Unity dependency so the recovered maths can be asserted from plain unit tests.
/// </summary>
public sealed class ShieldSim
{
    public const int FlagAdditive = 0x400;
    public const int FlagWavePoint = 0x800;
    public const int FlagWaveDirection = 0x1000;
    public const int FlagRipple = 0x2000;
    public const int FlagHostMaterial = 0x10000;

    /// <summary>1001ca3a: 2 pi as a float, widened.</summary>
    const double TwoPi = 6.2831854820251465;

    public int Flags { get; }
    public uint Colour { get; }
    public float Offset { get; }
    public int UvMode { get; }
    public float UScale { get; }
    public float VScale { get; }
    public float FadeInRate { get; }
    public float WaveSpeed { get; }
    public float FadeInLimit { get; }
    public float UScroll { get; }
    public float VScroll { get; }
    public int PulseMode { get; }
    public float PulseMin { get; }
    public float PulseMax { get; }
    public int FadeMode { get; }
    public float FadeTime { get; }
    public float FadeFrom { get; }

    /// <summary>Stock +0x10.</summary>
    public float Duration { get; set; }

    /// <summary>Stock +0x54.</summary>
    public bool Terminating { get; private set; }

    /// <summary>The visual's clock (+0x2c0) after the last <see cref="Step"/>.</summary>
    public float Time { get; private set; }

    /// <summary>The visual's alpha scale (+0x1a8), 1 until fade mode 1 lowers it.</summary>
    public float Fade { get; private set; } = 1f;

    float _terminatedAt;
    float _terminateTime;

    public ShieldSim(float[] fields)
    {
        Flags = Int(fields, 0);
        Duration = F(fields, 8);
        Colour = unchecked((uint)Int(fields, 10));
        Offset = F(fields, 11);
        UvMode = Int(fields, 18);
        UScale = F(fields, 19);
        VScale = F(fields, 20);
        FadeInRate = F(fields, 21);
        WaveSpeed = F(fields, 22);
        FadeInLimit = F(fields, 23);
        UScroll = F(fields, 24);
        VScroll = F(fields, 25);
        PulseMode = Int(fields, 26);
        PulseMin = F(fields, 27);
        PulseMax = F(fields, 28);
        FadeMode = Int(fields, 29);
        FadeTime = F(fields, 30);
        FadeFrom = F(fields, 31);
    }

    /// <summary>True when every vertex takes the same colour (no wave, no ripple).</summary>
    public bool UniformColour => (Flags & (FlagWavePoint | FlagRipple)) == 0;

    /// <summary>Stock slot 6 (<c>100edc9c</c>): start the fade, once.</summary>
    public void Terminate(float age)
    {
        if (Terminating)
            return;
        _terminatedAt = age;
        _terminateTime = age;
        if (PulseMin < PulseMax)
            _terminateTime = (float)(((double)age - PulseMin) % ((double)PulseMax - PulseMin) + PulseMin);
        Terminating = true;
    }

    /// <summary>
    /// The control's body at <paramref name="age"/>. True when it is ready. The base expiry
    /// (0 &lt;= duration &lt; age) is the caller's.
    /// </summary>
    public bool Step(float age)
    {
        bool ready = false;
        float v = age;
        if (Terminating)
        {
            float d = (float)((double)age - _terminatedAt);
            switch (FadeMode)
            {
                case 1:
                    Fade = FadeTime != 0f ? (float)(1.0 - (double)d / FadeTime) : 0f;
                    break;
                case 2:
                    v = (float)((double)_terminateTime - d);
                    break;
                case 3:
                    v = (float)((double)FadeFrom - d);
                    break;
            }

            if (FadeTime <= d)
                ready = true;
        }

        if (PulseMin < v)
        {
            float range = (float)((double)PulseMax - PulseMin);
            if (PulseMode == 1)
            {
                v = (float)((float)((double)v - PulseMin) % (double)range + PulseMin);
            }
            else if (PulseMode == 2)
            {
                float a = (float)((double)v - PulseMin + range);
                float m = (float)(a % (double)(float)(range * 2.0));
                v = (float)(Math.Abs((double)range - m) + PulseMin);
            }
        }

        Time = v;

        if (0f < Duration && (float)((double)Duration - FadeTime) < age)
            Terminate(age);

        return ready;
    }

    /// <summary>
    /// The shell's alpha byte for the uniform case (<c>1001cab2</c>): A * fade / 255, then
    /// _ftol(a * 255 * sin²(clamp(field 21 * T, 0, field 23))).
    /// </summary>
    public int UniformAlpha()
    {
        float a = (float)((Colour >> 24) * (double)Fade / 255.0);
        float x = (float)((double)FadeInRate * Time);
        if (!(0f < x || 0f == x))
            x = 0f;
        if (FadeInLimit < x)
            x = FadeInLimit;
        float s = (float)Math.Sin(x);
        double alpha = a * 255.0 * s * s;
        return double.IsNaN(alpha) ? 0 : (int)alpha;
    }

    /// <summary>
    /// UV for one vertex (<c>1001c9b8</c>): <paramref name="u"/>/<paramref name="v"/> the source UV,
    /// <paramref name="x"/>/<paramref name="y"/>/<paramref name="z"/> the skinned position before the offset.
    /// </summary>
    public void Uv(float u, float v, float x, float y, float z, out float outU, out float outV)
    {
        float uScroll = (float)((double)UScroll * Time);
        float vScroll = (float)((double)VScroll * Time);
        switch (UvMode)
        {
            case 0:
                outU = (float)(((double)u + uScroll) * UScale);
                outV = (float)(((double)v + vScroll) * VScale);
                return;
            case 1:
                // Cylindrical; stock scrolls v by the U scroll here too (1001ca74).
                outU = (float)(Math.Atan2(x, z) * (UScale / TwoPi) + uScroll);
                outV = (float)((double)y * VScale + uScroll);
                return;
            case 2:
                outU = (float)((double)x * UScale + uScroll);
                outV = (float)((double)y * VScale + vScroll);
                return;
            default:
                outU = u;
                outV = v;
                return;
        }
    }

    static float F(float[] f, int i) => f != null && i < f.Length ? f[i] : 0f;

    static int Int(float[] f, int i) => f != null && i < f.Length ? BitConverter.SingleToInt32Bits(f[i]) : 0;
}
