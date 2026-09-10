using System.Collections.Generic;
using AOSharp.Common.GameData;

public sealed class ItemContainer
{
    readonly List<InventoryItem> _items = new List<InventoryItem>();

    public Identity Identity { get; }
    public int Offset { get; }
    public int Capacity { get; }
    public ContainerFlags Flags { get; set; }
    public bool IsOpen { get; set; } = true;

    public IReadOnlyList<InventoryItem> Items => _items;

    public ItemContainer(Identity identity, int offset, int capacity)
    {
        Identity = identity;
        Offset = offset;
        Capacity = capacity;
    }

    public bool ContainsPlacement(int placement)
        => placement >= Offset && placement < Offset + Capacity;

    internal void Clear() => _items.Clear();

    internal void Add(InventoryItem item)
    {
        if (item != null)
            _items.Add(item);
    }
}
