using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Port of stock <c>MultiListView_c : View</c> (GUI.dll, vftable 101c68fc, 60 virtuals) — the
/// multi-column list behind inventory, missions, perks, the NCU and the friends list.
///
/// It never appears in the shipped XML: every instance is built in code, which is why its
/// surface comes from the binary rather than from a tag. The one attribute recovered beside
/// its vftable is <c>column_id</c> (shared with <see cref="AoColumnHeaderView"/>), along with
/// the style path <c>multilistview/columnheader</c>.
///
/// Rows are <see cref="AoMultiListViewItem"/>; the header row is built from
/// <see cref="AoColumnHeaderView"/> and drives sorting.
/// </summary>
[UxmlElement]
public partial class AoMultiListView : AoView
{
    readonly VisualElement _header;
    readonly AoScrollView _body;
    readonly List<AoColumn> _columns = new();
    readonly List<AoMultiListViewItem> _items = new();

    int _sortColumn = -1;
    bool _sortAscending = true;
    int _selectedIndex = -1;

    public event Action<int> SelectionChanged;
    public event Action<int, bool> SortChanged;
    public event Action<int> ItemActivated;

    public IReadOnlyList<AoMultiListViewItem> Items => _items;
    public int ColumnCount => _columns.Count;
    public int SortColumn => _sortColumn;
    public bool SortAscending => _sortAscending;

    public AoMultiListView()
    {
        _header = new VisualElement { name = "columnheader" };
        _header.style.flexDirection = FlexDirection.Row;
        _header.AddToClassList("multilistview__columnheader");
        hierarchy.Add(_header);

        _body = new AoScrollView { name = "listbody" };
        _body.style.flexGrow = 1f;
        hierarchy.Add(_body);
    }

    /// <summary>Rows go into the scrolling body, not beside the header.</summary>
    public override VisualElement contentContainer => _body?.contentContainer ?? this;

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

    // ---- rows ---------------------------------------------------------------------------

    public AoMultiListViewItem AddItem(params string[] cells)
    {
        var item = new AoMultiListViewItem();
        item.SetCells(cells, _columns);

        // Resolve the row's position at click time, not at add time: sorting reorders _items,
        // so a captured index would select a different row after the first sort.
        item.Clicked += () => SelectedIndex = _items.IndexOf(item);
        item.DoubleClicked += () => ItemActivated?.Invoke(_items.IndexOf(item));

        _items.Add(item);
        Add(item);
        return item;
    }

    public void Clear()
    {
        _items.Clear();
        _body.contentContainer.Clear();
        _selectedIndex = -1;
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

    /// <summary>Clicking the active column flips direction, matching stock.</summary>
    public void SortBy(int columnIndex)
    {
        if (columnIndex < 0 || columnIndex >= _columns.Count)
            return;

        if (_sortColumn == columnIndex)
            _sortAscending = !_sortAscending;
        else
        {
            _sortColumn = columnIndex;
            _sortAscending = true;
        }

        ApplySort();
        SortChanged?.Invoke(_sortColumn, _sortAscending);
    }

    void ApplySort()
    {
        if (_sortColumn < 0)
            return;

        // Selection follows the row, not the slot it happened to be in.
        AoMultiListViewItem selected =
            _selectedIndex >= 0 && _selectedIndex < _items.Count ? _items[_selectedIndex] : null;

        _items.Sort((a, b) =>
        {
            int cmp = string.Compare(a.CellText(_sortColumn), b.CellText(_sortColumn),
                                     StringComparison.OrdinalIgnoreCase);
            return _sortAscending ? cmp : -cmp;
        });

        VisualElement content = _body.contentContainer;
        content.Clear();
        foreach (AoMultiListViewItem item in _items)
            content.Add(item);

        // The row keeps its selected styling; only its index moved.
        _selectedIndex = selected != null ? _items.IndexOf(selected) : -1;
    }
}

/// <summary>
/// Port of stock <c>ColumnHeaderView_c</c> (GUI.dll) — one clickable column heading. Stock
/// styles it through <c>multilistview/columnheader</c>, mirrored here as a USS class.
/// </summary>
[UxmlElement]
public partial class AoColumnHeaderView : AoView
{
    readonly Label _label;

    public event Action Clicked;

    public AoColumnHeaderView()
    {
        AddToClassList("multilistview__columnheader__cell");
        _label = new Label { pickingMode = PickingMode.Ignore };
        Add(_label);
        pickingMode = PickingMode.Position;
        RegisterCallback<ClickEvent>(_ => Clicked?.Invoke());
    }

    /// <summary>Stock <c>column_id</c>: stable identity so column order can be persisted.</summary>
    [UxmlAttribute("column_id")]
    public int ColumnId { get; set; }

    [UxmlAttribute("title")]
    public string Title
    {
        get => _label?.text;
        set { if (_label != null) _label.text = value ?? string.Empty; }
    }
}

/// <summary>
/// Port of stock <c>MultiListViewItem_c</c> (GUI.dll, 8 virtuals — a light row object, unlike
/// the 60-virtual views). Stock's <c>ListViewBaseItem_c</c> sits under it and carries the
/// selection state; both are collapsed into this one row element.
/// </summary>
[UxmlElement]
public partial class AoMultiListViewItem : AoView
{
    readonly List<Label> _cells = new();

    public event Action Clicked;
    public event Action DoubleClicked;

    public bool IsSelected { get; private set; }

    public AoMultiListViewItem()
    {
        AddToClassList("multilistview__item");
        style.flexDirection = FlexDirection.Row;
        pickingMode = PickingMode.Position;
        RegisterCallback<ClickEvent>(OnClick);
    }

    void OnClick(ClickEvent evt)
    {
        if (evt.clickCount >= 2)
            DoubleClicked?.Invoke();
        else
            Clicked?.Invoke();
    }

    internal void SetCells(IReadOnlyList<string> cells, IReadOnlyList<AoMultiListView.AoColumn> columns)
    {
        Clear();
        _cells.Clear();

        for (int i = 0; i < cells.Count; i++)
        {
            var cell = new Label(cells[i] ?? string.Empty) { pickingMode = PickingMode.Ignore };
            if (i < columns.Count)
                cell.style.width = columns[i].Width;

            _cells.Add(cell);
            Add(cell);
        }
    }

    public string CellText(int index) =>
        index >= 0 && index < _cells.Count ? _cells[index].text : string.Empty;

    internal void SetSelected(bool selected)
    {
        IsSelected = selected;
        EnableInClassList("multilistview__item--selected", selected);
    }

    /// <summary>Arbitrary payload the owner hangs off a row (an item id, a mission ref…).</summary>
    public object UserData { get; set; }
}
