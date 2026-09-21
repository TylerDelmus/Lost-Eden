using UnityEngine;

/// <summary>
/// Stock gfxtweak color blocks are packed as A,R,G,B (D3DCOLOR / sprite tint order).
/// Alpha 0 in the tweak means opaque (same as <see cref="NanoEffectResolver.FromArgb"/>).
/// </summary>
public static class EffectColors
{
    public static Color ReadArgbBlock(GfxTweakRecord record, int start, Color fallback)
    {
        if (record == null || record.FieldCount <= start + 3)
            return fallback;

        float a = Mathf.Clamp01(record.Field(start, fallback.a > 0f ? fallback.a : 1f));
        float r = Mathf.Clamp01(record.Field(start + 1, fallback.r));
        float g = Mathf.Clamp01(record.Field(start + 2, fallback.g));
        float b = Mathf.Clamp01(record.Field(start + 3, fallback.b));
        if (a <= 0f)
            a = 1f;
        return new Color(r, g, b, a);
    }

    /// <summary>
    /// True when <paramref name="tint"/> is a meaningful non-white override.
    /// Neutral / white tints must not replace template ARGB colors.
    /// </summary>
    public static bool IsOverrideTint(Color tint)
    {
        if (tint.a <= 0.01f)
            return false;
        if (tint.r + tint.g + tint.b <= 0.01f)
            return false;
        // Default CreateEffect2 tint is Color.white — keep template colors.
        return Mathf.Abs(tint.r - 1f) > 0.01f
               || Mathf.Abs(tint.g - 1f) > 0.01f
               || Mathf.Abs(tint.b - 1f) > 0.01f;
    }

    /// <summary>
    /// Replaces a template's own RGB with an overriding one, keeping the template's alpha so its
    /// fade still runs.
    ///
    /// This used to multiply the two, which silently destroys any colour the two do not share. The
    /// Spell1 hand-cast ladder is the case that exposed it: every Flare rung from 46000 to 46017
    /// carries the same generic blue-ish 0.5,0.4,1.0, and the per-spell colour lives on the Spell1
    /// record instead — 46133 (nano 45680) is blue and 46269 (nano 201937) is green. Multiplying the
    /// green spell into the blue template gave 0,0.2,0.1, effectively black, so 201937's hand spikes
    /// could not be seen whatever geometry they were given. Since the rungs are shared across the
    /// whole ladder and the Spell1 block is what differs per spell, the spell's colour is the one
    /// that has to win.
    /// </summary>
    public static Color ApplyTint(Color baseColor, Color tint)
    {
        if (!IsOverrideTint(tint))
            return baseColor;
        return new Color(
            tint.r,
            tint.g,
            tint.b,
            baseColor.a * Mathf.Clamp01(tint.a > 0f ? tint.a : 1f));
    }
}
