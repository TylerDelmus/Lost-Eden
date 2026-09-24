using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Port of stock <c>TextView_c : ScrollView_c : GUIControl_c : View</c> (GUI.dll).
///
/// The base is not a mistake: in AO a text view *is* a scroll view with a text child, which is
/// why any label can scroll and why <c>v_scrollbar_mode</c> appears on TextView tags in the
/// shipped XML. The label lives in the inherited scrolling content container.
///
/// <c>value</c> carries stock's markup: string-table refs like <c>#10010:7</c> and inline HTML
/// (<c>&lt;font color=…&gt;</c>, <c>&lt;br&gt;</c>) that stock runs through HTMLParser_c. Both
/// are stored raw here; resolution belongs to the text pass, not this class.
/// </summary>
[UxmlElement]
public partial class AoTextView : AoScrollView
{
    readonly AoLabel _label;

    public AoTextView()
    {
        AddToClassList("ao-text-view");
        // Wrapping is stock behaviour (a TextView is a scroll view around wrapped text), not a
        // look, so it is set here rather than left to the skin.
        _label = new AoLabel { style = { whiteSpace = WhiteSpace.Normal } };
        _label.AddToClassList("ao-text-view__label");
        Add(_label);
    }

    string _value;

    /// <summary>Stock <c>value</c>: the text, possibly a <c>#table:index</c> ref or HTML.</summary>
    [UxmlAttribute("value")]
    public string Value
    {
        get => _value;
        set
        {
            _value = value;
            if (_label != null)
                _label.RawText = value;
        }
    }

    string _colorSpec;
    Color _color = Color.white;

    /// <summary>
    /// Stock <c>color</c>: a GUIColors.xml name or an AO literal. See <see cref="AoColors"/>.
    /// Only an explicit colour is written inline; without one the skin's text colour stands.
    /// </summary>
    [UxmlAttribute("color")]
    public string ColorSpec
    {
        get => _colorSpec;
        set
        {
            _colorSpec = value;
            _color = AoColors.Resolve(value, Color.white);
            if (_label != null)
                _label.style.color = AoColors.TryParse(value, out Color c) ? new StyleColor(c) : new StyleColor(StyleKeyword.Null);
        }
    }

    /// <summary>The resolved text colour.</summary>
    public Color ResolvedColor => _color;

    string _font;

    /// <summary>
    /// Stock <c>font</c>: a GUI font key such as <c>LARGE</c> or <c>CC17</c>. Applied as the
    /// class <c>ao-font--large</c>; the skin maps each key to a face and size.
    /// </summary>
    [UxmlAttribute("font")]
    public string Font
    {
        get => _font;
        set { SwapFontClass(this, _font, value); _font = value; }
    }

    /// <summary>Stock <c>use_macros</c>: run the value through stock's macro expansion.</summary>
    [UxmlAttribute("use_macros")]
    public bool UseMacros { get; set; }

    /// <summary>Stock <c>feature_flags</c>, kept raw until the bits are recovered.</summary>
    [UxmlAttribute("feature_flags")]
    public int FeatureFlags { get; set; }

    /// <summary>The label element, for callers that need to style it directly.</summary>
    public AoLabel TextElement => _label;
}
