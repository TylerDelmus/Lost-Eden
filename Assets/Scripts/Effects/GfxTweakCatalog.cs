using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Loaded gfxtweak.bin templates, keyed by effect id. Loads lazily once the AO path is ready.
/// </summary>
public sealed class GfxTweakCatalog
{
    readonly ResourceDatabase _database;
    Dictionary<int, GfxTweakRecord> _records;
    bool _loadAttempted;

    public int Count
    {
        get
        {
            EnsureLoaded();
            return _records != null ? _records.Count : 0;
        }
    }

    public GfxTweakCatalog(ResourceDatabase database)
    {
        _database = database;
        _records = new Dictionary<int, GfxTweakRecord>();
    }

    public bool TryGet(int effectId, out GfxTweakRecord record)
    {
        EnsureLoaded();
        return _records.TryGetValue(effectId, out record);
    }

    public void CopyIds(List<int> dest)
    {
        EnsureLoaded();
        dest?.Clear();
        if (dest == null || _records == null)
            return;

        foreach (int id in _records.Keys)
            dest.Add(id);
        dest.Sort();
    }

    void EnsureLoaded()
    {
        if (_loadAttempted && _records.Count > 0)
            return;
        if (_database?.Rdb == null)
            return;

        _loadAttempted = true;
        string aoBase = _database.Rdb.BaseAoPath;
        if (string.IsNullOrEmpty(aoBase) || !GfxTweakParser.TryLoad(aoBase, out Dictionary<int, GfxTweakRecord> records))
        {
            Debug.LogWarning("GfxTweakCatalog: failed to load Setupf/gfxtweak.bin.");
            return;
        }

        _records = records;
        Debug.Log($"GfxTweakCatalog: loaded {records.Count} templates from Setupf/gfxtweak.bin.");
    }
}
