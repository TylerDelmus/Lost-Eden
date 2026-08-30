using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine;

/// <summary>
/// Loads Lost Eden JSON overlays from project/build <c>twk/</c>, named like AO tweak files
/// (e.g. <c>Tweak_Rubi-Ka_Sun.json</c>). Overlays merge in include order; later files win.
/// <c>Tweak_Playfield_&lt;id&gt;.json</c> is always applied last for that playfield.
/// </summary>
public static class LostEdenTweakCatalog
{
    static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore
    };

    public static string TwkDirectory => PlayfieldTweakCatalog.TwkDirectory;

    public static LostEdenTweakFile LoadMergedForPlayfield(int playfieldId, IEnumerable<string> aoIncludeFileNames)
    {
        LostEdenTweakFile merged = LoadMergedForIncludes(aoIncludeFileNames);

        // Playfield LE overlay always wins over shared includes (entry is first in AO include order).
        string playfieldJson = $"Tweak_Playfield_{playfieldId}.json";
        LostEdenTweakFile playfieldOverlay = TryLoad(playfieldJson);
        if (playfieldOverlay != null)
        {
            MergeInto(merged, playfieldOverlay);
            Debug.Log($"[AoTweak] LE playfield overlay loaded: {playfieldJson}");
        }

        return merged;
    }

    public static LostEdenTweakFile LoadMergedForIncludes(IEnumerable<string> aoIncludeFileNames)
    {
        var merged = new LostEdenTweakFile();
        if (aoIncludeFileNames == null)
            return merged;

        int loaded = 0;
        foreach (string aoName in aoIncludeFileNames)
        {
            if (string.IsNullOrWhiteSpace(aoName))
                continue;

            string jsonName = ToJsonFileName(aoName);
            // Applied again last via LoadMergedForPlayfield so shared includes do not override it.
            if (IsPlayfieldTweakFileName(jsonName))
                continue;

            LostEdenTweakFile overlay = TryLoad(jsonName);
            if (overlay == null)
                continue;

            MergeInto(merged, overlay);
            loaded++;
            Debug.Log($"[AoTweak] LE overlay loaded: {jsonName}");
        }

        if (loaded > 0)
            Debug.Log($"[AoTweak] Merged {loaded} Lost Eden tweak overlay(s).");

        return merged;
    }

    static bool IsPlayfieldTweakFileName(string jsonFileName)
    {
        return jsonFileName.StartsWith("Tweak_Playfield_", StringComparison.OrdinalIgnoreCase)
            && jsonFileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
    }

    public static void ClearCache()
    {
        // Stateless loads for now; hook retained for playfield unload symmetry.
    }

    static string ToJsonFileName(string aoFileName)
    {
        string name = aoFileName.Trim();
        if (name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            name = name.Substring(0, name.Length - 4);
        if (!name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            name += ".json";
        return name;
    }

    static LostEdenTweakFile TryLoad(string jsonFileName)
    {
        string path = Path.Combine(TwkDirectory, jsonFileName);
        if (!File.Exists(path))
            return null;

        try
        {
            string json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<LostEdenTweakFile>(json, JsonSettings);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[AoTweak] Failed to load LE tweak '{path}': {ex.Message}");
            return null;
        }
    }

    static void MergeInto(LostEdenTweakFile target, LostEdenTweakFile overlay)
    {
        if (overlay.PrimarySun != null)
        {
            target.PrimarySun ??= new LostEdenPrimarySunTweak();
            MergePrimary(target.PrimarySun, overlay.PrimarySun);
        }

        if (overlay.CompanionSun != null)
        {
            target.CompanionSun ??= new LostEdenCompanionSunTweak();
            MergeCompanion(target.CompanionSun, overlay.CompanionSun);
        }

        if (overlay.SkipSkyMeshes != null && overlay.SkipSkyMeshes.Length > 0)
            target.SkipSkyMeshes = MergeSkipSkyMeshes(target.SkipSkyMeshes, overlay.SkipSkyMeshes);

        if (overlay.Objects != null && overlay.Objects.Count > 0)
        {
            target.Objects ??= new Dictionary<string, LostEdenSkyObjectTweak>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, LostEdenSkyObjectTweak> pair in overlay.Objects)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null)
                    continue;

                if (!target.Objects.TryGetValue(pair.Key, out LostEdenSkyObjectTweak existing) || existing == null)
                {
                    target.Objects[pair.Key] = CloneObject(pair.Value);
                    continue;
                }

                MergeObject(existing, pair.Value);
            }
        }

        if (overlay.Fog != null)
        {
            target.Fog ??= new LostEdenFogTweak();
            MergeFog(target.Fog, overlay.Fog);
        }
    }

    static void MergeFog(LostEdenFogTweak t, LostEdenFogTweak o)
    {
        if (o.AttenuationDistance.HasValue) t.AttenuationDistance = o.AttenuationDistance;
        if (o.BaseHeight.HasValue) t.BaseHeight = o.BaseHeight;
        if (o.MaximumHeight.HasValue) t.MaximumHeight = o.MaximumHeight;
        if (o.MaxFogDistance.HasValue) t.MaxFogDistance = o.MaxFogDistance;
        if (!string.IsNullOrWhiteSpace(o.Tint)) t.Tint = o.Tint;
        if (o.TintRgb != null) t.TintRgb = o.TintRgb;
        if (o.TintIntensity.HasValue) t.TintIntensity = o.TintIntensity;
        if (o.EnableVolumetricFog.HasValue) t.EnableVolumetricFog = o.EnableVolumetricFog;
        if (o.GiDimmer.HasValue) t.GiDimmer = o.GiDimmer;
        if (!string.IsNullOrWhiteSpace(o.DenoisingMode)) t.DenoisingMode = o.DenoisingMode;
        if (!string.IsNullOrWhiteSpace(o.Tier)) t.Tier = o.Tier;
        if (o.VolumetricLighting.HasValue) t.VolumetricLighting = o.VolumetricLighting;
    }

    static LostEdenSkyObjectTweak CloneObject(LostEdenSkyObjectTweak o)
    {
        return new LostEdenSkyObjectTweak
        {
            Enabled = o.Enabled,
            Scale = o.Scale,
            Intensity = o.Intensity,
            Position = o.Position
        };
    }

    static void MergeObject(LostEdenSkyObjectTweak t, LostEdenSkyObjectTweak o)
    {
        if (o.Enabled.HasValue) t.Enabled = o.Enabled;
        if (o.Scale.HasValue) t.Scale = o.Scale;
        if (o.Intensity.HasValue) t.Intensity = o.Intensity;
        if (o.Position != null) t.Position = o.Position;
    }

    static string[] MergeSkipSkyMeshes(string[] existing, string[] overlay)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (existing != null)
        {
            for (int i = 0; i < existing.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(existing[i]))
                    set.Add(existing[i].Trim());
            }
        }

        for (int i = 0; i < overlay.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(overlay[i]))
                set.Add(overlay[i].Trim());
        }

        var result = new string[set.Count];
        set.CopyTo(result);
        return result;
    }

    static void MergePrimary(LostEdenPrimarySunTweak t, LostEdenPrimarySunTweak o)
    {
        if (o.Intensity.HasValue) t.Intensity = o.Intensity;
        if (o.AngularDiameter.HasValue) t.AngularDiameter = o.AngularDiameter;
        if (o.FlareSize.HasValue) t.FlareSize = o.FlareSize;
        if (o.FlareTint != null) t.FlareTint = o.FlareTint;
        if (o.FlareFalloff.HasValue) t.FlareFalloff = o.FlareFalloff;
        if (o.FlareMultiplier.HasValue) t.FlareMultiplier = o.FlareMultiplier;
        if (o.SurfaceTint != null) t.SurfaceTint = o.SurfaceTint;
    }

    static void MergeCompanion(LostEdenCompanionSunTweak t, LostEdenCompanionSunTweak o)
    {
        if (o.Enabled.HasValue) t.Enabled = o.Enabled;
        if (o.Intensity.HasValue) t.Intensity = o.Intensity;
        if (o.YawOffsetDeg.HasValue) t.YawOffsetDeg = o.YawOffsetDeg;
        if (o.PitchOffsetDeg.HasValue) t.PitchOffsetDeg = o.PitchOffsetDeg;
        if (o.AngularDiameter.HasValue) t.AngularDiameter = o.AngularDiameter;
        if (o.Color != null) t.Color = o.Color;
        if (o.FlareSize.HasValue) t.FlareSize = o.FlareSize;
        if (o.FlareTint != null) t.FlareTint = o.FlareTint;
        if (o.FlareFalloff.HasValue) t.FlareFalloff = o.FlareFalloff;
        if (o.FlareMultiplier.HasValue) t.FlareMultiplier = o.FlareMultiplier;
        if (o.SurfaceTint != null) t.SurfaceTint = o.SurfaceTint;
    }
}
