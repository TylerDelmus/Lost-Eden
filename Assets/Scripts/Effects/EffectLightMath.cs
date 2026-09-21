using System;

/// <summary>
/// Intensity and range curves for the pooled effect lights, kept free of UnityEngine so the
/// brightness rules can be unit tested. <see cref="EffectLightPool"/> wraps these with Color.
///
/// The central rule: alpha is the fade channel, so alpha 0 must produce intensity 0. An earlier
/// "treat 0 as opaque" fallback here made faded-out effects hold a full-brightness light for the
/// rest of the control's duration.
/// </summary>
public static class EffectLightMath
{
    public const float BaseNanoCandela = 156250f;
    public const float BaseStarsCandela = 250000f;

    /// <summary>Below this the pool drops the light out of HDRP culling entirely.</summary>
    public const float VisibleCandela = 0.01f;

    public static float Luminance(float r, float g, float b)
        => Clamp01(0.2126f * r + 0.7152f * g + 0.0722f * b);

    public static float NanoIntensity(float r, float g, float b, float a)
        => Intensity(BaseNanoCandela, r, g, b, a);

    public static float StarsIntensity(float r, float g, float b, float a)
        => Intensity(BaseStarsCandela, r, g, b, a);

    static float Intensity(float baseCandela, float r, float g, float b, float a)
    {
        // Floor the luminance so saturated single-channel nanos still read as bright, but never
        // let it rescue a transparent colour: alpha gates the whole term.
        float lum = Math.Max(0.5f, Luminance(r, g, b));
        return baseCandela * lum * Clamp01(a);
    }

    public static float NanoRange(float scale)
        => Clamp(40f * Math.Max(0.05f, scale), 24f, 120f);

    public static float StarsRange(float spawnRadius)
        => Clamp(spawnRadius * 32f, 40f, 140f);

    static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

    static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;
}
