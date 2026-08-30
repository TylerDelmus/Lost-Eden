using AODB.Common.RDBObjects;
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Resolves abiff mesh file names to RDB ids via InfoObject. Local to AoTweak for removability.
/// </summary>
public sealed class AoTweakMeshNames
{
    readonly Dictionary<string, int> _nameToId =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    public AoTweakMeshNames(ResourceDatabase database)
    {
        if (database?.Rdb == null)
            return;

        try
        {
            InfoObject info = database.Get<InfoObject>(1);
            if (info?.Types == null)
                return;

            if (!info.Types.TryGetValue(ResourceTypeId.RdbMesh, out Dictionary<int, string> names)
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

                // Also index without path / with lowercase
                string file = System.IO.Path.GetFileName(name);
                if (!string.IsNullOrEmpty(file) && !_nameToId.ContainsKey(file))
                    _nameToId[file] = pair.Key;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[AoTweak] Failed to load mesh name map: {ex.Message}");
        }
    }

    public bool TryResolve(string meshName, out int meshId)
    {
        meshId = 0;
        if (string.IsNullOrWhiteSpace(meshName))
            return false;

        string key = meshName.Trim();
        if (_nameToId.TryGetValue(key, out meshId))
            return true;

        string file = System.IO.Path.GetFileName(key);
        return !string.IsNullOrEmpty(file) && _nameToId.TryGetValue(file, out meshId);
    }
}
