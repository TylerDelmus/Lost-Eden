using UnityEngine.UIElements;

/// <summary>
/// Port of stock <c>ScrollView_c : GUIControl_c : View</c> (GUI.dll).
///
/// Stock scrolls a nominated child (its <c>scroll_client</c>, authored as a
/// <c>&lt;ScrollViewChild&gt;</c>) inside a clipped viewport. Rather than reimplement that,
/// this hosts UI Toolkit's own <see cref="ScrollView"/> and redirects
/// <see cref="contentContainer"/> into it, so anything added to an AoScrollView — in UXML or
/// code — lands in the scrolling area exactly as stock's child would.
///
/// Note the hierarchy: stock's <c>TextView_c</c> derives from this, which is why a plain text
/// label in AO scrolls without extra work.
/// </summary>
[UxmlElement]
public partial class AoScrollView : AoGuiControl
{
    readonly ScrollView _scroll;

    public override VisualElement contentContainer => _scroll?.contentContainer ?? this;

    /// <summary>The underlying UI Toolkit scroller, for callers that need its viewport.</summary>
    public ScrollView Scroller => _scroll;

    public AoScrollView()
    {
        AddToClassList("ao-scroll-view");
        _scroll = new ScrollView(ScrollViewMode.Vertical)
        {
            style = { flexGrow = 1f }
        };
        _scroll.AddToClassList("ao-scroll-view__scroller");
        hierarchy.Add(_scroll);
        ApplyScrollbarModes();
    }

    AoScrollbarMode _vertical = AoScrollbarMode.Auto;
    AoScrollbarMode _horizontal = AoScrollbarMode.Off;

    /// <summary>Stock <c>v_scrollbar_mode</c>.</summary>
    [UxmlAttribute("v_scrollbar_mode")]
    public AoScrollbarMode VerticalScrollbarMode
    {
        get => _vertical;
        set { _vertical = value; ApplyScrollbarModes(); }
    }

    /// <summary>Stock <c>h_scrollbar_mode</c>.</summary>
    [UxmlAttribute("h_scrollbar_mode")]
    public AoScrollbarMode HorizontalScrollbarMode
    {
        get => _horizontal;
        set { _horizontal = value; ApplyScrollbarModes(); }
    }

    /// <summary>Stock <c>scroll_client</c>: the name of the child the view scrolls.</summary>
    [UxmlAttribute("scroll_client")]
    public string ScrollClient { get; set; }

    /// <summary>Stock <c>label</c>: caption drawn with the scroll frame.</summary>
    [UxmlAttribute("label")]
    public string Label { get; set; }

    void ApplyScrollbarModes()
    {
        if (_scroll == null)
            return;

        _scroll.verticalScrollerVisibility = Map(_vertical);
        _scroll.horizontalScrollerVisibility = Map(_horizontal);
    }

    static ScrollerVisibility Map(AoScrollbarMode mode) => mode switch
    {
        AoScrollbarMode.On => ScrollerVisibility.AlwaysVisible,
        AoScrollbarMode.Off => ScrollerVisibility.Hidden,
        _ => ScrollerVisibility.Auto
    };
}

/// <summary>
/// Port of stock <c>&lt;ScrollViewChild&gt;</c> — the named body a ScrollView scrolls. It is a
/// plain container; it exists as its own element because stock's <c>scroll_client</c> refers
/// to it by name, and because the shipped XML nests it explicitly.
/// </summary>
[UxmlElement]
public partial class AoScrollViewChild : AoView
{
    public AoScrollViewChild() => AddToClassList("ao-scroll-view-child");
}

public enum AoScrollbarMode
{
    Auto,
    On,
    Off
}
