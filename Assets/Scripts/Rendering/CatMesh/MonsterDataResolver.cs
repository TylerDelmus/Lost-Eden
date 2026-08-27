using System.Collections.Generic;
using AODB.Common.RDBObjects;

public static class MonsterDataResolver
{
    public const int BodyCatMeshStatId = 12;

    public static bool TryResolveBodyCatMeshId(ResourceDatabase db, int monsterDataId, out int catMeshId)
    {
        catMeshId = 0;
        if (monsterDataId <= 0 || db?.Rdb == null)
            return false;

        MonsterData monsterData = db.Get<MonsterData>(ResourceTypeId.MonsterData, monsterDataId);
        if (monsterData?.Stats == null)
            return false;

        if (!monsterData.Stats.TryGetValue(BodyCatMeshStatId, out uint bodyCatMeshId) || bodyCatMeshId == 0)
            return false;

        catMeshId = (int)bodyCatMeshId;
        return true;
    }

    public static bool TryGetAnimIds(ResourceDatabase db, int monsterDataId, int animSet, out List<int> animIds)
    {
        animIds = null;
        if (monsterDataId <= 0 || db?.Rdb == null)
            return false;

        MonsterData monsterData = db.Get<MonsterData>(ResourceTypeId.MonsterData, monsterDataId);
        if (monsterData?.Anims == null || monsterData.Anims.Count == 0)
            return false;

        if (monsterData.Anims.TryGetValue(animSet, out List<int> setIds) && setIds != null && setIds.Count > 0)
        {
            animIds = setIds;
            return true;
        }

        var union = new List<int>();
        var seen = new HashSet<int>();
        foreach (KeyValuePair<int, List<int>> pair in monsterData.Anims)
        {
            if (pair.Value == null)
                continue;

            for (int i = 0; i < pair.Value.Count; i++)
            {
                int id = pair.Value[i];
                if (id <= 0 || !seen.Add(id))
                    continue;

                union.Add(id);
            }
        }

        if (union.Count == 0)
            return false;

        animIds = union;
        return true;
    }

    public static bool TryGetAnimEntries(
        ResourceDatabase db,
        int monsterDataId,
        int? animSetFilter,
        out List<(int AnimSet, int AnimId)> entries)
    {
        entries = null;
        if (monsterDataId <= 0 || db?.Rdb == null)
            return false;

        MonsterData monsterData = db.Get<MonsterData>(ResourceTypeId.MonsterData, monsterDataId);
        if (monsterData?.Anims == null || monsterData.Anims.Count == 0)
            return false;

        var list = new List<(int, int)>();
        var seen = new HashSet<int>();

        foreach (KeyValuePair<int, List<int>> pair in monsterData.Anims)
        {
            if (animSetFilter.HasValue && pair.Key != animSetFilter.Value)
                continue;

            if (pair.Value == null)
                continue;

            for (int i = 0; i < pair.Value.Count; i++)
            {
                int id = pair.Value[i];
                if (id <= 0 || !seen.Add(id))
                    continue;

                list.Add((pair.Key, id));
            }
        }

        if (list.Count == 0)
            return false;

        entries = list;
        return true;
    }

    static Dictionary<int, int> _catMeshToMonsterDataCache;

    public static bool TryFindMonsterDataForCatMesh(ResourceDatabase db, int catMeshId, out int monsterDataId)
    {
        monsterDataId = 0;
        if (catMeshId <= 0 || db?.Rdb == null)
            return false;

        if (_catMeshToMonsterDataCache != null
            && _catMeshToMonsterDataCache.TryGetValue(catMeshId, out int cached)
            && cached > 0)
        {
            monsterDataId = cached;
            return true;
        }

        EnsureCatMeshToMonsterDataCache(db);
        if (_catMeshToMonsterDataCache != null
            && _catMeshToMonsterDataCache.TryGetValue(catMeshId, out int mapped)
            && mapped > 0)
        {
            monsterDataId = mapped;
            return true;
        }

        return false;
    }

    static void EnsureCatMeshToMonsterDataCache(ResourceDatabase db)
    {
        if (_catMeshToMonsterDataCache != null || db?.Rdb == null)
            return;

        _catMeshToMonsterDataCache = new Dictionary<int, int>();
        if (!db.Rdb.RecordTypeToId.TryGetValue((int)ResourceTypeId.MonsterData, out Dictionary<int, ulong> records)
            || records == null)
            return;

        foreach (int id in records.Keys)
        {
            if (!TryResolveBodyCatMeshId(db, id, out int bodyCatMeshId))
                continue;

            if (!_catMeshToMonsterDataCache.ContainsKey(bodyCatMeshId))
                _catMeshToMonsterDataCache[bodyCatMeshId] = id;
        }
    }
}
