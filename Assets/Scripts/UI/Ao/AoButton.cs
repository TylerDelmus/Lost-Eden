using System;
using System.Collections.Generic;
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
        // Tab moves between text inputs only; a negative tabIndex keeps a button clickable but out
        // of the focus ring.
        tabIndex = -1;
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
    // The frame is drawn here rather than by UI Toolkit, because UI Toolkit has no cut corner.
    // The skin still owns it: the colours and the cut come from these properties, and the
    // outline's thickness per side from the ordinary border-width, which also insets the label.
    static readonly CustomStyleProperty<Color> FillProperty = new("--ao-button-fill");
    static readonly CustomStyleProperty<Color> LineProperty = new("--ao-button-line");
    static readonly CustomStyleProperty<int> CutProperty = new("--ao-button-cut");

    readonly AoLabel _label;
    Color _fill = Color.clear;
    Color _line = Color.clear;
    int _cut;

    public AoButton() : base("ao-button")
    {
        _label = new AoLabel();
        _label.AddToClassList("ao-button__label");
        Add(_label);

        RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
        generateVisualContent += DrawFrame;
    }

    void OnCustomStyleResolved(CustomStyleResolvedEvent evt)
    {
        ICustomStyle custom = evt.customStyle;
        _fill = custom.TryGetValue(FillProperty, out Color fill) ? fill : Color.clear;
        _line = custom.TryGetValue(LineProperty, out Color line) ? line : Color.clear;
        _cut = custom.TryGetValue(CutProperty, out int cut) ? Mathf.Max(0, cut) : 0;
        MarkDirtyRepaint();
    }

    /// <summary>
    /// The fill and the outline, with the top-right corner cut by a one-pixel diagonal that
    /// steps <c>--ao-button-cut</c> rows down, each step flanked by a half-strength pixel on
    /// either side so the diagonal reads smooth without blurring the straight edges. The area
    /// beyond the cut is left empty, so whatever is behind the button shows through it.
    /// </summary>
    void DrawFrame(MeshGenerationContext mgc)
    {
        int w = Mathf.RoundToInt(layout.width);
        int h = Mathf.RoundToInt(layout.height);
        if (w <= 0 || h <= 0)
            return;

        int left = Mathf.RoundToInt(resolvedStyle.borderLeftWidth);
        int right = Mathf.RoundToInt(resolvedStyle.borderRightWidth);
        int top = Mathf.RoundToInt(resolvedStyle.borderTopWidth);
        int bottom = Mathf.RoundToInt(resolvedStyle.borderBottomWidth);
        int cut = Mathf.Min(_cut, Mathf.Min(h - bottom, w - left - right));
        if (cut < top)
            cut = 0;

        var rects = new List<(float x0, float y0, float x1, float y1, Color32 c)>();
        void Rect(int x0, int y0, int x1, int y1, Color c)
        {
            if (x1 > x0 && y1 > y0 && c.a > 0f)
                rects.Add((x0, y0, x1, y1, c));
        }

        // Where the diagonal crosses row y (0 <= y < cut): it meets the right border's inner
        // column on the last cut row.
        int Diagonal(int y) => w - right - cut + 1 + y;

        // Fill: every row between the top and bottom lines, stopping at the diagonal while the
        // corner is being cut.
        for (int y = top; y < h - bottom; y++)
            Rect(left, y, y < cut ? Diagonal(y) : w - right, y + 1, _fill);

        if (cut == 0)
        {
            Rect(0, 0, w, top, _line);
            Rect(w - right, top, w, h - bottom, _line);
        }
        else
        {
            for (int y = 0; y < top; y++)
                Rect(0, y, Diagonal(y) + 1, y + 1, _line);

            Color half = new Color(_line.r, _line.g, _line.b, _line.a * 0.5f);
            for (int y = 0; y < cut; y++)
            {
                int x = Diagonal(y);
                if (y >= top)
                {
                    Rect(x, y, x + 1, y + 1, _line);
                    Rect(x - 1, y, x, y + 1, half);   // inside, over the fill
                }
                Rect(x + 1, y, x + 2, y + 1, half);   // outside, over whatever is behind
            }

            Rect(w - right, cut, w, h - bottom, _line);
        }

        Rect(0, top, left, h - bottom, _line);
        Rect(0, h - bottom, w, h, _line);

        if (rects.Count == 0)
            return;

        MeshWriteData mesh = mgc.Allocate(rects.Count * 4, rects.Count * 6);
        for (int i = 0; i < rects.Count; i++)
        {
            var r = rects[i];
            ushort b = (ushort)(i * 4);
            mesh.SetNextVertex(new Vertex { position = new Vector3(r.x0, r.y0, Vertex.nearZ), tint = r.c });
            mesh.SetNextVertex(new Vertex { position = new Vector3(r.x1, r.y0, Vertex.nearZ), tint = r.c });
            mesh.SetNextVertex(new Vertex { position = new Vector3(r.x1, r.y1, Vertex.nearZ), tint = r.c });
            mesh.SetNextVertex(new Vertex { position = new Vector3(r.x0, r.y1, Vertex.nearZ), tint = r.c });

            mesh.SetNextIndex(b); mesh.SetNextIndex((ushort)(b + 1)); mesh.SetNextIndex((ushort)(b + 2));
            mesh.SetNextIndex(b); mesh.SetNextIndex((ushort)(b + 2)); mesh.SetNextIndex((ushort)(b + 3));
        }
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
