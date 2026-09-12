using System;
using System.Collections.Generic;
using AODB.Common.RDBObjects;

/// <summary>
/// Loads and caches item templates (interpolated by QL) and nano templates (by id).
/// </summary>
public sealed class ItemTemplateCache
{
    readonly ResourceDatabase _database;
    readonly Dictionary<int, ItemObject> _rawItems = new Dictionary<int, ItemObject>();
    readonly Dictionary<(int LowId, int HighId, int Quality), Item> _items =
        new Dictionary<(int, int, int), Item>();
    readonly Dictionary<int, NanoSpell> _nanos = new Dictionary<int, NanoSpell>();

    public ItemTemplateCache(ResourceDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public Item GetItem(int lowId, int highId, int quality)
    {
        var key = (lowId, highId, quality);
        if (_items.TryGetValue(key, out Item cached))
            return cached;

        ItemObject low = GetRawItem(lowId);
        ItemObject high = lowId == highId ? low : GetRawItem(highId);
        ItemObject interpolated = ItemTemplateInterpolator.Interpolate(low, high, quality);

        var item = new Item(lowId, highId, quality, interpolated);
        _items[key] = item;
        return item;
    }

    public NanoSpell GetNano(int id)
    {
        return TryGetNano(id, out NanoSpell nano) ? nano : null;
    }

    public bool TryGetNano(int id, out NanoSpell nano)
    {
        if (_nanos.TryGetValue(id, out nano))
            return nano != null;

        nano = null;
        if (id <= 0 || _database?.Rdb == null)
            return false;

        try
        {
            NanoObject template = _database.Get<NanoObject>(id);
            if (template == null)
                return false;

            nano = new NanoSpell(id, template);
            _nanos[id] = nano;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    ItemObject GetRawItem(int id)
    {
        if (_rawItems.TryGetValue(id, out ItemObject cached))
            return cached;

        ItemObject template = _database.Get<ItemObject>(id);
        _rawItems[id] = template;
        return template;
    }
}
