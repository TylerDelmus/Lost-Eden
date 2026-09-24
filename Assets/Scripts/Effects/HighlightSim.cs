using System;

/// <summary>
/// Stock <c>_GfxControlHighlight_t</c> (type 2011, 0x7db; vftable <c>Gamecode 1016d164</c>, loader
/// <c>100e2dc0</c>, Process <c>100e2efc</c>, start <c>100e309b</c>): no visual of its own — it tints the
/// target's existing mesh through randy31's <c>RRefFrame_t::SetTransparency</c>,
/// <c>SetEmissive</c> and, in mode 3, <c>SetSpecular</c>.
///
/// Fields: 0 flags (+0x84), 1 the mode (+0x3c), 2 the duration (+0x10), 3-6 the start block and 7-10 the
/// end block, both read as (transparency, r, g, b) by the ramp at +0x50 (<c>101085de</c>, eight
/// consecutive floats from field 3), 11 an int that sets +0x39 when non-zero — every record has 0 there.
///
/// Mode 2 is the odd one: the loader copies the duration into the pulse length (+0x4c) and then forces
/// the duration to <b>-1</b> (<c>100e2e1e</c>), so it runs until something terminates it.
///
/// The phase the ramp is evaluated at (<c>100e2f38</c>):
/// <list type="bullet">
/// <item>mode 0: <c>age / duration</c>, unclamped.</item>
/// <item>modes 1 and 3: <c>1 - (2 * age / duration - 1)^2</c> — a smooth arc up and back down.</item>
/// <item>mode 2: rising <c>min(age / pulse, 1)</c>; once terminated, <c>(duration - age) / pulse</c>.</item>
/// <item>any other mode: 0.</item>
/// </list>
///
/// No Unity dependency so the recovered curves can be asserted from plain unit tests.
/// </summary>
public sealed class HighlightSim
{
    /// <summary>A plain ramp over the duration.</summary>
    public const int ModeRamp = 0;

    /// <summary>Up and back down over the duration.</summary>
    public const int ModeArc = 1;

    /// <summary>Holds until terminated, then fades over one pulse.</summary>
    public const int ModeHold = 2;

    /// <summary>Like <see cref="ModeArc"/>, and the only mode that writes specular.</summary>
    public const int ModeSpecular = 3;

    /// <summary>Mode 2's loader forces the duration to this (<c>10156dd0</c>).</summary>
    public const float HoldDuration = -1f;

    public readonly int Flags;
    public readonly int Mode;

    /// <summary>Field 2 as the loader leaves it: mode 2 turns it into -1.</summary>
    public readonly float Duration;

    /// <summary>Mode 2's +0x4c, the rise and fade length; field 2 for every other mode.</summary>
    public readonly float Pulse;

    public readonly int Field11;

    /// <summary>Set by a graceful terminate; stock's +0x38.</summary>
    public bool FadingOut { get; private set; }

    public bool WritesSpecular => Mode == ModeSpecular;

    public HighlightSim(float[] fields)
    {
        Flags = Int(fields, 0);
        Mode = Int(fields, 1);
        float duration = F(fields, 2);
        Field11 = Int(fields, 11);

        // 100e2e1e: mode 2 keeps the field as its pulse and runs on an infinite duration.
        if (Mode == ModeHold)
        {
            Pulse = duration;
            Duration = HoldDuration;
        }
        else
        {
            Pulse = duration;
            Duration = duration;
        }
    }

    /// <summary>Stock's slot 6 for mode 2 raises +0x38; every other mode simply ends.</summary>
    public void Terminate() => FadingOut = true;

    /// <summary>
    /// The phase the colour ramp is read at (<c>100e2f38</c>). <paramref name="duration"/> is the
    /// control's live duration, which for mode 2 only becomes finite once something sets it.
    /// </summary>
    public float Phase(float age, float duration)
    {
        switch (Mode)
        {
            case ModeRamp:
                // 100e3093: no clamp in stock; the control ends at the duration anyway.
                return duration != 0f ? age / duration : 0f;

            case ModeArc:
            case ModeSpecular:
            {
                // 100e306b: 1 - (2u - 1)^2.
                float u = duration != 0f ? age / duration : 0f;
                float x = u + u - 1f;
                return 1f - x * x;
            }

            case ModeHold:
            {
                if (Pulse == 0f)
                    return 0f;
                if (FadingOut)
                    return (duration - age) / Pulse;
                // 100e2f79: the rise is held at 1.
                float t = age / Pulse;
                return 1f < t ? 1f : t;
            }

            default:
                // 100e2f55: an unknown mode leaves the phase at 0.
                return 0f;
        }
    }

    static float F(float[] f, int i) => f != null && i >= 0 && i < f.Length ? f[i] : 0f;

    static int Int(float[] f, int i) => f != null && i >= 0 && i < f.Length ? GfxBits.Of(f, i) : 0;
}
