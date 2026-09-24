using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Port of stock <c>BorderView_c : View</c> (GUI.dll) — a nine-slice frame drawn from AO GUI
/// art, and the most common container in the shipped XML after View itself.
///
/// The nine attribute names below are stock's own, taken from actual usage across
/// <c>cd_image/gui/Default/**/*.xml</c>: four corners, four edges and a centre fill, each
/// naming a UVGA entry. Painting is deferred (visual pass), so the slices are carried and
/// exposed but only the tint and a flat fallback are applied for now.
/// </summary>
[UxmlElement]
public partial class AoBorderView : AoView
{
    [UxmlAttribute("tl_gfx")] public string TopLeftGfx { get; set; }
    [UxmlAttribute("top_gfx")] public string TopGfx { get; set; }
    [UxmlAttribute("tr_gfx")] public string TopRightGfx { get; set; }
    [UxmlAttribute("left_gfx")] public string LeftGfx { get; set; }
    [UxmlAttribute("bg_gfx")] public string BackgroundGfx { get; set; }
    [UxmlAttribute("right_gfx")] public string RightGfx { get; set; }
    [UxmlAttribute("bl_gfx")] public string BottomLeftGfx { get; set; }
    [UxmlAttribute("bottom_gfx")] public string BottomGfx { get; set; }
    [UxmlAttribute("br_gfx")] public string BottomRightGfx { get; set; }

    string _colorSpec;
    Color _color = Color.white;
    float _alpha = 1f;

    /// <summary>
    /// Stock <c>color</c>: a tint applied to the frame art, either a GUIColors.xml name
    /// (<c>DEFAULT</c>) or an AO literal (<c>0x0080E9F3</c>). See <see cref="AoColors"/>.
    /// </summary>
    [UxmlAttribute("color")]
    public string ColorSpec
    {
        get => _colorSpec;
        set
        {
            _colorSpec = value;
            _color = AoColors.Resolve(value, Color.white);
            ApplyTint();
        }
    }

    /// <summary>The resolved tint.</summary>
    public Color ResolvedColor => _color;

    /// <summary>Stock <c>alpha</c>: frame opacity, independent of the view's own fade.</summary>
    [UxmlAttribute("alpha")]
    public float Alpha
    {
        get => _alpha;
        set { _alpha = value; ApplyTint(); }
    }

    void ApplyTint()
    {
        style.unityBackgroundImageTintColor = new Color(_color.r, _color.g, _color.b, _color.a * _alpha);
    }
}
