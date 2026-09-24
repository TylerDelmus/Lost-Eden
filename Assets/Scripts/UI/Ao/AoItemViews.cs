using System;
using UnityEngine.UIElements;

/// <summary>
/// Port of stock <c>ItemListViewBase_c : MultiListView_c : View</c> (GUI.dll) — a multi-list
/// specialised for game items, which is the list half of the inventory window. Stock's only
/// recovered attribute here is <c>listview_flags</c>.
/// </summary>
[UxmlElement]
public partial class AoItemListViewBase : AoMultiListView
{
    /// <summary>Stock <c>listview_flags</c>, kept raw until the bits are recovered.</summary>
    [UxmlAttribute("listview_flags")]
    public int ListViewFlags { get; set; }
}

/// <summary>
/// Port of stock <c>ItemContainerView_c : View</c> (GUI.dll) — the backing view for any
/// container of items. <c>InventoryView_c</c> constructs two of these, and
/// <c>InventoryViewBase_c</c> derives from it.
///
/// Stock carries the container's identity and capacity; the trailing strings beside its
/// vftable belong to a neighbouring class and were not used.
/// </summary>
[UxmlElement]
public partial class AoItemContainerView : AoView
{
    public event Action<int> SlotClicked;
    public event Action<int, int> SlotDropped;

    int _capacity;

    /// <summary>Number of slots; rebuilding is the owner's job via <see cref="Rebuild"/>.</summary>
    public int Capacity => _capacity;

    /// <summary>Container identity as the server addresses it (inventory, bank, a backpack…).</summary>
    public int ContainerId { get; set; }

    public void Rebuild(int capacity)
    {
        _capacity = capacity;
        Clear();

        for (int i = 0; i < capacity; i++)
        {
            int slotIndex = i;
            var slot = new AoItemSlotView { SlotIndex = slotIndex };
            slot.Clicked += () => SlotClicked?.Invoke(slotIndex);
            slot.Dropped += from => SlotDropped?.Invoke(from, slotIndex);
            Add(slot);
        }
    }

    public AoItemSlotView Slot(int index) =>
        index >= 0 && index < childCount ? this[index] as AoItemSlotView : null;
}

/// <summary>
/// Port of stock <c>ItemSlotView_c : View</c> (GUI.dll) — one drop target holding at most one
/// item. Derives from View, not GUIControl_c. Stock's attribute is <c>iconview_flags</c>.
/// </summary>
[UxmlElement]
public partial class AoItemSlotView : AoView
{
    readonly AoItemIconView _icon;

    public event Action Clicked;
    public event Action<int> Dropped;

    public AoItemSlotView()
    {
        AddToClassList("itemslot");
        pickingMode = PickingMode.Position;
        _icon = new AoItemIconView { pickingMode = PickingMode.Ignore };
        Add(_icon);
        RegisterCallback<ClickEvent>(_ => Clicked?.Invoke());
    }

    public int SlotIndex { get; set; }

    /// <summary>Stock <c>iconview_flags</c>, kept raw until the bits are recovered.</summary>
    [UxmlAttribute("iconview_flags")]
    public int IconViewFlags { get; set; }

    public AoItemIconView Icon => _icon;

    public bool IsEmpty => _icon.ItemId == 0;

    public void SetItem(int itemId, int iconId, int stackCount = 1)
    {
        _icon.ItemId = itemId;
        _icon.IconId = iconId;
        _icon.StackCount = stackCount;
    }

    public void ClearItem() => SetItem(0, 0, 0);

    /// <summary>Called by a drag controller when an item is released over this slot.</summary>
    public void NotifyDropped(int fromSlotIndex) => Dropped?.Invoke(fromSlotIndex);
}

/// <summary>
/// Port of stock <c>ItemIconView_c : View</c> (GUI.dll) — the icon itself, plus the state that
/// drives how a whole container is presented. The attributes beside its vftable are
/// <c>iconview_flags</c>, <c>listview_mode</c>, <c>list_sort_order</c>, <c>list_sort_column</c>,
/// <c>grid_sort_order</c> and <c>grid_sort_column</c>: stock keeps a *separate* sort for grid
/// mode and list mode, which is why the inventory remembers both independently.
/// </summary>
[UxmlElement]
public partial class AoItemIconView : AoView
{
    int _itemId;
    int _iconId;
    int _stackCount;

    readonly Label _stackLabel;

    public event Action<int> IconChanged;

    public AoItemIconView()
    {
        AddToClassList("itemicon");
        _stackLabel = new Label { pickingMode = PickingMode.Ignore };
        _stackLabel.AddToClassList("itemicon__stack");
        _stackLabel.style.display = DisplayStyle.None;
        Add(_stackLabel);
    }

    public int ItemId
    {
        get => _itemId;
        set => _itemId = value;
    }

    /// <summary>Numeric GUI art id; resolution is the visual pass's job.</summary>
    public int IconId
    {
        get => _iconId;
        set
        {
            if (_iconId == value)
                return;

            _iconId = value;
            IconChanged?.Invoke(value);
        }
    }

    public int StackCount
    {
        get => _stackCount;
        set
        {
            _stackCount = value;
            if (_stackLabel == null)
                return;

            _stackLabel.text = value > 1 ? value.ToString() : string.Empty;
            _stackLabel.style.display = value > 1 ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    /// <summary>Stock <c>iconview_flags</c>, kept raw until the bits are recovered.</summary>
    [UxmlAttribute("iconview_flags")]
    public int IconViewFlags { get; set; }

    /// <summary>Stock <c>listview_mode</c>: grid of icons, or a multi-column list.</summary>
    [UxmlAttribute("listview_mode")]
    public AoListViewMode ListViewMode { get; set; } = AoListViewMode.Grid;

    [UxmlAttribute("list_sort_column")] public int ListSortColumn { get; set; }
    [UxmlAttribute("list_sort_order")] public AoSortOrder ListSortOrder { get; set; }
    [UxmlAttribute("grid_sort_column")] public int GridSortColumn { get; set; }
    [UxmlAttribute("grid_sort_order")] public AoSortOrder GridSortOrder { get; set; }
}

public enum AoListViewMode
{
    Grid,
    List
}

public enum AoSortOrder
{
    Ascending,
    Descending
}
