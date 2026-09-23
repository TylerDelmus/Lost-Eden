using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Port of stock <c>ButtonBase_c : GUIControl_c : View</c> (GUI.dll) — press/hover/toggle
/// state and the click signal, without any art. Stock fires this through SignalTarget_c;
/// here it is a plain C# event.
/// </summary>
public abstract class AoButtonBase : AoGuiControl
{
    bool _pressed;
    bool _hovered;
    bool _toggled;

    public event Action Clicked;
    public event Action<bool> ToggledChanged;

    public bool IsPressed => _pressed;
    public bool IsHovered => _hovered;

    /// <summary>Stock keeps a toggle state on the base; <c>toggled_label</c> shows it.</summary>
    public bool Toggled
    {
        get => _toggled;
        set
        {
            if (_toggled == value)
                return;

            _toggled = value;
            OnStateChanged();
            ToggledChanged?.Invoke(value);
        }
    }

    /// <summary>
    /// When true a click flips <see cref="Toggled"/> instead of being momentary. Declared on
    /// the base rather than as a UXML attribute: the base is not authorable, so the concrete
    /// buttons re-expose it if they need it from markup.
    /// </summary>
    public bool IsToggle { get; set; }

    protected AoButtonBase()
    {
        focusable = true;
        pickingMode = PickingMode.Position;

        RegisterCallback<PointerEnterEvent>(_ => { _hovered = true; OnStateChanged(); });
        RegisterCallback<PointerLeaveEvent>(_ => { _hovered = false; _pressed = false; OnStateChanged(); });
        RegisterCallback<PointerDownEvent>(_ => { _pressed = true; OnStateChanged(); });
        RegisterCallback<PointerUpEvent>(OnPointerUp);
    }

    void OnPointerUp(PointerUpEvent evt)
    {
        bool wasPressed = _pressed;
        _pressed = false;

        if (!wasPressed || !_hovered)
        {
            OnStateChanged();
            return;
        }

        if (IsToggle)
            Toggled = !_toggled;
        else
            OnStateChanged();

        Clicked?.Invoke();
    }

    /// <summary>Hook for derived buttons to repaint on press/hover/toggle changes.</summary>
    protected virtual void OnStateChanged()
    {
    }
}

/// <summary>
/// Port of stock <c>Button_c : ButtonBase_c</c> (GUI.dll) — the labelled button used
/// throughout the shipped XML (75 instances). <c>toggled_label</c> is shown while
/// <see cref="AoButtonBase.Toggled"/> is set.
/// </summary>
[UxmlElement]
public partial class AoButton : AoButtonBase
{
    readonly Label _label;

    public AoButton()
    {
        _label = new Label { pickingMode = PickingMode.Ignore };
        Add(_label);
        style.flexDirection = FlexDirection.Row;
        style.justifyContent = Justify.Center;
        style.alignItems = Align.Center;
    }

    string _labelText;
    string _toggledLabel;

    /// <summary>Stock <c>label</c>; may be a <c>#table:index</c> string-table ref.</summary>
    [UxmlAttribute("label")]
    public string Label
    {
        get => _labelText;
        set { _labelText = value; OnStateChanged(); }
    }

    /// <summary>Stock <c>toggled_label</c>: replaces the label while toggled.</summary>
    [UxmlAttribute("toggled_label")]
    public string ToggledLabel
    {
        get => _toggledLabel;
        set { _toggledLabel = value; OnStateChanged(); }
    }

    protected override void OnStateChanged()
    {
        if (_label == null)
            return;

        _label.text = (Toggled && !string.IsNullOrEmpty(_toggledLabel) ? _toggledLabel : _labelText)
                      ?? string.Empty;
    }
}

/// <summary>
/// Port of stock <c>TextButton_c</c> (GUI.dll) — a text-only button carrying its own colour
/// per state rather than taking art. Attribute names are from the shipped XML.
/// </summary>
[UxmlElement]
public partial class AoTextButton : AoButtonBase
{
    readonly Label _label;

    public AoTextButton()
    {
        _label = new Label { pickingMode = PickingMode.Ignore };
        Add(_label);
    }

    [UxmlAttribute("text")]
    public string Text
    {
        get => _label?.text;
        set { if (_label != null) _label.text = value ?? string.Empty; }
    }

    [UxmlAttribute("font")] public string Font { get; set; }

    // Stock ships these as AO literals (0x0080E9F3) or GUIColors.xml names, neither of which
    // Unity's Color attribute understands — see AoColors.
    string _colorSpec, _hoverSpec, _pressedSpec;
    Color _normal = Color.white, _hover = Color.white, _pressed = Color.white;

    [UxmlAttribute("color")]
    public string ColorSpec
    {
        get => _colorSpec;
        set { _colorSpec = value; _normal = AoColors.Resolve(value, Color.white); OnStateChanged(); }
    }

    [UxmlAttribute("hover_color")]
    public string HoverColorSpec
    {
        get => _hoverSpec;
        set { _hoverSpec = value; _hover = AoColors.Resolve(value, Color.white); OnStateChanged(); }
    }

    [UxmlAttribute("pressed_color")]
    public string PressedColorSpec
    {
        get => _pressedSpec;
        set { _pressedSpec = value; _pressed = AoColors.Resolve(value, Color.white); OnStateChanged(); }
    }

    protected override void OnStateChanged()
    {
        if (_label == null)
            return;

        _label.style.color = IsPressed ? _pressed : IsHovered ? _hover : _normal;
    }
}
