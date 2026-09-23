using UnityEngine.UIElements;

/// <summary>
/// Port of stock <c>HLayoutSpacer</c> / <c>VLayoutSpacer</c>. Stock's LayoutNode places a
/// spacer that soaks up the slack on its axis, which is exactly flex-grow — so the spacer is
/// the one piece of stock's layout system that survives as a real element rather than being
/// absorbed into USS. It is by far the most common element in the shipped XML (89 H, 55 V).
/// </summary>
[UxmlElement]
public partial class AoSpacer : VisualElement
{
    public AoSpacer()
    {
        style.flexGrow = 1f;
        style.flexShrink = 1f;
        pickingMode = PickingMode.Ignore;
    }

    /// <summary>
    /// Stock spacers carry a weight when several share one node; default 1 matches a bare
    /// <c>&lt;HLayoutSpacer/&gt;</c>.
    /// </summary>
    [UxmlAttribute("weight")]
    public float Weight
    {
        get => style.flexGrow.value;
        set => style.flexGrow = value;
    }
}
