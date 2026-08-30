using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lost Eden additive JSON overlay for an AO tweak include (e.g. <c>Tweak_Rubi-Ka_Sun.json</c>).
/// </summary>
[Serializable]
public sealed class LostEdenTweakFile
{
    public LostEdenPrimarySunTweak PrimarySun;
    public LostEdenCompanionSunTweak CompanionSun;
    /// <summary>AO sky object names to skip (legacy; prefer <see cref="Objects"/> enabled:false).</summary>
    public string[] SkipSkyMeshes;
    /// <summary>Per AO object overrides keyed by tweak object name (e.g. HorizonCloudsRim).</summary>
    public Dictionary<string, LostEdenSkyObjectTweak> Objects;
    public LostEdenFogTweak Fog;
}

/// <summary>
/// HDRP Fog overrides (attenuation distance = mean free path).
/// Null / omitted optional fields disable that Volume override.
/// </summary>
[Serializable]
public sealed class LostEdenFogTweak
{
    /// <summary>HDRP Fog mean free path (attenuation distance).</summary>
    public float? AttenuationDistance;
    public float? BaseHeight;
    public float? MaximumHeight;
    public float? MaxFogDistance;
    /// <summary>Hex color e.g. "#DCFFFA", or omit and use <see cref="TintRgb"/>.</summary>
    public string Tint;
    /// <summary>Optional linear RGB 0-1 (used if <see cref="Tint"/> is empty).</summary>
    public float[] TintRgb;
    /// <summary>Multiplies tint RGB (HDR). Default 1.</summary>
    public float? TintIntensity;
    public bool? EnableVolumetricFog;
    /// <summary>HDRP Fog GI Dimmer (0-1).</summary>
    public float? GiDimmer;
    /// <summary>HDRP Fog denoising mode: None, Reprojection, Gaussian, Both (or int flags).</summary>
    public string DenoisingMode;
    /// <summary>HDRP Fog quality tier: Low, Medium, High (or 0-2).</summary>
    public string Tier;
    /// <summary>HDRP Fog volumetric lighting density cutoff.</summary>
    public float? VolumetricLighting;
}

/// <summary>
/// Override fields for a camera-locked AO sky object.
/// </summary>
[Serializable]
public sealed class LostEdenSkyObjectTweak
{
    public bool? Enabled;
    public float? Scale;
    public float? Intensity;
    /// <summary>Optional position offset [x,y,z] in AO tweak space.</summary>
    public float[] Position;
}

[Serializable]
public sealed class LostEdenPrimarySunTweak
{
    public float? Intensity;
    public float? AngularDiameter;
    public float? FlareSize;
    public float[] FlareTint;
    public float? FlareFalloff;
    public float? FlareMultiplier;
    public float[] SurfaceTint;
}

[Serializable]
public sealed class LostEdenCompanionSunTweak
{
    public bool? Enabled;
    public float? Intensity;
    public float? YawOffsetDeg;
    public float? PitchOffsetDeg;
    public float? AngularDiameter;
    public float[] Color;
    public float? FlareSize;
    public float[] FlareTint;
    public float? FlareFalloff;
    public float? FlareMultiplier;
    public float[] SurfaceTint;
}

public static class LostEdenTweakColorUtil
{
    public static bool TryReadRgb(float[] rgb, out Color color)
    {
        color = Color.white;
        if (rgb == null || rgb.Length < 3)
            return false;
        color = new Color(rgb[0], rgb[1], rgb[2], rgb.Length >= 4 ? rgb[3] : 1f);
        return true;
    }

    public static bool TryReadVector3(float[] xyz, out Vector3 value)
    {
        value = default;
        if (xyz == null || xyz.Length < 3)
            return false;
        value = new Vector3(xyz[0], xyz[1], xyz[2]);
        return true;
    }

    public static bool TryReadTint(LostEdenFogTweak fog, out Color color)
    {
        color = Color.white;
        if (fog == null)
            return false;

        float intensity = fog.TintIntensity ?? 1f;
        if (!string.IsNullOrWhiteSpace(fog.Tint) && TryParseHexColor(fog.Tint, out color))
        {
            color = new Color(color.r * intensity, color.g * intensity, color.b * intensity, color.a);
            return true;
        }

        if (TryReadRgb(fog.TintRgb, out color))
        {
            color = new Color(color.r * intensity, color.g * intensity, color.b * intensity, color.a);
            return true;
        }

        return false;
    }

    public static bool TryParseHexColor(string hex, out Color color)
    {
        color = Color.white;
        if (string.IsNullOrWhiteSpace(hex))
            return false;

        hex = hex.Trim();
        if (hex.StartsWith("#", StringComparison.Ordinal))
            hex = hex.Substring(1);

        if (hex.Length == 6
            && byte.TryParse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out byte r)
            && byte.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out byte g)
            && byte.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out byte b))
        {
            color = new Color(r / 255f, g / 255f, b / 255f, 1f);
            return true;
        }

        return false;
    }
}
