using AOSharp.Common.GameData;

/// <summary>
/// An item instance placed in a specific inventory/equipment slot.
/// </summary>
public sealed class InventoryItem
{
    public Identity Slot { get; }
    public Identity UniqueIdentity { get; }
    public short Flags { get; }
    public short Count { get; }
    public Item Item { get; }

    public InventoryItem(Identity slot, Identity uniqueIdentity, short flags, short count, Item item)
    {
        Slot = slot;
        UniqueIdentity = uniqueIdentity;
        Flags = flags;
        Count = count;
        Item = item;
    }
}
