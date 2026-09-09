using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine;

/// <summary>
/// Global mesh-name skip list from project/build <c>twk/SkippedStatels.json</c>.
/// Matched against raw RDB mesh names (before sanitization).
/// </summary>
public static class SkippedStatelsCatalog
{
    const string FileName = "SkippedStatels.json";

    static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore
    };

    static SkippedStatelsFile _cached;
    static bool _loaded;

    public static void ClearCache()
    {
        _cached = null;
        _loaded = false;
    }

    public static bool ShouldSkip(string rawMeshName)
    {
        if (string.IsNullOrEmpty(rawMeshName))
            return false;

        SkippedStatelsFile file = Get();
        if (file == null)
            return false;

        if (file.Names != null)
        {
            for (int i = 0; i < file.Names.Length; i++)
            {
                string entry = file.Names[i];
                if (!string.IsNullOrEmpty(entry) &&
                    string.Equals(rawMeshName, entry, StringComparison.Ordinal))
                    return true;
            }
        }

        if (file.Prefixes != null)
        {
            for (int i = 0; i < file.Prefixes.Length; i++)
            {
                string entry = file.Prefixes[i];
                if (!string.IsNullOrEmpty(entry) &&
                    rawMeshName.StartsWith(entry, StringComparison.Ordinal))
                    return true;
            }
        }

        if (file.Contains != null)
        {
            for (int i = 0; i < file.Contains.Length; i++)
            {
                string entry = file.Contains[i];
                if (!string.IsNullOrEmpty(entry) &&
                    rawMeshName.IndexOf(entry, StringComparison.Ordinal) >= 0)
                    return true;
            }
        }

        return false;
    }

    static SkippedStatelsFile Get()
    {
        if (_loaded)
            return _cached;

        _loaded = true;
        _cached = LoadFromDisk();
        return _cached;
    }

    static SkippedStatelsFile LoadFromDisk()
    {
        string path = Path.Combine(PlayfieldTweakCatalog.TwkDirectory, FileName);
        if (!File.Exists(path))
        {
            Debug.LogWarning($"[SkippedStatels] File not found: {path}");
            return null;
        }

        try
        {
            string json = File.ReadAllText(path);
            SkippedStatelsFile dto = JsonConvert.DeserializeObject<SkippedStatelsFile>(json, JsonSettings);
            if (dto == null)
            {
                Debug.LogWarning($"[SkippedStatels] Empty tweak file '{path}'.");
                return null;
            }

            Debug.Log($"[SkippedStatels] Loaded skip rules from {path}");
            return dto;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[SkippedStatels] Failed to load '{path}': {ex.Message}");
            return null;
        }
    }
}

[Serializable]
public sealed class SkippedStatelsFile
{
    /// <summary>Exact raw RDB mesh names to skip.</summary>
    public string[] Names;
    /// <summary>Skip when the raw name starts with any of these.</summary>
    public string[] Prefixes;
    /// <summary>Skip when the raw name contains any of these.</summary>
    public string[] Contains;
}
