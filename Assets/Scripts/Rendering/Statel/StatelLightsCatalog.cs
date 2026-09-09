using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine;

/// <summary>
/// Maps raw RDB mesh names (without extension) to light replacements from
/// project/build <c>twk/Lights.json</c>.
/// </summary>
public static class StatelLightsCatalog
{
    const string FileName = "Lights.json";

    static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore
    };

    static Dictionary<string, StatelLightTweak> _byName;
    static bool _loaded;

    public static void ClearCache()
    {
        _byName = null;
        _loaded = false;
    }

    public static bool TryGet(string rawMeshName, out StatelLightTweak settings)
    {
        settings = null;
        if (string.IsNullOrEmpty(rawMeshName))
            return false;

        Dictionary<string, StatelLightTweak> map = Get();
        if (map == null || map.Count == 0)
            return false;

        string key = NormalizeKey(rawMeshName);
        if (string.IsNullOrEmpty(key))
            return false;

        if (map.TryGetValue(key, out settings) && settings != null)
            return true;

        // Allow exact raw-name keys (including extension) if authored that way.
        return map.TryGetValue(rawMeshName.Trim('\0'), out settings) && settings != null;
    }

    static Dictionary<string, StatelLightTweak> Get()
    {
        if (_loaded)
            return _byName;

        _loaded = true;
        _byName = LoadFromDisk();
        return _byName;
    }

    static Dictionary<string, StatelLightTweak> LoadFromDisk()
    {
        string path = Path.Combine(PlayfieldTweakCatalog.TwkDirectory, FileName);
        if (!File.Exists(path))
        {
            Debug.LogWarning($"[StatelLights] File not found: {path}");
            return null;
        }

        try
        {
            string json = File.ReadAllText(path);
            Dictionary<string, StatelLightTweak> dto =
                JsonConvert.DeserializeObject<Dictionary<string, StatelLightTweak>>(json, JsonSettings);
            if (dto == null || dto.Count == 0)
            {
                Debug.LogWarning($"[StatelLights] Empty tweak file '{path}'.");
                return null;
            }

            var normalized = new Dictionary<string, StatelLightTweak>(dto.Count, StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, StatelLightTweak> kvp in dto)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key) || kvp.Value == null)
                    continue;

                string key = NormalizeKey(kvp.Key);
                if (string.IsNullOrEmpty(key))
                    continue;

                normalized[key] = kvp.Value;
            }

            Debug.Log($"[StatelLights] Loaded {normalized.Count} light replacements from {path}");
            return normalized;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[StatelLights] Failed to load '{path}': {ex.Message}");
            return null;
        }
    }

    /// <summary>Strips <c>.abiff</c> and trims; lookup is case-insensitive.</summary>
    public static string NormalizeKey(string rawMeshName)
    {
        if (string.IsNullOrEmpty(rawMeshName))
            return null;

        string name = rawMeshName.Trim().Trim('\0');
        if (name.EndsWith(".abiff", StringComparison.OrdinalIgnoreCase))
            name = name.Substring(0, name.Length - ".abiff".Length);

        return string.IsNullOrEmpty(name) ? null : name;
    }
}

[Serializable]
public sealed class StatelLightTweak
{
    /// <summary>Point (default), Spot, or Directional.</summary>
    public string Type;
    public float[] Color;
    /// <summary>
    /// Intensity in candela for Point/Spot (HDRP native punctual unit).
    /// Outdoor scenes often need thousands to read next to the sun.
    /// </summary>
    public float? Intensity;
    public float? Range;
    public bool? Shadows;
    /// <summary>HDRP light volumetric dimmer (0–16). Needs volumetric fog enabled in the Volume.</summary>
    public float? VolumetricDimmer;
    public float? SpotAngle;
    /// <summary>Inner spot cone as percent of <see cref="SpotAngle"/> (0–100).</summary>
    public float? InnerSpotPercent;
    public float? BounceIntensity;
}
