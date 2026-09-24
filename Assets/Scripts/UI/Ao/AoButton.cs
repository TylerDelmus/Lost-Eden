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

    readonly string _pressedClass;
    readonly string _onClass;

    /// <param name="block">
    /// The concrete button's USS block name. Hover and disabled come from the <c>:hover</c> and
    /// <c>:disabled</c> pseudo-classes; the two states UI Toolkit has no pseudo-class for are
    /// published as <c>block--pressed</c> and <c>block--on</c> (toggled).
    /// </param>
    protected AoButtonBase(string block)
    {
        AddToClassList(block);
        _pressedClass = block + "--pressed";
        _onClass = block + "--on";

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

    void OnStateChanged()
    {
        EnableInClassList(_pressedClass, _pressed);
        EnableInClassList(_onClass, _toggled);
        OnButtonStateChanged();
    }

    /// <summary>Hook for derived buttons to repaint on press/hover/toggle changes.</summary>
    protected virtual void OnButtonStateChanged()
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
    readonly AoLabel _label;

    public AoButton() : base("ao-button")
    {
        _label = new AoLabel();
        _label.AddToClassList("ao-button__label");
        Add(_label);
    }

    string _labelText;
    string _toggledLabel;

    /// <summary>Stock <c>label</c>; may be a <c>#table:index</c> string-table ref.</summary>
    [UxmlAttribute("label")]
    public string Label
    {
        get => _labelText;
        set { _labelText = value; OnButtonStateChanged(); }
    }

    /// <summary>Stock <c>toggled_label</c>: replaces the label while toggled.</summary>
    [UxmlAttribute("toggled_label")]
    public string ToggledLabel
    {
        get => _toggledLabel;
        set { _toggledLabel = value; OnButtonStateChanged(); }
    }

    protected override void OnButtonStateChanged()
    {
        if (_label == null)
            return;

        _label.RawText = Toggled && !string.IsNullOrEmpty(_toggledLabel) ? _toggledLabel : _labelText;
    }
}

/// <summary>
/// Port of stock <c>TextButton_c</c> (GUI.dll) — a text-only button carrying its own colour
/// per state rather than taking art. Attribute names are from the shipped XML.
/// </summary>
[UxmlElement]
public partial class AoTextButton : AoButtonBase
{
    readonly AoLabel _label;

    public AoTextButton() : base("ao-text-button")
    {
        _label = new AoLabel();
        _label.AddToClassList("ao-text-button__label");
        Add(_label);
    }

    [UxmlAttribute("text")]
    public string Text
    {
        get => _label?.RawText;
        set { if (_label != null) _label.RawText = value; }
    }

    string _font;

    /// <summary>Stock <c>font</c> key, applied as <c>ao-font--key</c>; see <see cref="AoTextView.Font"/>.</summary>
    [UxmlAttribute("font")]
    public string Font
    {
        get => _font;
        set { SwapFontClass(this, _font, value); _font = value; }
    }

    // Stock ships these as AO literals (0x0080E9F3) or GUIColors.xml names, neither of which
    // Unity's Color attribute understands — see AoColors.
    //
    // A state with no colour of its own falls back to the skin rather than to white, so a
    // button authored without colours is styled entirely by .ao-text-button and its states.
    string _colorSpec, _hoverSpec, _pressedSpec;
    Color? _normal, _hover, _pressed;

    [UxmlAttribute("color")]
    public string ColorSpec
    {
        get => _colorSpec;
        set { _colorSpec = value; _normal = Parse(value); OnButtonStateChanged(); }
    }

    [UxmlAttribute("hover_color")]
    public string HoverColorSpec
    {
        get => _hoverSpec;
        set { _hoverSpec = value; _hover = Parse(value); OnButtonStateChanged(); }
    }

    [UxmlAttribute("pressed_color")]
    public string PressedColorSpec
    {
        get => _pressedSpec;
        set { _pressedSpec = value; _pressed = Parse(value); OnButtonStateChanged(); }
    }

    static Color? Parse(string spec) => AoColors.TryParse(spec, out Color c) ? c : null;

    protected override void OnButtonStateChanged()
    {
        if (_label == null)
            return;

        Color? color = IsPressed ? _pressed ?? _hover ?? _normal
                     : IsHovered ? _hover ?? _normal
                     : _normal;
        _label.style.color = color.HasValue ? new StyleColor(color.Value) : new StyleColor(StyleKeyword.Null);
    }
}
