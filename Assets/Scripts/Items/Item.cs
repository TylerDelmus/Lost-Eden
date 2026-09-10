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
}
