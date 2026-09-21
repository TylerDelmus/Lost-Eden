using System;

/// <summary>
/// Field layout for the stock tracer / beam-ribbon controls (typeCodes 1013, 1024, 1025, 1026).
///
/// These records are NOT laid out like the sprite family, which is easy to get wrong because they
/// share fields 0, 8 and 9 with it. The mapping below was read off the typeCode 0x401 template
/// loader at <c>0x101001d0</c>, which assigns each template field to a fixed object offset:
///
///   field 0  -> +0x34 flags (int)      field 12 -> +0x44 ribbon node width
///   field 8  -> +0x10 duration         field 13 -> +0x48 colour, high byte
///   field 9  -> +0x38 material (int)   field 14 -> +0x4c colour
///   field 10 -> +0x3c speed            field 15 -> +0x50 colour
///   field 11 -> +0x40 trail length     field 16 -> +0x54 colour, low byte
///   fields 17..19 -> +0x58..+0x60 (int)
///
/// The init at <c>0x10100554</c> then rounds each of those four colour floats to a byte and packs
/// them <c>f13 &lt;&lt; 24 | f14 &lt;&lt; 16 | f15 &lt;&lt; 8 | f16</c>, i.e. a D3DCOLOR 0xAARRGGBB, so field 13
/// is alpha and 14/15/16 are red/green/blue. There is a single constant colour, not a start/end
/// pair. The geometry update at <c>0x1010028b</c> reads the width straight out of +0x44 for each of
/// the ribbon's three nodes, confirming field 12 is a width and not a size-channel pair.
///
/// Field 11 is confirmed a length by the per-frame update at <c>0x10100431</c>, which advances the
/// strip along the segment as <c>tail = speed * age</c> and <c>head = field11 + tail</c>, clamping
/// the head to the segment length and ending the control once the tail reaches it. Note that a
/// tracer is not always longer than it is wide — the 1024 records are stubbier than they are broad —
/// so the two must be read independently rather than derived from each other or from texture aspect.
///
/// The beam-cylinder loader at <c>0x100fd9c6</c> / init at <c>0x100fdcb2</c> is structurally
/// identical and uses the same offsets, so the layout is shared across the family.
/// </summary>
public static class TracerMath
{
    public const int FieldFlags = 0;
    public const int FieldDuration = 8;
    public const int FieldMaterial = 9;
    public const int FieldSpeed = 10;
    public const int FieldLength = 11;
    public const int FieldWidth = 12;
    public const int FieldAlpha = 13;
    public const int FieldRed = 14;
    public const int FieldGreen = 15;
    public const int FieldBlue = 16;

    /// <summary>Lowest index a record must reach for the whole colour block to be present.</summary>
    public const int MinFieldCount = FieldBlue + 1;

    /// <summary>
    /// Stock rounds alpha to a byte, so anything below half a step is genuinely invisible rather
    /// than merely faint. Record 45705 is all zeroes and is meant to draw nothing at all.
    /// </summary>
    public const float MinVisibleAlpha = 0.5f / 255f;

    /// <summary>Ribbon width, from field 12. Stock widths run 0.02 to 0.5.</summary>
    public static float Width(float field12) => Sanitise(field12, 8f);

    /// <summary>
    /// Trail length, from field 11. The ceiling is deliberately generous: most tracers are a couple
    /// of metres, but record 70004 is a 100 metre beam.
    /// </summary>
    public static float Length(float field11) => Sanitise(field11, 1000f);

    /// <summary>
    /// True when the template describes something that can actually be seen. A degenerate width or
    /// length collapses the strip, and a zero alpha over an additive blend contributes nothing.
    /// </summary>
    public static bool IsVisible(float width, float length, float alpha)
        => width > 0f && length > 0f && alpha >= MinVisibleAlpha;

    static float Sanitise(float value, float max)
    {
        if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f || value > max)
            return 0f;
        return value;
    }
}
