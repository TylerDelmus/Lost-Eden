using System;
using System.Collections.Generic;
using AODB.Common.RDBObjects;
using AOSharp.Common.GameData;
using UnityEngine;

/// <summary>
/// Stock AnimResolve: PC breed/sex filename, creature MonsterData kind→anim Loop A.
/// </summary>
public sealed class AnimKindResolver
{
    readonly ResourceDatabase _database;
    Dictionary<string, int> _nameToId;
    static readonly Dictionary<(int monsterDataId, int kindId, int breed, int sex), int> Cache =
        new Dictionary<(int, int, int, int), int>();
    static readonly object CacheGate = new object();

    public AnimKindResolver(ResourceDatabase database)
    {
        _database = database;
    }

    public bool TryResolve(
        int monsterDataId,
        int breed,
        int sex,
        int kindId,
        out int animId)
    {
        animId = 0;
        if (kindId <= 0 || _database?.Rdb == null)
            return false;

        var cacheKey = (monsterDataId, kindId, breed, sex);
        lock (CacheGate)
        {
            if (Cache.TryGetValue(cacheKey, out int cached))
            {
                animId = cached;
                return cached > 0;
            }
        }

        bool ok = monsterDataId != 0
            ? TryResolveCreature(monsterDataId, kindId, out animId)
            : TryResolvePlayer(breed, sex, kindId, out animId);

        if (!ok)
            animId = TryParentChain(monsterDataId, breed, sex, kindId);

        lock (CacheGate)
            Cache[cacheKey] = animId;

        return animId > 0;
    }

    public bool TryResolveByKindName(int breed, int sex, string kindName, out int animId)
    {
        animId = 0;
        if (string.IsNullOrEmpty(kindName))
            return false;

        string prefix = BreedSexPrefix(breed, sex);
        return TryLookupFilename(prefix, kindName, social: false, out animId);
    }

    int TryParentChain(int monsterDataId, int breed, int sex, int kindId)
    {
        int fallback = kindId switch
        {
            AnimKindIds.Walk2h => AnimKindIds.Walk,
            AnimKindIds.Run2h => AnimKindIds.Run,
            _ => 0,
        };

        if (fallback == 0)
            return 0;

        return TryResolve(monsterDataId, breed, sex, fallback, out int animId) ? animId : 0;
    }

    bool TryResolveCreature(int monsterDataId, int kindId, out int animId)
    {
        animId = 0;
        if (!MonsterDataResolver.TryGetAnimIdsByKind(_database, monsterDataId, kindId, out List<int> ids)
            || ids == null
            || ids.Count == 0)
            return false;

        animId = ids[0];
        return animId > 0;
    }

    bool TryResolvePlayer(int breed, int sex, int kindId, out int animId)
    {
        animId = 0;
        if (!AnimKindCatalog.TryGetName(kindId, out string kindName))
            return false;

        string prefix = BreedSexPrefix(breed, sex);
        bool social = kindId < AnimKindIds.SocialKindMax;
        return TryLookupFilename(prefix, kindName, social, out animId);
    }

    bool TryLookupFilename(string prefix, string kindName, bool social, out int animId)
    {
        animId = 0;
        EnsureNameIndex();
        if (_nameToId == null || _nameToId.Count == 0)
            return false;

        if (social)
        {
            string socialStem = $"{prefix}_social-{kindName}";
            return _nameToId.TryGetValue(socialStem, out animId) && animId > 0;
        }

        string stem = $"{prefix}_{kindName}_01_01";
        if (_nameToId.TryGetValue(stem, out animId) && animId > 0)
            return true;

        string prefixToken = $"{prefix}_{kindName}";
        foreach (KeyValuePair<string, int> pair in _nameToId)
        {
            if (pair.Value <= 0 || !pair.Key.StartsWith(prefixToken, StringComparison.Ordinal))
                continue;

            animId = pair.Value;
            return true;
        }

        return false;
    }

    public static string BreedSexPrefix(int breed, int sex)
    {
        if (breed == (int)Breed.Atrox)
            return "athrox";

        if (sex == (int)Gender.Female)
            return "female";

        return "male";
    }

    void EnsureNameIndex()
    {
        if (_nameToId != null)
            return;

        _nameToId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (_database?.Rdb == null)
            return;

        try
        {
            InfoObject info = _database.Get<InfoObject>(1);
            if (info?.Types == null)
                return;

            if (!info.Types.TryGetValue(ResourceTypeId.Anim, out Dictionary<int, string> names) || names == null)
                return;

            foreach (KeyValuePair<int, string> pair in names)
            {
                if (string.IsNullOrEmpty(pair.Value) || pair.Key <= 0)
                    continue;

                string key = NormalizeName(pair.Value);
                if (string.IsNullOrEmpty(key) || _nameToId.ContainsKey(key))
                    continue;

                _nameToId[key] = pair.Key;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"AnimKindResolver: Failed to load anim names ({ex.Message}).");
        }
    }

    static string NormalizeName(string name)
    {
        string trimmed = name.Trim().Trim('\0').ToLowerInvariant();
        if (trimmed.EndsWith(".ani", StringComparison.Ordinal))
            trimmed = trimmed.Substring(0, trimmed.Length - 4);
        return trimmed.Trim();
    }
}
