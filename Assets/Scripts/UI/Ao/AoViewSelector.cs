using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

/// <summary>
/// Port of stock <c>ViewSelector_c : GUIControl_c : View</c> (GUI.dll) — shows exactly one of
/// its children at a time. Stock uses it for the paged areas of a window (the shipped XML has
/// eight), and <c>InventoryView_c</c> builds one directly.
///
/// Stock keeps no per-page attributes: the pages are simply the child views, selected by index
/// or by name.
/// </summary>
[UxmlElement]
public partial class AoViewSelector : AoGuiControl
{
    int _selectedIndex = -1;

    public event Action<int> SelectionChanged;

    public AoViewSelector()
    {
        RegisterCallback<AttachToPanelEvent>(_ => ApplySelection());
    }

    /// <summary>Index of the visible page, or -1 for none.</summary>
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (_selectedIndex == value)
                return;

            _selectedIndex = value;
            ApplySelection();
            SelectionChanged?.Invoke(value);
        }
    }

    /// <summary>Selects the child whose <c>name</c> matches; no-op when absent.</summary>
    public bool Select(string childName)
    {
        for (int i = 0; i < childCount; i++)
        {
            if (this[i].name == childName)
            {
                SelectedIndex = i;
                return true;
            }
        }

        return false;
    }

    public IEnumerable<string> PageNames
    {
        get
        {
            for (int i = 0; i < childCount; i++)
                yield return this[i].name;
        }
    }

    void ApplySelection()
    {
        for (int i = 0; i < childCount; i++)
        {
            VisualElement page = this[i];
            bool shown = i == _selectedIndex;
            page.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            page.pickingMode = shown ? PickingMode.Position : PickingMode.Ignore;
        }
    }
}
