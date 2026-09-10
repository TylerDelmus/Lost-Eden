using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Grid of <see cref="ItemCellView"/> cells sized to an <see cref="ItemContainer"/> capacity.
/// </summary>
public sealed class ItemContainerGridView
{
    const string CellTemplateResourcePath = "UI/ItemCell";

    readonly VisualElement _root;
    readonly VisualElement _grid;
    readonly List<ItemCellView> _cells = new List<ItemCellView>();

    IconTextureCache _icons;
    VisualTreeAsset _cellTemplate;
    int _capacity;

    public VisualElement Root => _root;
    public IReadOnlyList<ItemCellView> Cells => _cells;
    public int Capacity => _capacity;

    public ItemContainerGridView(VisualElement contentRoot)
    {
        _root = contentRoot?.Q<VisualElement>("root") ?? contentRoot
            ?? throw new System.ArgumentNullException(nameof(contentRoot));
        _grid = _root.Q<VisualElement>("cell-grid") ?? _root;
        UserInterface.EnsureStylesheet(_root, "UI/ItemContainerGrid");
    }

    public void Build(int capacity, IconTextureCache icons)
    {
        _icons = icons;
        _capacity = UnityEngine.Mathf.Max(0, capacity);
        _cellTemplate ??= UserInterface.LoadTemplate(CellTemplateResourcePath);

        _grid.Clear();
        _cells.Clear();

        if (_cellTemplate == null)
        {
            Debug.LogError($"[ItemContainerGrid] Missing Resources/{CellTemplateResourcePath}");
            return;
        }

        for (int i = 0; i < _capacity; i++)
        {
            ItemCellView cell = ItemCellView.Create(_cellTemplate, _icons);
            _cells.Add(cell);
            _grid.Add(cell.Root);
        }
    }

    public void Bind(ItemContainer container)
    {
        ClearCells();
        if (container == null || _cells.Count == 0)
            return;

        IReadOnlyList<InventoryItem> items = container.Items;
        for (int i = 0; i < items.Count; i++)
        {
            InventoryItem slot = items[i];
            if (slot == null)
                continue;

            int index = slot.Slot.Instance - container.Offset;
            if (index < 0 || index >= _cells.Count)
                continue;

            _cells[index].Bind(slot);
        }
    }

    public void Clear()
    {
        ClearCells();
    }

    public ItemCellView GetCell(int index)
    {
        if (index < 0 || index >= _cells.Count)
            return null;
        return _cells[index];
    }

    void ClearCells()
    {
        for (int i = 0; i < _cells.Count; i++)
            _cells[i].Clear();
    }
}
