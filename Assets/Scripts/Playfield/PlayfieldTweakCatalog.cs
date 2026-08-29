using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Per-playfield JSON tweaks from project/build folder <c>twk/&lt;playfieldId&gt;.json</c>.
/// </summary>
public static class PlayfieldTweakCatalog
{
    const string FolderName = "twk";

    static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore
    };

    static readonly Dictionary<int, PlayfieldTweakFile> Cache = new Dictionary<int, PlayfieldTweakFile>();

    public static string TwkDirectory =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", FolderName));

    public static PlayfieldTweakFile Get(int playfieldId)
    {
        if (Cache.TryGetValue(playfieldId, out PlayfieldTweakFile cached))
            return cached;

        PlayfieldTweakFile tweak = LoadFromDisk(playfieldId) ?? new PlayfieldTweakFile();
        Cache[playfieldId] = tweak;
        return tweak;
    }

    public static void ClearCache() => Cache.Clear();

    public static WaterSurfaceType ResolveSurfaceType(PlayfieldTweakFile tweak, int bodyIndex)
    {
        PlayfieldWaterBodyTweak body = FindBody(tweak, bodyIndex);
        if (body == null || string.IsNullOrWhiteSpace(body.SurfaceType))
            return WaterSurfaceType.Pool;

        return ParseSurfaceType(body.SurfaceType);
    }

    public static ShoreWaveSettings ResolveWaves(PlayfieldTweakFile tweak, int bodyIndex)
    {
        PlayfieldWaterBodyTweak body = FindBody(tweak, bodyIndex);
        return ShoreWaveSettings.FromTweak(body?.Waves);
    }

    static PlayfieldWaterBodyTweak FindBody(PlayfieldTweakFile tweak, int bodyIndex)
    {
        if (tweak?.WaterBodies == null)
            return null;

        for (int i = 0; i < tweak.WaterBodies.Count; i++)
        {
            PlayfieldWaterBodyTweak body = tweak.WaterBodies[i];
            if (body != null && body.Index == bodyIndex)
                return body;
        }

        return null;
    }

    static WaterSurfaceType ParseSurfaceType(string value)
    {
        string normalized = value.Trim();
        if (normalized.Equals("Ocean", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("OceanSeaLake", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Sea", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Lake", StringComparison.OrdinalIgnoreCase))
            return WaterSurfaceType.OceanSeaLake;

        if (normalized.Equals("River", StringComparison.OrdinalIgnoreCase))
            return WaterSurfaceType.River;

        if (normalized.Equals("Pool", StringComparison.OrdinalIgnoreCase))
            return WaterSurfaceType.Pool;

        Debug.LogWarning($"[PlayfieldTweak] Unknown surfaceType '{value}', using Pool.");
        return WaterSurfaceType.Pool;
    }

    static PlayfieldTweakFile LoadFromDisk(int playfieldId)
    {
        string path = Path.Combine(TwkDirectory, $"{playfieldId}.json");
        if (!File.Exists(path))
            return null;

        try
        {
            string json = File.ReadAllText(path);
            PlayfieldTweakFile dto = JsonConvert.DeserializeObject<PlayfieldTweakFile>(json, JsonSettings);
            if (dto == null)
            {
                Debug.LogWarning($"[PlayfieldTweak] Empty tweak file '{path}'.");
                return null;
            }

            Debug.Log($"[PlayfieldTweak] Loaded tweaks for playfield {playfieldId} from {path}");
            return dto;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PlayfieldTweak] Failed to load '{path}': {ex.Message}");
            return null;
        }
    }
}

[Serializable]
public sealed class PlayfieldTweakFile
{
    public List<PlayfieldWaterBodyTweak> WaterBodies;
}

[Serializable]
public sealed class PlayfieldWaterBodyTweak
{
    public int Index;
    public string SurfaceType;
    public ShoreWaveTweak Waves;
}

[Serializable]
public sealed class ShoreWaveTweak
{
    public bool? Enabled;
    public float? Spacing;
    public float? DistanceFromLand;
    public float? AlongShoreOffset;
    public float? ShoreHeightEpsilon;
    public int? MaxActive;
    public float? ActivationRadius;
    public float? UpdateInterval;
    public float? MoveThreshold;
    public float? RegionSizeX;
    public float? RegionSizeZ;
    public float? Amplitude;
    public float? Wavelength;
    public float? SkippedWaves;
    public float? Speed;
    public float? WaveOffset;
    public float[] BlendRange;
    public float[] BreakingRange;
    public float[] DeepFoamRange;
    public float? SurfaceFoamDimmer;
    public float? DeepFoamDimmer;
}

public struct ShoreWaveSettings
{
    public bool Enabled;
    public float Spacing;
    public float DistanceFromLand;
    public float AlongShoreOffset;
    public float ShoreHeightEpsilon;
    public int MaxActive;
    public float ActivationRadius;
    public float UpdateInterval;
    public float MoveThreshold;
    public float RegionSizeX;
    public float RegionSizeZ;
    public float Amplitude;
    public float Wavelength;
    public float SkippedWaves;
    public float Speed;
    public float WaveOffset;
    public Vector2 BlendRange;
    public Vector2 BreakingRange;
    public Vector2 DeepFoamRange;
    public float SurfaceFoamDimmer;
    public float DeepFoamDimmer;

    public static ShoreWaveSettings Defaults => new ShoreWaveSettings
    {
        Enabled = true,
        Spacing = 18f,
        DistanceFromLand = 4f,
        AlongShoreOffset = 0f,
        ShoreHeightEpsilon = 0.25f,
        MaxActive = 24,
        ActivationRadius = 120f,
        UpdateInterval = 0.5f,
        MoveThreshold = 8f,
        RegionSizeX = 20f,
        RegionSizeZ = 12f,
        Amplitude = 1.2f,
        Wavelength = 8f,
        SkippedWaves = 3f,
        Speed = 12f,
        WaveOffset = 0f,
        BlendRange = new Vector2(0.3f, 0.7f),
        BreakingRange = new Vector2(0.5f, 0.8f),
        DeepFoamRange = new Vector2(0.2f, 0.6f),
        SurfaceFoamDimmer = 1f,
        DeepFoamDimmer = 1f
    };

    public static ShoreWaveSettings FromTweak(ShoreWaveTweak raw)
    {
        ShoreWaveSettings s = Defaults;
        if (raw == null)
            return s;

        if (raw.Enabled.HasValue) s.Enabled = raw.Enabled.Value;
        if (raw.Spacing.HasValue) s.Spacing = Mathf.Max(1f, raw.Spacing.Value);
        if (raw.DistanceFromLand.HasValue) s.DistanceFromLand = raw.DistanceFromLand.Value;
        if (raw.AlongShoreOffset.HasValue) s.AlongShoreOffset = raw.AlongShoreOffset.Value;
        if (raw.ShoreHeightEpsilon.HasValue) s.ShoreHeightEpsilon = Mathf.Max(0f, raw.ShoreHeightEpsilon.Value);
        if (raw.MaxActive.HasValue) s.MaxActive = Mathf.Clamp(raw.MaxActive.Value, 1, 48);
        if (raw.ActivationRadius.HasValue) s.ActivationRadius = Mathf.Max(1f, raw.ActivationRadius.Value);
        if (raw.UpdateInterval.HasValue) s.UpdateInterval = Mathf.Max(0.05f, raw.UpdateInterval.Value);
        if (raw.MoveThreshold.HasValue) s.MoveThreshold = Mathf.Max(0f, raw.MoveThreshold.Value);
        if (raw.RegionSizeX.HasValue) s.RegionSizeX = Mathf.Max(0.5f, raw.RegionSizeX.Value);
        if (raw.RegionSizeZ.HasValue) s.RegionSizeZ = Mathf.Max(0.5f, raw.RegionSizeZ.Value);
        if (raw.Amplitude.HasValue) s.Amplitude = raw.Amplitude.Value;
        if (raw.Wavelength.HasValue) s.Wavelength = Mathf.Max(0.01f, raw.Wavelength.Value);
        if (raw.SkippedWaves.HasValue) s.SkippedWaves = Mathf.Max(1f, raw.SkippedWaves.Value);
        if (raw.Speed.HasValue) s.Speed = raw.Speed.Value;
        if (raw.WaveOffset.HasValue) s.WaveOffset = raw.WaveOffset.Value;
        if (TryReadRange(raw.BlendRange, out Vector2 blend)) s.BlendRange = blend;
        if (TryReadRange(raw.BreakingRange, out Vector2 breaking)) s.BreakingRange = breaking;
        if (TryReadRange(raw.DeepFoamRange, out Vector2 deepFoam)) s.DeepFoamRange = deepFoam;
        if (raw.SurfaceFoamDimmer.HasValue) s.SurfaceFoamDimmer = Mathf.Clamp01(raw.SurfaceFoamDimmer.Value);
        if (raw.DeepFoamDimmer.HasValue) s.DeepFoamDimmer = Mathf.Clamp01(raw.DeepFoamDimmer.Value);
        return s;
    }

    static bool TryReadRange(float[] values, out Vector2 range)
    {
        range = default;
        if (values == null || values.Length < 2)
            return false;
        range = new Vector2(values[0], values[1]);
        return true;
    }
}
