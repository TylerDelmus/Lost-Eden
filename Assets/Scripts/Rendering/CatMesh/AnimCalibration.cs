using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Per-clip playback-rate calibration keyed by (animId, anim-set slot). Defaults to 1.0.
/// Optional overrides live in StreamingAssets/anim_calibration.json.
/// </summary>
public static class AnimCalibration
{
    const string FileName = "anim_calibration.json";

    static readonly Dictionary<long, float> _factors = new Dictionary<long, float>();
    static bool _loaded;

    public static float GetFactor(int animId, int slot)
    {
        EnsureLoaded();
        if (animId <= 0)
            return 1f;

        return _factors.TryGetValue(PackKey(animId, slot), out float factor) ? factor : 1f;
    }

    public static void Reload()
    {
        _loaded = false;
        _factors.Clear();
        EnsureLoaded();
    }

    static void EnsureLoaded()
    {
        if (_loaded)
            return;

        _loaded = true;
        LoadFromDisk();
    }

    static void LoadFromDisk()
    {
        string path = Path.Combine(Application.streamingAssetsPath, FileName);
        if (!File.Exists(path))
            return;

        try
        {
            string json = File.ReadAllText(path);
            CalibrationFile file = JsonUtility.FromJson<CalibrationFile>(json);
            if (file?.entries == null)
                return;

            for (int i = 0; i < file.entries.Length; i++)
            {
                Entry entry = file.entries[i];
                if (entry.animId <= 0 || entry.factor <= 0f)
                    continue;

                _factors[PackKey(entry.animId, entry.slot)] = entry.factor;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[AnimCalibration] Failed to load '{path}': {ex.Message}");
        }
    }

    static long PackKey(int animId, int slot) => ((long)animId << 32) | (uint)slot;

    [Serializable]
    sealed class CalibrationFile
    {
        public Entry[] entries;
    }

    [Serializable]
    sealed class Entry
    {
        public int animId;
        public int slot;
        public float factor = 1f;
    }
}
