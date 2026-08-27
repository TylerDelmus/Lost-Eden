using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine;

public static class AnimCalibration
{
    const string FileName = "anim_calibration.json";

    static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        Formatting = Formatting.Indented
    };

    static Dictionary<int, Entry> _entries;
    static bool _loaded;

    public struct Entry
    {
        public float TrimStart;
        public float TrimEnd;

        public bool HasTrim => TrimStart > 0f || TrimEnd > 0f;
    }

    public static string ConfigPath =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", FileName));

    public static bool TryGet(int animId, out Entry entry)
    {
        EnsureLoaded();
        return _entries.TryGetValue(animId, out entry);
    }

    public static Entry GetOrDefault(int animId)
    {
        return TryGet(animId, out Entry entry) ? entry : default;
    }

    public static void Set(int animId, float trimStart, float trimEnd)
    {
        if (animId <= 0)
            return;

        EnsureLoaded();
        trimStart = Mathf.Max(0f, trimStart);
        trimEnd = Mathf.Max(0f, trimEnd);

        if (trimStart <= 0f && trimEnd <= 0f)
        {
            _entries.Remove(animId);
            return;
        }

        _entries[animId] = new Entry
        {
            TrimStart = trimStart,
            TrimEnd = trimEnd
        };
    }

    public static void Save()
    {
        EnsureLoaded();

        var dto = new CalibrationDto
        {
            Anims = new List<AnimDto>(_entries.Count)
        };

        foreach (KeyValuePair<int, Entry> pair in _entries)
        {
            dto.Anims.Add(new AnimDto
            {
                AnimId = pair.Key,
                TrimStart = pair.Value.TrimStart,
                TrimEnd = pair.Value.TrimEnd
            });
        }

        dto.Anims.Sort((a, b) => a.AnimId.CompareTo(b.AnimId));

        string path = ConfigPath;
        string json = JsonConvert.SerializeObject(dto, JsonSettings);
        File.WriteAllText(path, json);
        Debug.Log($"[AnimCalibration] Saved {_entries.Count} entr(y/ies) → {path}");
    }

    public static void Reload()
    {
        _loaded = false;
        _entries = null;
        EnsureLoaded();
    }

    static void EnsureLoaded()
    {
        if (_loaded)
            return;

        _entries = new Dictionary<int, Entry>();
        _loaded = true;

        string path = ConfigPath;
        if (!File.Exists(path))
            return;

        try
        {
            string json = File.ReadAllText(path);
            var dto = JsonConvert.DeserializeObject<CalibrationDto>(json, JsonSettings);
            if (dto?.Anims == null)
                return;

            for (int i = 0; i < dto.Anims.Count; i++)
            {
                AnimDto anim = dto.Anims[i];
                if (anim.AnimId <= 0)
                    continue;

                _entries[anim.AnimId] = new Entry
                {
                    TrimStart = Mathf.Max(0f, anim.TrimStart),
                    TrimEnd = Mathf.Max(0f, anim.TrimEnd)
                };
            }

            Debug.Log($"[AnimCalibration] Loaded {_entries.Count} entr(y/ies) from {path}");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[AnimCalibration] Failed to load '{path}': {ex.Message}");
        }
    }

    sealed class CalibrationDto
    {
        public List<AnimDto> Anims;
    }

    sealed class AnimDto
    {
        public int AnimId;
        public float TrimStart;
        public float TrimEnd;
    }
}
