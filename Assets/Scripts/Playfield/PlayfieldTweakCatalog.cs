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
    /// <summary>How far along the coast from the camera/player waves stay active (alias of activationRadius).</summary>
    public float? AlongCoastRange;
    public float? UpdateInterval;
    public float? MoveThreshold;
    public float? RegionSizeX;
    public float? RegionSizeZ;
    public float? Amplitude;
    public float? Wavelength;
    public float? SkippedWaves;
    public float? SkippedWavesJitter;
    public float? Speed;
    public float? WaveOffset;
    public float? StartPhase;
    public float[] BlendRange;
    public float[] BreakingRange;
    public float[] DeepFoamRange;
    public float? SurfaceFoamDimmer;
    public float? DeepFoamDimmer;
    public float? SpawnJitterDistance;
    public float? SpawnJitterAlong;
    public float? SpawnJitterAngleDeg;
    public float? SpawnSkipChance;
    public float? SpacingJitter;
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
    public bool AutoRegionSizeX;
    public bool AutoRegionSizeZ;
    public float Amplitude;
    public float Wavelength;
    public bool AutoWavelength;
    public float SkippedWaves;
    public float SkippedWavesJitter;
    public float Speed;
    public float WaveOffset;
    public bool AutoStartPhase;
    public float StartPhase;
    public Vector2 BlendRange;
    public Vector2 BreakingRange;
    public Vector2 DeepFoamRange;
    public bool AutoBreaking;
    public float SurfaceFoamDimmer;
    public float DeepFoamDimmer;
    public float SpawnJitterDistance;
    public float SpawnJitterAlong;
    public float SpawnJitterAngleDeg;
    public float SpawnSkipChance;
    public float SpacingJitter;

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
        AutoRegionSizeX = true,
        AutoRegionSizeZ = true,
        Amplitude = 1.2f,
        Wavelength = 8f,
        AutoWavelength = true,
        SkippedWaves = 3f,
        SkippedWavesJitter = 2f,
        Speed = 12f,
        WaveOffset = 0f,
        AutoStartPhase = true,
        StartPhase = 1f,
        BlendRange = new Vector2(0.3f, 0.7f),
        BreakingRange = new Vector2(0.55f, 0.9f),
        DeepFoamRange = new Vector2(0.35f, 0.75f),
        AutoBreaking = true,
        SurfaceFoamDimmer = 1f,
        DeepFoamDimmer = 1f,
        SpawnJitterDistance = 0.18f,
        SpawnJitterAlong = 0.35f,
        SpawnJitterAngleDeg = 14f,
        SpawnSkipChance = 0.12f,
        SpacingJitter = 0.25f
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
        if (raw.AlongCoastRange.HasValue)
            s.ActivationRadius = Mathf.Max(1f, raw.AlongCoastRange.Value);
        else if (raw.ActivationRadius.HasValue)
            s.ActivationRadius = Mathf.Max(1f, raw.ActivationRadius.Value);
        if (raw.UpdateInterval.HasValue) s.UpdateInterval = Mathf.Max(0.05f, raw.UpdateInterval.Value);
        if (raw.MoveThreshold.HasValue) s.MoveThreshold = Mathf.Max(0f, raw.MoveThreshold.Value);
        if (raw.RegionSizeX.HasValue)
        {
            s.RegionSizeX = Mathf.Max(0.5f, raw.RegionSizeX.Value);
            s.AutoRegionSizeX = false;
        }

        if (raw.RegionSizeZ.HasValue)
        {
            s.RegionSizeZ = Mathf.Max(0.5f, raw.RegionSizeZ.Value);
            s.AutoRegionSizeZ = false;
        }

        if (raw.Amplitude.HasValue) s.Amplitude = raw.Amplitude.Value;
        if (raw.Wavelength.HasValue)
        {
            s.Wavelength = Mathf.Max(0.01f, raw.Wavelength.Value);
            s.AutoWavelength = false;
        }

        if (raw.SkippedWaves.HasValue) s.SkippedWaves = Mathf.Max(1f, raw.SkippedWaves.Value);
        if (raw.SkippedWavesJitter.HasValue) s.SkippedWavesJitter = Mathf.Max(0f, raw.SkippedWavesJitter.Value);
        if (raw.Speed.HasValue) s.Speed = raw.Speed.Value;
        if (raw.WaveOffset.HasValue)
        {
            s.WaveOffset = raw.WaveOffset.Value;
            s.AutoStartPhase = false;
        }

        if (raw.StartPhase.HasValue)
        {
            s.StartPhase = Mathf.Clamp01(raw.StartPhase.Value);
            s.AutoStartPhase = true;
        }
        if (TryReadRange(raw.BlendRange, out Vector2 blend)) s.BlendRange = blend;
        if (TryReadRange(raw.BreakingRange, out Vector2 breaking))
        {
            s.BreakingRange = breaking;
            s.AutoBreaking = false;
        }

        if (TryReadRange(raw.DeepFoamRange, out Vector2 deepFoam))
        {
            s.DeepFoamRange = deepFoam;
            s.AutoBreaking = false;
        }

        if (raw.SurfaceFoamDimmer.HasValue) s.SurfaceFoamDimmer = Mathf.Clamp01(raw.SurfaceFoamDimmer.Value);
        if (raw.DeepFoamDimmer.HasValue) s.DeepFoamDimmer = Mathf.Clamp01(raw.DeepFoamDimmer.Value);
        if (raw.SpawnJitterDistance.HasValue) s.SpawnJitterDistance = Mathf.Clamp01(raw.SpawnJitterDistance.Value);
        if (raw.SpawnJitterAlong.HasValue) s.SpawnJitterAlong = Mathf.Max(0f, raw.SpawnJitterAlong.Value);
        if (raw.SpawnJitterAngleDeg.HasValue) s.SpawnJitterAngleDeg = Mathf.Max(0f, raw.SpawnJitterAngleDeg.Value);
        if (raw.SpawnSkipChance.HasValue) s.SpawnSkipChance = Mathf.Clamp01(raw.SpawnSkipChance.Value);
        if (raw.SpacingJitter.HasValue) s.SpacingJitter = Mathf.Clamp01(raw.SpacingJitter.Value);
        return s;
    }

    /// <summary>
    /// WaterDecals are centered on the transform with +X toward land.
    /// Landward half-extent must cover <see cref="DistanceFromLand"/> plus a small overrun
    /// so foam breaks on the beach: regionSizeX = 2 * (D + overrun).
    /// </summary>
    public Vector2 ResolveRegionSize()
    {
        float d = Mathf.Max(1f, DistanceFromLand);
        float overrun = Mathf.Max(3f, d * 0.12f);
        float autoX = 2f * (d + overrun);
        float autoZ = Mathf.Clamp(Spacing * 0.9f, d * 0.25f, Mathf.Max(Spacing * 1.15f, 8f));

        float sizeX = AutoRegionSizeX ? autoX : Mathf.Max(0.5f, RegionSizeX);
        float sizeZ = AutoRegionSizeZ ? autoZ : Mathf.Max(0.5f, RegionSizeZ);
        return new Vector2(sizeX, sizeZ);
    }

    /// <summary>
    /// Random skip count around <see cref="SkippedWaves"/> (inclusive jitter in crest counts).
    /// </summary>
    public int RollSkippedWaves(System.Random rng)
    {
        float average = Mathf.Max(1f, SkippedWaves);
        float jitter = Mathf.Max(0f, SkippedWavesJitter);
        float min = Mathf.Max(1f, average - jitter);
        float max = Mathf.Max(min, average + jitter);
        double t = rng.NextDouble();
        return Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(min, max, (float)t)));
    }

    public float ResolveWavelength(Vector2 regionSize)
    {
        if (!AutoWavelength)
            return Mathf.Max(0.01f, Wavelength);

        // A few wave crests across the run-up from spawn to shore.
        float runUp = Mathf.Max(1f, regionSize.x * 0.5f);
        return Mathf.Clamp(runUp * 0.35f, 5f, 48f);
    }

    /// <summary>
    /// Initial shore-wave phase on first load. <see cref="StartPhase"/> 1 shifts the
    /// train so a crest is already near the shore instead of waiting for the run-up.
    /// </summary>
    public float ResolveWaveOffset(Vector2 regionSize)
    {
        if (!AutoStartPhase)
            return WaveOffset;

        float shoreUv = (BreakingRange.x + BreakingRange.y) * 0.5f;
        float shoreLocal = shoreUv * 2f - 1f; // [-1, 1] across the decal
        // Material stores offset in the same normalized space as (uv*2-1).
        return shoreLocal * Mathf.Clamp01(StartPhase) * Mathf.Max(regionSize.x, regionSize.y);
    }

    public void ResolveBreaking(Vector2 regionSize, out Vector2 breaking, out Vector2 deepFoam)
    {
        if (!AutoBreaking)
        {
            breaking = BreakingRange;
            deepFoam = DeepFoamRange;
            return;
        }

        float d = Mathf.Max(1f, DistanceFromLand);
        // Shore lies at +D along local X from center; UV x = 0.5 + D / regionSizeX.
        float shoreUv = Mathf.Clamp01(0.5f + d / Mathf.Max(regionSize.x, 0.01f));
        breaking = new Vector2(
            Mathf.Clamp01(shoreUv - 0.18f),
            Mathf.Clamp01(shoreUv + 0.04f));
        deepFoam = new Vector2(
            Mathf.Clamp01(shoreUv - 0.35f),
            Mathf.Clamp01(shoreUv - 0.05f));
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
