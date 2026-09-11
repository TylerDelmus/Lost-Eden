using AODB.Common.Enums;
using AODB.Common.RDBObjects;

/// <summary>
/// An item materialized at a specific quality from a low/high RDB template pair.
/// </summary>
public sealed class Item
{
    public int LowId { get; }
    public int HighId { get; }
    public int Quality { get; }
    public ItemBase Template { get; }

    public Item(int lowId, int highId, int quality, ItemBase template)
    {
        LowId = lowId;
        HighId = highId;
        Quality = quality;
        Template = template;
    }

    public bool TryGetStat(int statId, out int value)
    {
        value = 0;
        if (Template?.Stats == null)
            return false;

        if (!Template.Stats.TryGetValue((StatId)statId, out uint raw))
            return false;

        value = (int)raw;
        return true;
    }
}
