using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Stock <c>MultiListView_c::LayoutMode_e</c>. <c>SetLayoutMode</c> (10137761) lays out a grid for
/// 0 and a list for anything else; the saved settings carry it as the bool <c>listview_mode</c>.
/// </summary>
public enum AoLayoutMode
{
    Grid = 0,
    List = 1
}

/// <summary>
/// Stock <c>MultiListView_c::IconSize_e</c>, named by the icon size <c>SetGridIconSize</c>
/// (101357f5) gives each value. The stock names are not recovered.
/// </summary>
public enum AoIconSize
{
    Icon31 = 0,
    Icon47 = 1,
    Icon39x23 = 2,
    Icon19x11 = 3
}

/// <summary>
/// Port of stock <c>MultiListView_c : View</c> (GUI.dll, vftable 101c68fc, 60 virtuals) — the
/// view behind inventory, missions, perks, the NCU and the friends list.
///
/// It never appears in the shipped XML: every instance is built in code, which is why its
/// surface comes from the binary rather than from a tag. The attribute recovered beside its
/// vftable is <c>column_id</c> (shared with <see cref="AoColumnHeaderView"/>), along with the
/// style path <c>multilistview/columnheader</c>.
///
/// <para>
/// It has two layouts (<see cref="AoLayoutMode"/>), and a grid is the default (the constructor
/// at 101364ff zeroes the mode):
/// </para>
/// <list type="bullet">
///   <item><b>Grid</b>: items as icons. Each cell is the icon size plus one pixel, cells are
///     spaced by the icon spacing, and the grid is inset by the border (<c>GridPosToViewPos</c>,
///     101330b7: <c>pos = gridPos * (iconSize + spacing + 1) + border</c>). Defaults: 47px icons
///     (<see cref="AoIconSize.Icon47"/>), 10px spacing, a 6px border. There is no header.</item>
///   <item><b>List</b>: one row per item under a column header (<c>UpdateHeaderView</c>,
///     1013758a, only builds the header in list mode).</item>
/// </list>
///
/// <para>
/// The two modes keep separate sorts (<c>SetListSortColumn</c> 1013734d,
/// <c>SetGridSortColumn</c> 101373b2); both start unsorted. Clicking a header sorts the list.
/// </para>
///
/// <para>
/// <b>Not ported:</b> stock's grid is positional — an item sits at a grid cell, can be moved to
/// another and leave gaps (<c>AddItem(IPoint, …)</c>, <c>MoveItem</c>, <c>GetFirstFreePos</c>),
/// unless auto-arrange (feature bit 2) packs them. Here the grid is always packed, in item order,
/// which is what UI Toolkit's wrapping gives.
/// </para>
/// </summary>
[UxmlElement]
public partial class AoMultiListView : AoView
{
    const float DefaultIconSpacing = 10f;   // 101364ff: +0x188 and +0x190 = Point(10, 10)
    const float DefaultBorder = 6f;         // 101364ff: +0x170 = Rect(6, 6, 6, 6)

    readonly VisualElement _header;
    readonly AoScrollView _body;
    readonly List<AoColumn> _columns = new();
    readonly List<AoMultiListViewItem> _items = new();
    readonly List<VisualElement> _emptySlots = new();

    AoLayoutMode _layoutMode = AoLayoutMode.Grid;
    AoIconSize _gridIconSize = AoIconSize.Icon47;

    int _listSortColumn = -1;
    bool _listSortAscending = true;
    int _gridSortColumn = -1;
    bool _gridSortAscending = true;
    int _selectedIndex = -1;

    public event Action<int> SelectionChanged;
    public event Action<int, bool> SortChanged;
    public event Action<int> ItemActivated;

    /// <summary>The item under the pointer, or null when it leaves one.</summary>
    public event Action<AoMultiListViewItem> ItemHovered;

    /// <summary>
    /// A pointer went down on an item — stock's mouse-down signal (+0x130, 10133c3f), which
    /// <c>ItemListViewBase_c</c> turns into pick-up and use.
    /// </summary>
    public event Action<AoMultiListViewItem, PointerDownEvent> ItemPressed;

    public IReadOnlyList<AoMultiListViewItem> Items => _items;
    public IReadOnlyList<AoColumn> Columns => _columns;
    public int ColumnCount => _columns.Count;

    public AoMultiListView()
    {
        AddToClassList("ao-multi-list-view");

        // Stock styles the header through the path multilistview/columnheader.
        _header = new VisualElement { name = "columnheader" };
        _header.style.flexDirection = FlexDirection.Row;
        _header.AddToClassList("ao-multi-list-view__header");
        hierarchy.Add(_header);

        _body = new AoScrollView { name = "listbody" };
        _body.style.flexGrow = 1f;
        _body.AddToClassList("ao-multi-list-view__body");
        hierarchy.Add(_body);

        // The grid is laid out to the scroll view's size, so it follows it. Not the viewport's:
        // that one takes its height from the content, which the grid sizes from it in turn, and
        // the two would lock at whatever the first layout pass happened to give.
        _body.Scroller.RegisterCallback<GeometryChangedEvent>(_ => LayoutGrid());

        ApplyLayoutMode();
    }

    /// <summary>Rows go into the scrolling body, not beside the header.</summary>
    public override VisualElement contentContainer => _body?.contentContainer ?? this;

    // ---- layout mode --------------------------------------------------------------------

    /// <summary>Stock <c>listview_mode</c>: a grid of icons, or a list under a header.</summary>
    [UxmlAttribute("listview_mode")]
    public AoLayoutMode LayoutMode
    {
        get => _layoutMode;
        set
        {
            if (_layoutMode == value)
                return;

            _layoutMode = value;
            ApplyLayoutMode();
        }
    }

    /// <summary>The grid's icon size. The cell is one pixel larger each way.</summary>
    public AoIconSize GridIconSize
    {
        get => _gridIconSize;
        set
        {
            _gridIconSize = value;
            ApplyLayoutMode();
        }
    }

    /// <summary>Stock's icon size in pixels for each <see cref="AoIconSize"/> (101357f5).</summary>
    public static Vector2 IconSizeOf(AoIconSize size) => size switch
    {
        AoIconSize.Icon31 => new Vector2(31f, 31f),
        AoIconSize.Icon39x23 => new Vector2(39f, 23f),
        AoIconSize.Icon19x11 => new Vector2(19f, 11f),
        _ => new Vector2(47f, 47f),
    };

    /// <summary>The grid cell: the icon plus one pixel each way.</summary>
    public Vector2 GridCellSize => IconSizeOf(_gridIconSize) + Vector2.one;

    /// <summary>
    /// The distance from one grid cell to the next: the cell plus the icon spacing. Each grid
    /// item (and each empty slot) is a box of this size with its cell centred in it, so a skin
    /// can frame the slot in the spacing without touching the icon's pixels.
    /// </summary>
    public Vector2 GridPitch => GridCellSize + new Vector2(DefaultIconSpacing, DefaultIconSpacing);

    void ApplyLayoutMode()
    {
        bool grid = _layoutMode == AoLayoutMode.Grid;
        EnableInClassList("ao-multi-list-view--grid", grid);
        EnableInClassList("ao-multi-list-view--list", !grid);

        _header.style.display = grid ? DisplayStyle.None : DisplayStyle.Flex;

        // Both scroll down, with the vertical bar shown when there is something to scroll. In
        // the grid the skin lays that bar over the grid's right margin, so it takes no width.
        _body.VerticalScrollbarMode = AoScrollbarMode.Auto;
        _body.HorizontalScrollbarMode = AoScrollbarMode.Off;

        VisualElement content = _body.contentContainer;
        content.style.flexDirection = FlexDirection.Column;
        content.style.flexWrap = Wrap.NoWrap;
        SetPadding(content, null);
        if (!grid)
        {
            content.style.width = StyleKeyword.Null;
            content.style.height = StyleKeyword.Null;
        }

        foreach (AoMultiListViewItem item in _items)
            item.ApplyLayout(_layoutMode, GridCellSize, GridPitch);

        RefreshSort();
    }

    // ---- the grid -------------------------------------------------------------------------

    /// <summary>
    /// The inset of the first grid box: stock puts cell n at <c>border + n * pitch</c>
    /// (GridPosToViewPos, 101330b7), and a box starts half a spacing before its cell.
    /// </summary>
    static float GridInset => DefaultBorder - DefaultIconSpacing * 0.5f;

    /// <summary>
    /// The framed slot inside a grid box, filled or empty (class <c>ao-multi-list-view__slot</c>).
    /// It fills the box and centres what it holds; the skin frames it and may inset it with a
    /// margin, which never moves the cell because the box around it is a fixed pitch.
    /// </summary>
    internal static VisualElement CreateSlot()
    {
        var slot = new AoSlotFrame { pickingMode = PickingMode.Ignore };
        slot.AddToClassList("ao-multi-list-view__slot");
        slot.style.flexGrow = 1f;
        slot.style.alignItems = Align.Center;
        slot.style.justifyContent = Justify.Center;
        return slot;
    }

    /// <summary>
    /// The grid's cell counts that fit a viewport of <paramref name="size"/>, at least one each.
    /// </summary>
    public Vector2Int GridCellsFitting(Vector2 size)
    {
        Vector2 pitch = GridPitch;
        return new Vector2Int(
            Mathf.Max(1, Mathf.FloorToInt((size.x - 2f * GridInset) / pitch.x)),
            Mathf.Max(1, Mathf.FloorToInt((size.y - 2f * GridInset) / pitch.y)));
    }

    /// <summary>
    /// The size of the whole view (the grid plus the view's own frame) that shows exactly
    /// <paramref name="cells"/> — stock's CellCountToPreferredSize (101332b8, not read). A window
    /// resized over a grid snaps to this.
    /// </summary>
    public Vector2 GridViewSizeFor(Vector2Int cells)
    {
        Vector2 pitch = GridPitch;
        return new Vector2(
            cells.x * pitch.x + 2f * GridInset + resolvedStyle.borderLeftWidth + resolvedStyle.borderRightWidth,
            cells.y * pitch.y + 2f * GridInset + resolvedStyle.borderTopWidth + resolvedStyle.borderBottomWidth);
    }

    /// <summary>
    /// A view size snapped to the nearest whole number of grid cells (at least one each way), in
    /// grid mode; in list mode the size as asked. How stock snaps a resize is not read; the live
    /// client's inventory always fits whole cells.
    /// </summary>
    public Vector2 SnapViewSize(Vector2 viewSize)
    {
        if (_layoutMode != AoLayoutMode.Grid)
            return viewSize;

        Vector2 frame = GridViewSizeFor(Vector2Int.zero);
        Vector2 pitch = GridPitch;
        return GridViewSizeFor(new Vector2Int(
            Mathf.Max(1, Mathf.RoundToInt((viewSize.x - frame.x) / pitch.x)),
            Mathf.Max(1, Mathf.RoundToInt((viewSize.y - frame.y) / pitch.y))));
    }

    /// <summary>
    /// Stock's LayoutGrid (10135165, not read instruction by instruction), as the grid behaves
    /// here: the columns are as many as the view's width holds, the rows as many as its height
    /// holds or as the items need, whichever is more, and the items go row by row. Past the
    /// view's height it scrolls down. Every cell no item occupies is still drawn as a slot.
    /// (Stock draws those itself — UpdateBackgroundSurfaces, 101340ff.)
    ///
    /// <para>
    /// <b>Not ported:</b> stock's positions are the items' own, restored from its config. Here
    /// they are recomputed row by row, so an item can move when the column count changes.
    /// </para>
    /// </summary>
    void LayoutGrid()
    {
        VisualElement content = _body.contentContainer;
        foreach (VisualElement slot in _emptySlots)
            slot.RemoveFromHierarchy();

        if (_layoutMode != AoLayoutMode.Grid)
            return;

        // The scroll view's own size: the vertical bar lies over the grid, so it takes none of it.
        Rect area = _body.Scroller.layout;
        if (float.IsNaN(area.width) || area.width <= 0f || float.IsNaN(area.height))
            return;

        Vector2 pitch = GridPitch;
        Vector2Int visible = GridCellsFitting(area.size);
        int columns = visible.x;
        int rows = Mathf.Max(visible.y, (_items.Count + columns - 1) / columns);
        int total = columns * rows;

        content.style.width = columns * pitch.x + 2f * GridInset;
        content.style.height = rows * pitch.y + 2f * GridInset;

        for (int i = 0; i < _items.Count; i++)
            Place(_items[i], i);

        int needed = total - _items.Count;
        while (_emptySlots.Count < needed)
        {
            var box = new VisualElement { pickingMode = PickingMode.Ignore };
            box.AddToClassList("ao-multi-list-view__empty-slot");
            box.Add(CreateSlot());
            _emptySlots.Add(box);
        }

        for (int i = 0; i < needed; i++)
        {
            VisualElement box = _emptySlots[i];
            box.style.width = pitch.x;
            box.style.height = pitch.y;
            content.Add(box);
            Place(box, _items.Count + i);
        }

        void Place(VisualElement e, int index)
        {
            e.style.position = Position.Absolute;
            e.style.left = GridInset + index % columns * pitch.x;
            e.style.top = GridInset + index / columns * pitch.y;
        }
    }

    static void SetPadding(VisualElement e, float? value)
    {
        StyleLength v = value.HasValue ? new StyleLength(value.Value) : new StyleLength(StyleKeyword.Null);
        e.style.paddingLeft = v;
        e.style.paddingTop = v;
        e.style.paddingRight = v;
        e.style.paddingBottom = v;
    }

    // ---- columns ------------------------------------------------------------------------

    public readonly struct AoColumn
    {
        public readonly int ColumnId;
        public readonly string Title;
        public readonly float Width;

        public AoColumn(int columnId, string title, float width)
        {
            ColumnId = columnId;
            Title = title;
            Width = width;
        }
    }

    /// <summary>Rebuilds the header. Existing rows are left alone; call <see cref="Clear"/> first if reusing.</summary>
    public void SetColumns(IEnumerable<AoColumn> columns)
    {
        _columns.Clear();
        _header.Clear();

        foreach (AoColumn column in columns)
        {
            _columns.Add(column);
            int index = _columns.Count - 1;

            var head = new AoColumnHeaderView
            {
                ColumnId = column.ColumnId,
                Title = column.Title
            };
            head.style.width = column.Width;
            head.Clicked += () => SortBy(index);
            _header.Add(head);
        }
    }

    int IndexOfColumn(int columnId) => _columns.FindIndex(c => c.ColumnId == columnId);

    // ---- items --------------------------------------------------------------------------

    /// <summary>A plain row of text cells, one per column in order.</summary>
    public AoMultiListViewItem AddItem(params string[] cells)
    {
        var item = new AoMultiListViewItem();
        item.SetTextCells(cells);
        return AddItem(item);
    }

    public AoMultiListViewItem AddItem(AoMultiListViewItem item)
    {
        item.BuildCells(_columns);
        item.ApplyLayout(_layoutMode, GridCellSize, GridPitch);

        // Resolve the item's position at click time, not at add time: sorting reorders _items,
        // so a captured index would select a different item after the first sort.
        item.Clicked += () => SelectedIndex = _items.IndexOf(item);
        item.DoubleClicked += () => ItemActivated?.Invoke(_items.IndexOf(item));
        item.RegisterCallback<PointerEnterEvent>(_ => ItemHovered?.Invoke(item));
        item.RegisterCallback<PointerLeaveEvent>(_ => ItemHovered?.Invoke(null));
        item.RegisterCallback<PointerDownEvent>(evt => ItemPressed?.Invoke(item, evt));

        _items.Add(item);
        Add(item);
        ApplySort();
        LayoutGrid();
        return item;
    }

    public void Clear()
    {
        _items.Clear();
        _body.contentContainer.Clear();
        _selectedIndex = -1;
        LayoutGrid();
    }

    /// <summary>The item under a panel point (stock <c>HitTest</c>, 1013380c), or null.</summary>
    public AoMultiListViewItem ItemAt(Vector2 panelPoint)
    {
        foreach (AoMultiListViewItem item in _items)
            if (item.worldBound.Contains(panelPoint))
                return item;
        return null;
    }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (_selectedIndex == value)
                return;

            if (_selectedIndex >= 0 && _selectedIndex < _items.Count)
                _items[_selectedIndex].SetSelected(false);

            _selectedIndex = value;

            if (_selectedIndex >= 0 && _selectedIndex < _items.Count)
                _items[_selectedIndex].SetSelected(true);

            SelectionChanged?.Invoke(value);
        }
    }

    // ---- sorting ------------------------------------------------------------------------

    /// <summary>Stock <c>SetListSortColumn</c>: by column id, applied while in list mode.</summary>
    public void SetListSortColumn(int columnId, bool ascending)
    {
        _listSortColumn = IndexOfColumn(columnId);
        _listSortAscending = ascending;
        RefreshSort();
    }

    /// <summary>Stock <c>SetGridSortColumn</c>: by column id, applied while in grid mode.</summary>
    public void SetGridSortColumn(int columnId, bool ascending)
    {
        _gridSortColumn = IndexOfColumn(columnId);
        _gridSortAscending = ascending;
        RefreshSort();
    }

    /// <summary>The sort the current mode uses (stock <c>GetActiveSortColumn</c>, 1013327d).</summary>
    public int ActiveSortColumn => _layoutMode == AoLayoutMode.Grid ? _gridSortColumn : _listSortColumn;

    public bool ActiveSortAscending => _layoutMode == AoLayoutMode.Grid ? _gridSortAscending : _listSortAscending;

    /// <summary>A header click: the active column flips direction, another column sorts ascending.</summary>
    public void SortBy(int columnIndex)
    {
        if (columnIndex < 0 || columnIndex >= _columns.Count)
            return;

        if (_listSortColumn == columnIndex)
            _listSortAscending = !_listSortAscending;
        else
        {
            _listSortColumn = columnIndex;
            _listSortAscending = true;
        }

        RefreshSort();
    }

    void RefreshSort()
    {
        ApplySort();
        LayoutGrid();
        for (int i = 0; i < _header.childCount; i++)
            if (_header[i] is AoColumnHeaderView head)
                head.SetSorted(i == _listSortColumn, _listSortAscending);
        SortChanged?.Invoke(ActiveSortColumn, ActiveSortAscending);
    }

    void ApplySort()
    {
        int column = ActiveSortColumn;
        bool ascending = ActiveSortAscending;
        if (column < 0)
            return;

        // Selection follows the item, not the place it happened to be in.
        AoMultiListViewItem selected =
            _selectedIndex >= 0 && _selectedIndex < _items.Count ? _items[_selectedIndex] : null;

        _items.Sort((a, b) =>
        {
            int cmp = Compare(a.SortKey(column), b.SortKey(column));
            return ascending ? cmp : -cmp;
        });

        VisualElement content = _body.contentContainer;
        content.Clear();
        foreach (AoMultiListViewItem item in _items)
            content.Add(item);

        _selectedIndex = selected != null ? _items.IndexOf(selected) : -1;
    }

    // Stock compares through the virtual SlotCompareItems (101334c8), which is not read. Numbers
    // compare as numbers here so a count or a QL does not sort 10 before 9.
    static int Compare(string a, string b)
    {
        if (long.TryParse(a, out long x) && long.TryParse(b, out long y))
            return x.CompareTo(y);
        return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Port of stock <c>ColumnHeaderView_c</c> (GUI.dll) — one clickable column heading. Stock
/// styles it through <c>multilistview/columnheader</c>, mirrored here as a USS class.
/// </summary>
[UxmlElement]
public partial class AoColumnHeaderView : AoView
{
    readonly AoLabel _label;
    readonly AoCaret _caret;

    public event Action Clicked;

    public AoColumnHeaderView()
    {
        AddToClassList("ao-column-header-view");
        style.flexDirection = FlexDirection.Row;
        _label = new AoLabel();
        _label.AddToClassList("ao-column-header-view__label");
        Add(_label);
        _caret = new AoCaret();
        _caret.AddToClassList("ao-column-header-view__caret");
        _caret.style.display = DisplayStyle.None;
        Add(_caret);
        pickingMode = PickingMode.Position;
        RegisterCallback<ClickEvent>(_ => Clicked?.Invoke());
    }

    /// <summary>
    /// Marks this column as the sort key. Published as <c>ao-column-header-view--sorted</c>
    /// (and <c>--descending</c>), and shown by the caret.
    /// </summary>
    public void SetSorted(bool sorted, bool ascending)
    {
        EnableInClassList("ao-column-header-view--sorted", sorted);
        EnableInClassList("ao-column-header-view--descending", sorted && !ascending);
        _caret.style.display = sorted ? DisplayStyle.Flex : DisplayStyle.None;
        _caret.PointsUp = ascending;
    }

    /// <summary>Stock <c>column_id</c>: stable identity so column order can be persisted.</summary>
    [UxmlAttribute("column_id")]
    public int ColumnId { get; set; }

    [UxmlAttribute("title")]
    public string Title
    {
        get => _label?.RawText;
        set { if (_label != null) _label.RawText = value; }
    }
}

/// <summary>
/// Port of stock <c>MultiListViewItem_c</c> (GUI.dll, 8 virtuals — a light row object, unlike
/// the 60-virtual views). Stock's <c>ListViewBaseItem_c</c> sits under it and carries the
/// selection state; both are collapsed into this one element.
///
/// In list mode it is a row of cells, one per column. In grid mode it is one cell holding
/// <see cref="CreateGridView"/>. A subclass decides what both look like; this base shows text.
/// </summary>
[UxmlElement]
public partial class AoMultiListViewItem : AoView
{
    readonly VisualElement _row;
    readonly List<string> _sortKeys = new();
    string[] _texts = Array.Empty<string>();
    VisualElement _gridView;
    VisualElement _slot;

    public event Action Clicked;
    public event Action DoubleClicked;

    public bool IsSelected { get; private set; }

    public AoMultiListViewItem()
    {
        AddToClassList("ao-multi-list-view-item");
        pickingMode = PickingMode.Position;

        _row = new VisualElement { pickingMode = PickingMode.Ignore };
        _row.style.flexDirection = FlexDirection.Row;
        _row.AddToClassList("ao-multi-list-view-item__row");
        Add(_row);

        RegisterCallback<ClickEvent>(OnClick);
    }

    void OnClick(ClickEvent evt)
    {
        if (evt.clickCount >= 2)
            DoubleClicked?.Invoke();
        else
            Clicked?.Invoke();
    }

    internal void SetTextCells(string[] cells) => _texts = cells ?? Array.Empty<string>();

    /// <summary>Builds the list-mode cells for these columns.</summary>
    internal void BuildCells(IReadOnlyList<AoMultiListView.AoColumn> columns)
    {
        _row.Clear();
        _sortKeys.Clear();

        for (int i = 0; i < columns.Count; i++)
        {
            VisualElement cell = CreateCell(columns[i].ColumnId, i) ?? new Label();
            cell.pickingMode = PickingMode.Ignore;
            cell.AddToClassList("ao-multi-list-view-item__cell");
            cell.style.width = columns[i].Width;
            cell.style.flexShrink = 0f;
            _row.Add(cell);
            _sortKeys.Add(SortKeyFor(columns[i].ColumnId, i) ?? string.Empty);
        }
    }

    /// <summary>The list-mode cell for a column. The base shows the text given for that position.</summary>
    protected virtual VisualElement CreateCell(int columnId, int columnIndex) =>
        new Label(columnIndex < _texts.Length ? _texts[columnIndex] ?? string.Empty : string.Empty);

    /// <summary>What sorting compares for a column. The base uses the text given for that position.</summary>
    protected virtual string SortKeyFor(int columnId, int columnIndex) =>
        columnIndex < _texts.Length ? _texts[columnIndex] : string.Empty;

    /// <summary>What the item shows as a grid cell. The base shows its first text.</summary>
    protected virtual VisualElement CreateGridView() =>
        new Label(_texts.Length > 0 ? _texts[0] : string.Empty);

    /// <summary>
    /// In the grid the item is a slot one pitch across with its cell centred in it, so the cell
    /// keeps its exact size whatever frame the skin gives the slot.
    /// </summary>
    internal void ApplyLayout(AoLayoutMode mode, Vector2 cell, Vector2 pitch)
    {
        bool grid = mode == AoLayoutMode.Grid;
        EnableInClassList("ao-multi-list-view-item--grid", grid);
        _row.style.display = grid ? DisplayStyle.None : DisplayStyle.Flex;

        if (grid && _slot == null)
        {
            _gridView = CreateGridView();
            _gridView.pickingMode = PickingMode.Ignore;
            _gridView.AddToClassList("ao-multi-list-view-item__grid");
            _gridView.style.width = cell.x;
            _gridView.style.height = cell.y;
            _gridView.style.flexShrink = 0f;
            _gridView.style.flexGrow = 0f;   // the cell is exact; nothing may stretch it

            _slot = AoMultiListView.CreateSlot();
            _slot.Add(_gridView);
            Add(_slot);
        }

        if (_slot != null)
            _slot.style.display = grid ? DisplayStyle.Flex : DisplayStyle.None;

        StyleLength Size(float v) => grid ? new StyleLength(v) : new StyleLength(StyleKeyword.Null);
        style.width = Size(pitch.x);
        style.height = Size(pitch.y);
        style.flexShrink = grid ? new StyleFloat(0f) : new StyleFloat(StyleKeyword.Null);

        // The grid places each box itself (LayoutGrid); a list row flows.
        if (!grid)
        {
            style.position = StyleKeyword.Null;
            style.left = StyleKeyword.Null;
            style.top = StyleKeyword.Null;
        }
    }

    public string SortKey(int columnIndex) =>
        columnIndex >= 0 && columnIndex < _sortKeys.Count ? _sortKeys[columnIndex] : string.Empty;

    /// <summary>The text of a list cell, when it is text.</summary>
    public string CellText(int index) =>
        index >= 0 && index < _row.childCount && _row[index] is Label label ? label.text : string.Empty;

    internal void SetSelected(bool selected)
    {
        IsSelected = selected;
        EnableInClassList("ao-multi-list-view-item--selected", selected);
    }

    /// <summary>Arbitrary payload the owner hangs off an item (an item id, a mission ref…).</summary>
    public object UserData { get; set; }
}
