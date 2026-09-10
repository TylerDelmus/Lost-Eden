using AODB.Common.Enums;
using AODB.Common.RDBObjects;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Reusable inventory/nano cell that displays an icon from <see cref="StatId.icon"/>.
/// </summary>
public sealed class ItemCellView
{
    readonly VisualElement _root;
    readonly UnityEngine.UIElements.Image _icon;
    readonly IconTextureCache _icons;

    public VisualElement Root => _root;

    public ItemCellView(VisualElement root, IconTextureCache icons)
    {
        _root = root ?? throw new System.ArgumentNullException(nameof(root));
        _icons = icons;
        _icon = root.Q<UnityEngine.UIElements.Image>("icon")
            ?? root.Q<UnityEngine.UIElements.Image>();
    }

    public static ItemCellView Create(VisualTreeAsset cellTemplate, IconTextureCache icons)
    {
        if (cellTemplate == null)
            throw new System.ArgumentNullException(nameof(cellTemplate));

        TemplateContainer instance = cellTemplate.Instantiate();
        VisualElement cellRoot = instance.Q<VisualElement>("cell") ?? (VisualElement)instance;
        UserInterface.EnsureStylesheet(cellRoot, "UI/ItemCell");
        return new ItemCellView(cellRoot, icons);
    }

    public void Bind(Item item)
    {
        if (item == null)
        {
            Clear();
            return;
        }

        BindTemplate(item.Template);
    }

    public void Bind(NanoSpell nano)
    {
        if (nano == null)
        {
            Clear();
            return;
        }

        BindTemplate(nano.Template);
    }

    public void Bind(InventoryItem slot)
    {
        if (slot?.Item == null)
        {
            Clear();
            return;
        }

        Bind(slot.Item);
    }

    public void Clear()
    {
        if (_icon != null)
            _icon.image = null;
    }

    void BindTemplate(ItemBase template)
    {
        if (_icon == null)
            return;

        if (template?.Stats == null
            || !template.Stats.TryGetValue(StatId.icon, out uint iconId)
            || iconId == 0
            || _icons == null)
        {
            _icon.image = null;
            return;
        }

        _icon.image = _icons.GetIcon((int)iconId);
        _icon.scaleMode = ScaleMode.ScaleToFit;
    }
}
