using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Port of stock <c>ItemListViewBase_c : MultiListView_c : View</c> (GUI.dll, ctor 100423fc) —
/// the multi-list that shows a container's items, as a grid of icons or as a list. Its saved
/// settings (10041352) carry the layout mode and a list sort and a grid sort, each applied to
/// the base. Stock's only other recovered attribute here is <c>listview_flags</c>.
/// </summary>
[UxmlElement]
public partial class AoItemListViewBase : AoMultiListView
{
    public AoItemListViewBase() => AddToClassList("ao-item-list-view-base");

    /// <summary>Stock <c>listview_flags</c>, kept raw until the bits are recovered.</summary>
    [UxmlAttribute("listview_flags")]
    public int ListViewFlags { get; set; }
}

/// <summary>
/// Stock <c>ItemContainerView_c</c>'s column flags: which optional columns its list gets. The
/// Icon column is always there.
/// </summary>
[Flags]
public enum AoItemColumns
{
    None = 0,
    Count = 1 << 0,
    Name = 1 << 1,
    Price = 1 << 2,
    Quality = 1 << 3,
}

/// <summary>
/// Port of stock <c>ItemContainerView_c : View</c> (GUI.dll, ctor 100ce55d) — the view for one
/// container of items (the inventory, a backpack, the bank…). It holds an
/// <see cref="AoItemListViewBase"/> and gives it its columns:
///
/// <code>
///   id  label    width  when
///   0   Icon      16    always
///   1   Name     200    AoItemColumns.Name
///   2   Count     30    AoItemColumns.Count
///   3   Price    100    AoItemColumns.Price
///   4   Quality  100    AoItemColumns.Quality
/// </code>
///
/// then sorts the list by Name, ascending (<c>SetListSortColumn(1, …)</c>).
///
/// <para>
/// <b>Not ported:</b> the item placement stock restores from its saved config (<c>item_id</c>
/// and <c>item_pos</c> pairs), and whatever the Price column's alternate flags (0x1e rather than
/// 0xe, when the view's own flag 0x10 is set) change.
/// </para>
/// </summary>
[UxmlElement]
public partial class AoItemContainerView : AoView
{
    public const int IconColumn = 0;
    public const int NameColumn = 1;
    public const int CountColumn = 2;
    public const int PriceColumn = 3;
    public const int QualityColumn = 4;

    readonly AoItemListViewBase _list;
    AoItemColumns _columns = AoItemColumns.None;

    /// <summary>
    /// An item was picked up — stock's left press without a qualifier (<c>ItemListViewBase_c</c>
    /// 10041b41: button 1, <c>GetQualifiers() &amp; 3</c> clear), which fires the pick-up signal
    /// (+0x2d8). The owner starts the drag.
    /// </summary>
    public event Action<AoMultiListViewItem, PointerDownEvent> ItemPickedUp;

    /// <summary>
    /// The item's other press — stock's button 2 (10041bfa), which fires +0x2e8. Taken here as
    /// the use request (a backpack opens this way); what stock connects to +0x2e8 is not read.
    /// </summary>
    public event Action<AoMultiListViewItem> ItemUseRequested;

    /// <summary>An item drag was released over this container (stock's drop slot, 100cc855).</summary>
    public event Action<AoItemDrop> ItemDropped;

    public AoItemContainerView()
    {
        AddToClassList("ao-item-container-view");

        _list = new AoItemListViewBase { name = "itemlist" };
        _list.style.flexGrow = 1f;
        _list.ItemPressed += OnItemPressed;
        Add(_list);

        BuildColumns();
    }

    // Stock numbers the buttons from 1 (1 = left, 2 = right, as its mouse-down slot branches);
    // UI Toolkit numbers them from 0. Shift and Ctrl stand in for stock's two qualifier bits,
    // whose mapping is not read.
    void OnItemPressed(AoMultiListViewItem item, PointerDownEvent evt)
    {
        if (evt.button == 0)
        {
            if (evt.shiftKey || evt.ctrlKey)
                return;
            ItemPickedUp?.Invoke(item, evt);
        }
        else if (evt.button == 1)
        {
            ItemUseRequested?.Invoke(item);
        }
    }

    /// <summary>Called by the drag controller when an item drag is released over this view.</summary>
    public void ReceiveDrop(AoItemDrop drop) => ItemDropped?.Invoke(drop);

    /// <summary>The list showing this container's items.</summary>
    public AoItemListViewBase List => _list;

    /// <summary>Container identity as the server addresses it (inventory, bank, a backpack…).</summary>
    public int ContainerId { get; set; }

    /// <summary>Which optional columns the list has. Changing it rebuilds the header.</summary>
    public AoItemColumns ItemColumns
    {
        get => _columns;
        set
        {
            if (_columns == value)
                return;

            _columns = value;
            BuildColumns();
        }
    }

    void BuildColumns()
    {
        var columns = new System.Collections.Generic.List<AoMultiListView.AoColumn>
        {
            new(IconColumn, "Icon", 16f)
        };

        if ((_columns & AoItemColumns.Name) != 0) columns.Add(new(NameColumn, "Name", 200f));
        if ((_columns & AoItemColumns.Count) != 0) columns.Add(new(CountColumn, "Count", 30f));
        if ((_columns & AoItemColumns.Price) != 0) columns.Add(new(PriceColumn, "Price", 100f));
        if ((_columns & AoItemColumns.Quality) != 0) columns.Add(new(QualityColumn, "Quality", 100f));

        _list.SetColumns(columns);
        _list.SetListSortColumn(NameColumn, ascending: true);
    }
}

/// <summary>
/// Port of stock <c>InventoryViewBase_c : ItemContainerView_c</c> (GUI.dll, ctor 100ccde7) —
/// the container view the inventory window builds. It passes column flags 0xb: Name, Count and
/// Quality, and no Price.
/// </summary>
[UxmlElement]
public partial class AoInventoryViewBase : AoItemContainerView
{
    public AoInventoryViewBase()
    {
        AddToClassList("ao-inventory-view-base");
        ItemColumns = AoItemColumns.Name | AoItemColumns.Count | AoItemColumns.Quality;
    }
}

/// <summary>
/// Port of stock <c>InventoryListViewItem_c</c> (GUI.dll, ctor 1003e20a) — one item in an
/// <see cref="AoItemListViewBase"/>. In the grid it is the item's icon with its stack count;
/// in the list its Icon cell is the icon and the rest are text.
///
/// <para>
/// <b>Not read:</b> how stock draws the grid cell (<c>MultiListView_c::CreateItemView</c>,
/// 10133f5f) and the list cells. Here the grid cell is an <see cref="AoItemIconView"/>, which
/// shows the stack count when there is more than one.
/// </para>
/// </summary>
public class AoInventoryListViewItem : AoMultiListViewItem
{
    readonly string _name;
    readonly int _count;
    readonly int _quality;
    readonly string _price;

    public Texture2D Icon { get; }

    public AoInventoryListViewItem(Texture2D icon, string name, int count, int quality, string price = null)
    {
        AddToClassList("ao-inventory-list-view-item");
        Icon = icon;
        _name = name ?? string.Empty;
        _count = count;
        _quality = quality;
        _price = price ?? string.Empty;
    }

    public string Name => _name;
    public int Count => _count;
    public int Quality => _quality;

    protected override VisualElement CreateCell(int columnId, int columnIndex)
    {
        if (columnId == AoItemContainerView.IconColumn)
        {
            var icon = new VisualElement();
            icon.AddToClassList("ao-inventory-list-view-item__icon");
            if (Icon != null)
                icon.style.backgroundImage = new StyleBackground(Icon);
            return icon;
        }

        return new Label(SortKeyFor(columnId, columnIndex));
    }

    protected override string SortKeyFor(int columnId, int columnIndex) => columnId switch
    {
        AoItemContainerView.NameColumn => _name,
        AoItemContainerView.CountColumn => _count.ToString(),
        AoItemContainerView.PriceColumn => _price,
        AoItemContainerView.QualityColumn => _quality.ToString(),
        _ => string.Empty,
    };

    protected override VisualElement CreateGridView()
    {
        var view = new AoItemIconView { Image = Icon, StackCount = _count };
        return view;
    }
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
        AddToClassList("ao-item-slot-view");
        AddToClassList("ao-item-slot-view--empty");
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

    public void SetItem(int itemId, int iconId, int stackCount = 1, Texture2D image = null)
    {
        _icon.ItemId = itemId;
        _icon.IconId = iconId;
        _icon.StackCount = stackCount;
        _icon.Image = image;
        EnableInClassList("ao-item-slot-view--empty", itemId == 0);
    }

    public void ClearItem() => SetItem(0, 0, 0, null);

    /// <summary>Called by a drag controller when an item is released over this slot.</summary>
    public void NotifyDropped(int fromSlotIndex) => Dropped?.Invoke(fromSlotIndex);
}

/// <summary>
/// Port of stock <c>ItemIconView_c : View</c> (GUI.dll) — an item's icon, with its stack count.
/// Its attribute is <c>iconview_flags</c>. (<c>listview_mode</c> and the list and grid sorts,
/// whose names sit near this vftable too, are read by <c>ItemListViewBase_c</c> — 10041352 — and
/// live on <see cref="AoMultiListView"/>.)
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
        AddToClassList("ao-item-icon-view");
        _stackLabel = new Label { pickingMode = PickingMode.Ignore };
        _stackLabel.AddToClassList("ao-item-icon-view__stack");
        _stackLabel.style.display = DisplayStyle.None;
        Add(_stackLabel);
    }

    public int ItemId
    {
        get => _itemId;
        set => _itemId = value;
    }

    /// <summary>
    /// The icon art, drawn as this view's background. The view does not resolve <see cref="IconId"/>
    /// itself — it has no database — so whoever fills the slot supplies the texture.
    /// </summary>
    public Texture2D Image
    {
        get => _image;
        set
        {
            _image = value;
            style.backgroundImage = value != null ? new StyleBackground(value) : new StyleBackground(StyleKeyword.None);
        }
    }

    Texture2D _image;

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
}
