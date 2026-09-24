using System;
using System.Collections.Generic;
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
    readonly VisualElement _popup;
    readonly List<string> _choices = new();

    public event Action<string> ValueChanged;
    public event Action<string> ChoiceSelected;

    public AoComboBox()
    {
        AddToClassList("combobox");
        style.flexDirection = FlexDirection.Row;

        _field = new TextField { isDelayed = false, style = { flexGrow = 1f } };
        _field.RegisterValueChangedCallback(evt => ValueChanged?.Invoke(evt.newValue));
        hierarchy.Add(_field);

        _toggle = new Button(ToggleDropdown) { text = "▾" };
        _toggle.AddToClassList("combobox__toggle");
        hierarchy.Add(_toggle);

        _popup = new VisualElement { name = "combobox-popup" };
        _popup.AddToClassList("combobox__popup");
        _popup.style.position = Position.Absolute;
        _popup.style.left = 0f;
        _popup.style.right = 0f;
        _popup.style.top = new StyleLength(StyleKeyword.Auto);
        _popup.style.display = DisplayStyle.None;
        hierarchy.Add(_popup);
    }

    /// <summary>Stock <c>value</c>: the current text, typed or picked.</summary>
    [UxmlAttribute("value")]
    public string Value
    {
        get => _field?.value;
        set { if (_field != null) _field.value = value ?? string.Empty; }
    }

    public IReadOnlyList<string> Choices => _choices;

    public bool IsDropdownOpen => _popup.style.display.value == DisplayStyle.Flex;

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

            var entry = new Button(() => Pick(captured)) { text = choice };
            entry.AddToClassList("combobox__entry");
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
        if (_choices.Count == 0)
            return;

        _popup.style.display = DisplayStyle.Flex;
        _popup.BringToFront();
    }

    public void CloseDropdown() => _popup.style.display = DisplayStyle.None;

    public TextField Field => _field;
}
