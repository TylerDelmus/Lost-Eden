using System;
using System.Collections.Generic;
using AODB.Common.RDBObjects;
using UnityEngine;

/// <summary>
/// Resolves effect atlas filenames to RDB 1010004 texture ids via InfoObject.
/// </summary>
public sealed class EffectTextureNames
{
    readonly ResourceDatabase _database;
    readonly Dictionary<string, int> _nameToId =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    bool _loadAttempted;

    public EffectTextureNames(ResourceDatabase database)
    {
        _database = database;
    }

    public bool TryResolve(string fileName, out int textureId)
    {
        textureId = 0;
        EnsureLoaded();
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        string key = fileName.Trim();
        if (_nameToId.TryGetValue(key, out textureId))
            return true;

        string file = System.IO.Path.GetFileName(key);
        return !string.IsNullOrEmpty(file) && _nameToId.TryGetValue(file, out textureId);
    }

    void EnsureLoaded()
    {
        if (_loadAttempted && _nameToId.Count > 0)
            return;
        if (_database?.Rdb == null)
            return;

        _loadAttempted = true;
        try
        {
            InfoObject info = _database.Get<InfoObject>(1);
            if (info?.Types == null)
                return;

            if (!info.Types.TryGetValue(ResourceTypeId.Texture, out Dictionary<int, string> names)
                || names == null)
                return;

            foreach (KeyValuePair<int, string> pair in names)
            {
                if (string.IsNullOrEmpty(pair.Value))
                    continue;

                string name = pair.Value.Trim('\0').Trim();
                if (name.Length == 0)
                    continue;

                if (!_nameToId.ContainsKey(name))
                    _nameToId[name] = pair.Key;

                string file = System.IO.Path.GetFileName(name);
                if (!string.IsNullOrEmpty(file) && !_nameToId.ContainsKey(file))
                    _nameToId[file] = pair.Key;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"EffectTextureNames: failed to load Texture names ({ex.Message}).");
        }
    }
}
