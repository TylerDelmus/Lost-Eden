using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Port of stock <c>View</c> (GUI.dll, vftable 101c8304, 60 virtuals) — the base every AO
/// widget derives from. Stock chain is <c>View : Handler, SignalTarget_c, BaseObject_c,
/// XMLObject_c</c>; only the <c>View</c> part is ported here:
///
///   Handler / Looper / Window  -> UI Toolkit's event dispatcher and PanelRenderer panel
///   SignalTarget_c / Slot0-2_c -> plain C# events on the derived classes
///   XMLObject_c                -> UXML + [UxmlAttribute]
///   LayoutNode / LayoutSpacer  -> flex-direction / flex-grow (see AoSpacer)
///
/// The attribute names below are stock's own, read verbatim from the string table directly
/// after View's vftable, so UXML authored here lines up 1:1 with the shipped Views/*.xml.
/// Visuals are deliberately absent — this carries structure, layout and state only.
/// </summary>
[UxmlElement]
public partial class AoView : VisualElement
{
    /// <summary>Stock sentinel for "resolve against my parent" in a size group owner.</summary>
    public const string ParentOwnerToken = ":our_parent:";

    static readonly Dictionary<string, List<AoView>> WidthGroups = new();
    static readonly Dictionary<string, List<AoView>> HeightGroups = new();

    AoViewLayout _viewLayout = AoViewLayout.Vertical;
    AoHAlign _hAlignment = AoHAlign.Fill;
    AoVAlign _vAlignment = AoVAlign.Fill;
    string _layoutBorders;
    string _minSize;
    string _maxSize;
    bool _useMaxSize = true;
    string _widthGroup;
    string _heightGroup;

    /// <summary>
    /// USS class every Ao* view carries. Each derived class adds its own block name as well
    /// (<c>ao-button</c>, <c>ao-text-view</c>, ...) and names its parts <c>block__part</c> and
    /// its states <c>block--state</c>. Those names are what a skin styles, so they are API.
    /// </summary>
    public const string UssClassName = "ao-view";

    public AoView()
    {
        AddToClassList(UssClassName);

        // Stock View is a plain box that lays its children out through a LayoutNode; the
        // default node is vertical.
        style.flexDirection = FlexDirection.Column;
        RegisterCallback<AttachToPanelEvent>(OnAttach);
        RegisterCallback<DetachFromPanelEvent>(OnDetach);
    }

    void OnAttach(AttachToPanelEvent evt)
    {
        JoinGroups();
        ApplyAlignment();

        // UXML assigns view_layout before the children exist, and a child cannot know its
        // parent's layout at construction, so stacking is settled from both ends on attach.
        // Doing it here as well as in the child covers children that are not AoViews.
        if (_viewLayout == AoViewLayout.Stacked)
            ApplyViewLayout();

        if (parent is AoView p)
            p.ApplyStackingTo(this);
    }

    void OnDetach(DetachFromPanelEvent evt) => LeaveGroups();

    // ---- stock: view_layout -------------------------------------------------------------

    /// <summary>
    /// Stock <c>view_layout</c>. Horizontal and vertical are the LayoutNode directions;
    /// <see cref="AoViewLayout.Stacked"/> is stock's third mode, where children are layered on
    /// top of each other instead of flowing. CharacterSelectionWindow and ControlCenter both
    /// use it to draw a bottom bar over a panel.
    /// </summary>
    [UxmlAttribute("view_layout")]
    public AoViewLayout ViewLayout
    {
        get => _viewLayout;
        set { _viewLayout = value; ApplyViewLayout(); }
    }

    void ApplyViewLayout()
    {
        style.flexDirection = _viewLayout == AoViewLayout.Horizontal ? FlexDirection.Row : FlexDirection.Column;

        // Stacked children overlap, so each is taken out of the flow and pinned to the box.
        for (int i = 0; i < childCount; i++)
            ApplyStackingTo(this[i]);
    }

    void ApplyStackingTo(VisualElement child)
    {
        if (_viewLayout == AoViewLayout.Stacked)
        {
            child.style.position = Position.Absolute;
            child.style.left = 0f;
            child.style.top = 0f;
            child.style.right = 0f;
            child.style.bottom = 0f;
        }
        else if (child.style.position.value == Position.Absolute)
        {
            child.style.position = StyleKeyword.Null;
            child.style.left = StyleKeyword.Null;
            child.style.top = StyleKeyword.Null;
            child.style.right = StyleKeyword.Null;
            child.style.bottom = StyleKeyword.Null;
        }
    }

    // ---- stock: h_alignment / v_alignment -----------------------------------------------

    [UxmlAttribute("h_alignment")]
    public AoHAlign HAlignment
    {
        get => _hAlignment;
        set { _hAlignment = value; ApplyAlignment(); }
    }

    [UxmlAttribute("v_alignment")]
    public AoVAlign VAlignment
    {
        get => _vAlignment;
        set { _vAlignment = value; ApplyAlignment(); }
    }

    /// <summary>
    /// Stock aligns a child inside its LayoutNode along both axes. In flex terms the cross
    /// axis is align-self; the main axis is stock's own job for the spacers (HLayoutSpacer /
    /// VLayoutSpacer), so only the cross axis is set here.
    /// </summary>
    void ApplyAlignment()
    {
        bool parentIsRow = parent is AoView pv && pv.ViewLayout == AoViewLayout.Horizontal;

        // Fill means "no explicit alignment", so leave alignSelf unset rather than writing
        // Stretch: an inline style beats USS, and writing it would override a stylesheet's
        // align-items on the parent — which is how the login panels centre.
        if (parentIsRow)
            style.alignSelf = _vAlignment switch
            {
                AoVAlign.Top => new StyleEnum<Align>(Align.FlexStart),
                AoVAlign.Middle => new StyleEnum<Align>(Align.Center),
                AoVAlign.Bottom => new StyleEnum<Align>(Align.FlexEnd),
                _ => new StyleEnum<Align>(StyleKeyword.Null)
            };
        else
            style.alignSelf = _hAlignment switch
            {
                AoHAlign.Left => new StyleEnum<Align>(Align.FlexStart),
                AoHAlign.Center => new StyleEnum<Align>(Align.Center),
                AoHAlign.Right => new StyleEnum<Align>(Align.FlexEnd),
                _ => new StyleEnum<Align>(StyleKeyword.Null)
            };
    }

    // ---- stock: layout_borders ----------------------------------------------------------

    /// <summary>
    /// Stock <c>layout_borders="Rect(l,t,r,b)"</c> — the gap the LayoutNode leaves around
    /// the view. That is spacing outside the view's own box, so it maps to margin.
    /// </summary>
    [UxmlAttribute("layout_borders")]
    public string LayoutBorders
    {
        get => _layoutBorders;
        set
        {
            _layoutBorders = value;
            if (!AoGeometry.TryParseRect(value, out float l, out float t, out float r, out float b))
                return;

            style.marginLeft = l;
            style.marginTop = t;
            style.marginRight = r;
            style.marginBottom = b;
        }
    }

    // ---- stock: min_size / max_size / max_size_limit / use_max_size ----------------------

    /// <summary>
    /// Stock <c>min_size="Point(w,h)"</c>. A negative component is stock's "unconstrained"
    /// sentinel — <c>Point(70,-1)</c> means "70 wide, height free" — so it must be left unset
    /// rather than written through as -1.
    /// </summary>
    [UxmlAttribute("min_size")]
    public string MinSize
    {
        get => _minSize;
        set
        {
            _minSize = value;
            if (!AoGeometry.TryParsePoint(value, out float x, out float y))
                return;

            style.minWidth = AoGeometry.Length(x);
            style.minHeight = AoGeometry.Length(y);
        }
    }

    [UxmlAttribute("max_size")]
    public string MaxSize
    {
        get => _maxSize;
        set { _maxSize = value; ApplyMaxSize(); }
    }

    /// <summary>Stock <c>use_max_size</c>: when false the max_size is carried but not applied.</summary>
    [UxmlAttribute("use_max_size")]
    public bool UseMaxSize
    {
        get => _useMaxSize;
        set { _useMaxSize = value; ApplyMaxSize(); }
    }

    void ApplyMaxSize()
    {
        if (!_useMaxSize)
        {
            style.maxWidth = StyleKeyword.Null;
            style.maxHeight = StyleKeyword.Null;
            return;
        }

        if (!AoGeometry.TryParsePoint(_maxSize, out float x, out float y))
            return;

        style.maxWidth = AoGeometry.Length(x);
        style.maxHeight = AoGeometry.Length(y);
    }

    /// <summary>Stock <c>max_size_limit</c>: a ceiling the view will not grow past even in a size group.</summary>
    [UxmlAttribute("max_size_limit")]
    public string MaxSizeLimit { get; set; }

    // ---- stock: width_group / height_group ----------------------------------------------
    //
    // Stock's shared-sizing mechanism: every view naming the same group is measured and the
    // widest (tallest) wins for all of them. `*_group_owner` scopes the group, with
    // ":our_parent:" meaning the immediate parent. Used throughout the shipped XML to line
    // up button columns.

    [UxmlAttribute("width_group")]
    public string WidthGroup
    {
        get => _widthGroup;
        set { LeaveGroups(); _widthGroup = value; JoinGroups(); }
    }

    [UxmlAttribute("height_group")]
    public string HeightGroup
    {
        get => _heightGroup;
        set { LeaveGroups(); _heightGroup = value; JoinGroups(); }
    }

    [UxmlAttribute("width_group_owner")]
    public string WidthGroupOwner { get; set; }

    [UxmlAttribute("height_group_owner")]
    public string HeightGroupOwner { get; set; }

    string WidthGroupKey => _widthGroup == null ? null : (WidthGroupOwner ?? string.Empty) + "/" + _widthGroup;
    string HeightGroupKey => _heightGroup == null ? null : (HeightGroupOwner ?? string.Empty) + "/" + _heightGroup;

    void JoinGroups()
    {
        Register(WidthGroups, WidthGroupKey);
        Register(HeightGroups, HeightGroupKey);
        UpdateGeometryHook();
    }

    void LeaveGroups()
    {
        Unregister(WidthGroups, WidthGroupKey);
        Unregister(HeightGroups, HeightGroupKey);
    }

    void Register(Dictionary<string, List<AoView>> groups, string key)
    {
        if (string.IsNullOrEmpty(key))
            return;

        if (!groups.TryGetValue(key, out List<AoView> members))
        {
            members = new List<AoView>();
            groups[key] = members;
        }

        if (!members.Contains(this))
            members.Add(this);
    }

    void Unregister(Dictionary<string, List<AoView>> groups, string key)
    {
        if (string.IsNullOrEmpty(key) || !groups.TryGetValue(key, out List<AoView> members))
            return;

        members.Remove(this);
        if (members.Count == 0)
            groups.Remove(key);
    }

    /// <summary>
    /// The group tables are static, so with Reload Domain off they would otherwise carry
    /// views from the previous play session into the next one. VisualElement is not a
    /// UnityEngine.Object, so those stale entries do not even read as null — they simply
    /// accumulate and skew the measured maximum. Same trap as the PanelSettings cache in
    /// UserInterface; see Docs/UI.md.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStaticState()
    {
        WidthGroups.Clear();
        HeightGroups.Clear();
        _resolving = false;
    }

    /// <summary>
    /// Applies stock's shared sizing: every member of a group takes the largest resolved
    /// size in that group. Call once layout has resolved (from a GeometryChangedEvent on the
    /// panel root), not during construction — stock does the same on its layout pass.
    /// </summary>
    public static void ResolveSizeGroups()
    {
        // Assigning a width re-runs layout, which fires GeometryChangedEvent, which asks for
        // another resolve — so guard re-entry and only write when the value actually moves.
        if (_resolving)
            return;

        _resolving = true;
        try
        {
            foreach (KeyValuePair<string, List<AoView>> group in WidthGroups)
            {
                float widest = 0f;
                foreach (AoView v in group.Value)
                    widest = Mathf.Max(widest, v.resolvedStyle.width);

                if (widest <= 0f)
                    continue;

                foreach (AoView v in group.Value)
                    if (Mathf.Abs(v.resolvedStyle.width - widest) > 0.5f)
                        v.style.width = widest;
            }

            foreach (KeyValuePair<string, List<AoView>> group in HeightGroups)
            {
                float tallest = 0f;
                foreach (AoView v in group.Value)
                    tallest = Mathf.Max(tallest, v.resolvedStyle.height);

                if (tallest <= 0f)
                    continue;

                foreach (AoView v in group.Value)
                    if (Mathf.Abs(v.resolvedStyle.height - tallest) > 0.5f)
                        v.style.height = tallest;
            }
        }
        finally
        {
            _resolving = false;
        }
    }

    static bool _resolving;

    /// <summary>
    /// Stock resolves size groups on its layout pass. The equivalent hook here is the group
    /// members' own geometry, so a view in a group listens for layout and asks for a resolve.
    /// Without this the attributes parsed and registered but never did anything.
    /// </summary>
    void OnGeometryChanged(GeometryChangedEvent evt) => ResolveSizeGroups();

    void UpdateGeometryHook()
    {
        bool inGroup = !string.IsNullOrEmpty(_widthGroup) || !string.IsNullOrEmpty(_heightGroup);
        UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        if (inGroup)
            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
    }

    // ---- stock: view_flags / view_resize_mask / fade_group / view_enable_expression -------

    /// <summary>Stock <c>view_flags</c>, kept raw until the individual bits are recovered.</summary>
    [UxmlAttribute("view_flags")]
    public int ViewFlags { get; set; }

    /// <summary>Stock <c>view_resize_mask</c>: which edges follow the parent on resize.</summary>
    [UxmlAttribute("view_resize_mask")]
    public int ViewResizeMask { get; set; }

    /// <summary>Stock <c>fade_group</c>: views sharing a name fade together.</summary>
    [UxmlAttribute("fade_group")]
    public string FadeGroup { get; set; }

    /// <summary>
    /// Stock <c>activate_criteria</c>: a CriteriaMonitor_c expression gating the view on game
    /// state. Stored verbatim; evaluation is not ported yet.
    /// </summary>
    [UxmlAttribute("activate_criteria")]
    public string ActivateCriteria { get; set; }

    /// <summary>
    /// Stock <c>view_enable_expression</c>: an ExpressionMonitor_c string that enables or
    /// disables the view from game state. Stored verbatim; evaluation is not ported yet.
    /// </summary>
    [UxmlAttribute("view_enable_expression")]
    public string EnableExpression { get; set; }

    // ---- stock: font -------------------------------------------------------------------

    /// <summary>
    /// Stock's <c>font</c> attribute names a GUI font key (<c>NORMAL</c>, <c>LARGE</c>,
    /// <c>HUGE</c>, <c>CC17</c> ...). It becomes the class <c>ao-font--large</c> and so on, and
    /// the skin decides which face and size each key draws with.
    /// </summary>
    protected static void SwapFontClass(VisualElement element, string oldKey, string newKey)
    {
        if (!string.IsNullOrEmpty(oldKey))
            element.RemoveFromClassList(FontClass(oldKey));
        if (!string.IsNullOrEmpty(newKey))
            element.AddToClassList(FontClass(newKey));
    }

    static string FontClass(string key) => "ao-font--" + key.Trim().ToLowerInvariant();

    // ---- visibility ---------------------------------------------------------------------

    public bool IsShown
    {
        get => style.display.value != DisplayStyle.None;
        set
        {
            style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
            pickingMode = value ? PickingMode.Position : PickingMode.Ignore;
        }
    }
}

public enum AoViewLayout
{
    Vertical,
    Horizontal,

    /// <summary>Stock's third mode: children are layered over one another, not flowed.</summary>
    Stacked
}

public enum AoHAlign
{
    Fill,
    Left,
    Center,
    Right
}

public enum AoVAlign
{
    Fill,
    Top,
    Middle,
    Bottom
}
