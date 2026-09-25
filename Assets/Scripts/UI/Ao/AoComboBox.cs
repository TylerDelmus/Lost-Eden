using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Port of stock <c>ComboBox_c</c> (GUI.dll) — an <i>editable</i> dropdown: a text field the
/// user can type into, plus a list of remembered choices. LoginWindow.xml uses it for the
/// username, which is why it has to accept a new account name as well as offer the saved ones.
///
/// It is one of the factory-registered controls (its name appears in GUIControl_c's registry
/// strings), hence the AoGuiControl base. UI Toolkit's DropdownField is deliberately not used:
/// that one is not editable.
/// </summary>
[UxmlElement]
public partial class AoComboBox : AoGuiControl
{
    readonly TextField _field;
    readonly Button _toggle;
    readonly AoCaret _caret;
    readonly VisualElement _popup;
    readonly List<string> _choices = new();

    public event Action<string> ValueChanged;
    public event Action<string> ChoiceSelected;

    public AoComboBox()
    {
        AddToClassList("ao-combo-box");
        style.flexDirection = FlexDirection.Row;

        _field = new TextField { isDelayed = false, style = { flexGrow = 1f } };
        _field.AddToClassList("ao-combo-box__field");
        _field.RegisterValueChangedCallback(evt => ValueChanged?.Invoke(evt.newValue));
        hierarchy.Add(_field);

        // The arrow is drawn, not typed: the pixel fonts are latin1, which has no triangle.
        _toggle = new Button(ToggleDropdown) { tabIndex = -1 };  // Tab is for text inputs only
        _toggle.AddToClassList("ao-combo-box__toggle");
        _caret = new AoCaret();
        _toggle.Add(_caret);
        hierarchy.Add(_toggle);

        // The list floats under the field. While open it lives at the top of the panel, not
        // inside this view: anything drawn after the combo box would otherwise paint over it.
        // Its placement is structure rather than look, so it is set here.
        _popup = new VisualElement { name = "combobox-popup" };
        _popup.AddToClassList("ao-combo-box__popup");
        _popup.style.position = Position.Absolute;
        _popup.style.display = DisplayStyle.None;

        RegisterCallback<GeometryChangedEvent>(_ => { if (IsDropdownOpen) PlacePopup(); });
        RegisterCallback<DetachFromPanelEvent>(_ => CloseDropdown());
    }

    /// <summary>Stock <c>value</c>: the current text, typed or picked.</summary>
    [UxmlAttribute("value")]
    public string Value
    {
        get => _field?.value;
        set { if (_field != null) _field.value = value ?? string.Empty; }
    }

    public IReadOnlyList<string> Choices => _choices;

    public bool IsDropdownOpen => _popup.parent != null && _popup.style.display.value == DisplayStyle.Flex;

    public void SetChoices(IEnumerable<string> choices)
    {
        _choices.Clear();
        _popup.Clear();
        if (choices == null)
            return;

        foreach (string choice in choices)
        {
            _choices.Add(choice);
            string captured = choice;

            var entry = new Button(() => Pick(captured)) { text = choice, tabIndex = -1 };
            entry.AddToClassList("ao-combo-box__entry");
            _popup.Add(entry);
        }
    }

    public void Pick(string choice)
    {
        Value = choice;
        CloseDropdown();
        ChoiceSelected?.Invoke(choice);
    }

    public void ToggleDropdown()
    {
        if (IsDropdownOpen)
            CloseDropdown();
        else
            OpenDropdown();
    }

    public void OpenDropdown()
    {
        if (_choices.Count == 0 || panel == null)
            return;

        panel.visualTree.Add(_popup);
        _popup.style.display = DisplayStyle.Flex;
        PlacePopup();
        AddToClassList("ao-combo-box--open");
        _caret.PointsUp = true;
    }

    public void CloseDropdown()
    {
        _popup.style.display = DisplayStyle.None;
        _popup.RemoveFromHierarchy();
        RemoveFromClassList("ao-combo-box--open");
        _caret.PointsUp = false;
    }

    /// <summary>Pins the list under this view, in the coordinates of wherever it now lives.</summary>
    void PlacePopup()
    {
        if (_popup.parent == null)
            return;

        Rect own = _popup.parent.WorldToLocal(worldBound);
        _popup.style.left = own.xMin;
        _popup.style.top = own.yMax;
        _popup.style.width = own.width;
    }

    public TextField Field => _field;

    bool _editable = true;

    /// <summary>
    /// Whether the text can be typed into, as stock's <c>ComboBox_c</c> always can. Ours: false
    /// makes it a plain picker — the field is read-only, takes no focus (so no Tab stop and no
    /// caret), and a click anywhere on it opens the list. Published as <c>ao-combo-box--fixed</c>.
    /// </summary>
    public bool Editable
    {
        get => _editable;
        set
        {
            if (_editable == value)
                return;

            _editable = value;
            _field.isReadOnly = !value;
            _field.focusable = value;

            // The field takes focus through its inner text element, so every part of it leaves
            // the Tab order, not only the outer field.
            _field.Query<VisualElement>().ForEach(e => e.tabIndex = value ? 0 : -1);
            EnableInClassList("ao-combo-box--fixed", !value);

            if (value)
                _field.UnregisterCallback<PointerDownEvent>(OnFixedFieldPointerDown, TrickleDown.TrickleDown);
            else
                _field.RegisterCallback<PointerDownEvent>(OnFixedFieldPointerDown, TrickleDown.TrickleDown);
        }
    }

    void OnFixedFieldPointerDown(PointerDownEvent evt)
    {
        ToggleDropdown();
        evt.StopPropagation();
    }
}
