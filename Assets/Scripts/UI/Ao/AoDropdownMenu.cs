using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Port of stock <c>DropdownMenu_c</c> (GUI.dll) — a button that opens a list of fixed choices
/// and shows the one picked. Unlike <see cref="AoComboBox"/> nothing can be typed. The surface
/// follows its exports: <c>AppendItem(id, label)</c>, <c>SelectByIndex</c>,
/// <c>GetSelection</c>, <c>GetItemID</c>, <c>Open</c>, <c>Clear</c>.
///
/// <para>
/// <b>Not read:</b> its drawing and its popup (<c>PopupMenu_c</c>); here the list floats under
/// the button at the top of the panel, as the combo box's does, and closes on a pick or on a
/// click anywhere else.
/// </para>
/// </summary>
[UxmlElement]
public partial class AoDropdownMenu : AoGuiControl
{
    readonly Button _button;
    readonly AoLabel _label;
    readonly AoCaret _caret;
    readonly VisualElement _popup;
    readonly List<(object Id, string Label)> _items = new();
    int _selection = -1;

    /// <summary>A choice was picked from the list (stock's SlotMenuItemSelected).</summary>
    public event Action<int> SelectionChanged;

    public AoDropdownMenu()
    {
        AddToClassList("ao-dropdown-menu");

        _button = new Button(Toggle) { tabIndex = -1 };   // Tab is for text inputs only
        _button.AddToClassList("ao-dropdown-menu__button");
        _button.style.flexDirection = FlexDirection.Row;
        _button.style.alignItems = Align.Center;
        hierarchy.Add(_button);

        _label = new AoLabel();
        _label.AddToClassList("ao-dropdown-menu__label");
        _button.Add(_label);

        // Drawn, not typed: the pixel fonts are latin1, which has no triangle.
        _caret = new AoCaret();
        _caret.AddToClassList("ao-dropdown-menu__caret");
        _button.Add(_caret);

        _popup = new VisualElement { name = "dropdownmenu-popup" };
        _popup.AddToClassList("ao-dropdown-menu__popup");
        _popup.style.position = Position.Absolute;

        RegisterCallback<DetachFromPanelEvent>(_ => Close());
    }

    public int Count => _items.Count;

    /// <summary>Stock <c>GetSelection</c>: the picked index, or -1.</summary>
    public int Selection => _selection;

    public object GetItemID(int index) => index >= 0 && index < _items.Count ? _items[index].Id : null;

    public bool IsOpen => _popup.parent != null;

    /// <summary>Stock <c>AppendItem</c>: adds a choice, returns its index.</summary>
    public int AppendItem(object id, string label)
    {
        _items.Add((id, label ?? string.Empty));
        return _items.Count - 1;
    }

    public void Clear()
    {
        Close();
        _items.Clear();
        _selection = -1;
        _label.RawText = string.Empty;
    }

    /// <summary>Stock <c>SelectByIndex</c>; <paramref name="notify"/> raises <see cref="SelectionChanged"/>.</summary>
    public void SelectByIndex(int index, bool notify = false)
    {
        if (index < 0 || index >= _items.Count)
            return;

        _selection = index;
        _label.RawText = _items[index].Label;
        if (notify)
            SelectionChanged?.Invoke(index);
    }

    public void Toggle()
    {
        if (IsOpen)
            Close();
        else
            Open();
    }

    public void Open()
    {
        if (panel == null || _items.Count == 0 || IsOpen)
            return;

        _popup.Clear();
        for (int i = 0; i < _items.Count; i++)
        {
            int captured = i;
            var entry = new Button(() => Pick(captured)) { text = _items[i].Label, tabIndex = -1 };
            entry.AddToClassList("ao-dropdown-menu__entry");
            entry.EnableInClassList("ao-dropdown-menu__entry--selected", i == _selection);
            _popup.Add(entry);
        }

        VisualElement top = panel.visualTree;
        top.Add(_popup);
        Rect own = top.WorldToLocal(_button.worldBound);
        _popup.style.left = own.xMin;
        _popup.style.top = own.yMax;
        _popup.style.minWidth = own.width;

        AddToClassList("ao-dropdown-menu--open");
        _caret.PointsUp = true;
        top.RegisterCallback<PointerDownEvent>(OnPanelPointerDown, TrickleDown.TrickleDown);
    }

    public void Close()
    {
        if (!IsOpen)
            return;

        _popup.parent?.UnregisterCallback<PointerDownEvent>(OnPanelPointerDown, TrickleDown.TrickleDown);
        _popup.RemoveFromHierarchy();
        RemoveFromClassList("ao-dropdown-menu--open");
        _caret.PointsUp = false;
    }

    void Pick(int index)
    {
        Close();
        SelectByIndex(index, notify: true);
    }

    // A click anywhere but the list or the button closes the list. The button's own click then
    // does the toggling, so the button has to count as inside. Contains() does not count the
    // element itself, and a click on the button's padding targets the button.
    void OnPanelPointerDown(PointerDownEvent evt)
    {
        if (evt.target is VisualElement target && (IsOrContains(_popup, target) || IsOrContains(_button, target)))
            return;
        Close();
    }

    static bool IsOrContains(VisualElement element, VisualElement target) =>
        element == target || element.Contains(target);
}
