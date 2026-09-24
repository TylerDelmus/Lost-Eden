using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Port of stock <c>DockableView_c : View</c> (GUI.dll, vftable 101afd1c) — content that lives in
/// a tab of a dock area and can be dragged from one to another. <c>InventoryView_c</c> is one.
/// A drag carries its <see cref="ViewId"/> (the <c>view_id</c> key), and its title names the tab.
/// </summary>
[UxmlElement]
public partial class AoDockableView : AoView
{
    string _title = string.Empty;

    /// <summary>The title changed; a dock window follows it with the tab's label (1003c117).</summary>
    public event Action<AoDockableView> TitleChanged;

    public AoDockableView() => AddToClassList("ao-dockable-view");

    /// <summary>Stock's <c>view_id</c>: which view a drag carries, unique within its dock area.</summary>
    [UxmlAttribute("view_id")]
    public string ViewId { get; set; } = string.Empty;

    [UxmlAttribute("title")]
    public string Title
    {
        get => _title;
        set
        {
            _title = value ?? string.Empty;
            TitleChanged?.Invoke(this);
        }
    }

    /// <summary>The dock area this view is in, or null.</summary>
    public IAoDockArea DockArea { get; internal set; }

    /// <summary>
    /// An optional control for the window's header, shown while this view's tab is selected —
    /// the inventory's grid/list picker, for one. Ours: stock's header content is not read.
    /// </summary>
    public VisualElement HeaderAccessory { get; set; }

    /// <summary>
    /// Whether the window can be resized while this view is selected. Stock gives every view a
    /// <c>view_resize_mask</c> — some resize freely, some by steps, some not at all — whose bits
    /// are not recovered; this and <see cref="SnapSize"/> are ours.
    /// </summary>
    [UxmlAttribute("resizable")]
    public bool Resizable { get; set; } = true;

    /// <summary>
    /// Turns a size the window is being resized to into the size this view takes — a grid snaps
    /// to whole cells. Null takes the size as asked, to the pixel.
    /// </summary>
    public Func<Vector2, Vector2> SnapSize { get; set; }

    /// <summary>The smallest size a free resize goes to.</summary>
    public static readonly Vector2 MinimumSize = new(40f, 20f);

    /// <summary>Resizes the view, through <see cref="SnapSize"/>; returns the size it took.</summary>
    public Vector2 ResizeTo(Vector2 size)
    {
        Vector2 took = SnapSize != null ? SnapSize(size) : Vector2.Max(size, MinimumSize);
        style.width = took.x;
        style.height = took.y;
        return took;
    }
}

/// <summary>
/// Stock <c>DockArea_c</c> (GUI.dll, vftable 101afe54): an interface, four pure virtuals past the
/// destructor. <see cref="AoDockWindow"/> implements it as <c>DockWindow_c</c> does.
/// </summary>
public interface IAoDockArea
{
    /// <summary>The area's <c>dock_name</c>, by which a drag finds its source (1003aca6).</summary>
    string DockName { get; }

    IReadOnlyList<AoDockableView> Views { get; }

    /// <summary>Slot 2: dock a view at a tab index; past the end appends.</summary>
    void Dock(int index, AoDockableView view);

    /// <summary>Slot 3: take a view out. An area left empty goes away.</summary>
    void Undock(AoDockableView view);

    /// <summary>Slot 4: show the view's tab and bring the area to the front.</summary>
    void Activate(AoDockableView view);
}

/// <summary>
/// The owner of every dock area in one layer — stock's dock manager (reached through
/// <c>100393a1</c>; its find-or-create is <c>1003aca6</c>). Areas are found by name, and a new
/// one takes the first free <c>DockArea%u</c>, counting from 0.
///
/// <para>
/// It also runs the tab drag, because a drag crosses windows: stock's is a
/// <c>DragObject_c</c> of type <c>dockableview/view</c> carrying <c>dock_name</c>,
/// <c>view_id</c> and <c>view_index</c>.
/// </para>
/// </summary>
public sealed class AoDockManager
{
    public const string DragMimeType = "dockableview/view";

    readonly VisualElement _layer;
    readonly Dictionary<string, AoDockWindow> _areas = new();

    /// <summary>A dock window was created, or went away when its last view left.</summary>
    public event Action<AoDockWindow> WindowAdded;
    public event Action<AoDockWindow> WindowRemoved;

    /// <param name="layer">
    /// Where the windows go. Not an <see cref="AoView"/>: one of those puts absolutely placed
    /// children back into the flow unless it is stacked, and every dock window is placed absolutely.
    /// </param>
    public AoDockManager(VisualElement layer)
    {
        _layer = layer ?? throw new ArgumentNullException(nameof(layer));
        _layer.RegisterCallback<PointerDownEvent>(OnLayerPointerDown, TrickleDown.TrickleDown);
    }

    public VisualElement Layer => _layer;

    public AoDockWindow Find(string dockName) =>
        dockName != null && _areas.TryGetValue(dockName, out AoDockWindow area) ? area : null;

    /// <summary>A new, empty dock window with its top-left at <paramref name="position"/> (layer space).</summary>
    public AoDockWindow Create(Vector2 position)
    {
        string name;
        uint n = 0;
        do name = $"DockArea{n++}";
        while (_areas.ContainsKey(name));

        var window = new AoDockWindow(this, name);
        window.style.left = position.x;
        window.style.top = position.y;
        _areas[name] = window;
        _layer.Add(window);
        WindowAdded?.Invoke(window);
        return window;
    }

    internal void Remove(AoDockWindow window)
    {
        if (!_areas.Remove(window.DockName))
            return;

        window.RemoveFromHierarchy();
        WindowRemoved?.Invoke(window);
    }

    // ---- the tab drag -----------------------------------------------------------------------
    //
    // A right click on a tab picks it up: its label then follows the pointer with no button
    // held (the live game's behaviour), and the next click puts it down where the pointer is.
    // Clicks inside a window are caught here before anything under them sees them; the layer's
    // owner feeds the pointer (MoveDrag) and the clicks outside every window (ClickOutside).

    AoDockableView _dragView;
    AoDockWindow _dragSource;
    bool _dragCanTearOut;
    Vector2 _dragGrab;
    VisualElement _dragImage;
    int _dragFrame = -1;

    public bool IsDragging => _dragView != null;

    /// <summary>A tab was picked up or put down; while one is held the pointer is the UI's.</summary>
    public event Action<bool> DraggingChanged;

    /// <summary>
    /// Stock <c>DockWindow_c</c>'s begin-drag slot (1003c428): the drag image is the tab's label,
    /// held where it was grabbed. Tearing out into a new window is only armed when the source
    /// has more than one tab (1003c54b).
    /// </summary>
    public void BeginDrag(AoDockWindow source, AoDockableView view, VisualElement tab, Vector2 pointer)
    {
        CancelDrag();

        _dragFrame = Time.frameCount;
        _dragView = view;
        _dragSource = source;
        _dragCanTearOut = source.Views.Count > 1;

        Vector2 tabTopLeft = _layer.WorldToLocal(tab.worldBound.position);
        Vector2 local = _layer.WorldToLocal(pointer);
        _dragGrab = local - tabTopLeft;

        _dragImage = new AoLabel { RawText = view.Title, pickingMode = PickingMode.Ignore };
        _dragImage.AddToClassList("ao-dock-drag-image");
        _dragImage.style.position = Position.Absolute;
        _layer.Add(_dragImage);
        MoveDrag(pointer);
        DraggingChanged?.Invoke(true);
    }

    public void MoveDrag(Vector2 pointer)
    {
        if (_dragImage == null)
            return;

        Vector2 local = _layer.WorldToLocal(pointer) - _dragGrab;
        _dragImage.style.left = local.x;
        _dragImage.style.top = local.y;
    }

    /// <summary>
    /// The drop. Onto a window's tab bar: stock's drop slot (1003c5a9) — the view leaves its
    /// source and is docked before the tab under the pointer, or at the end; dropping a window's
    /// only tab back onto that window does nothing. Onto nothing: stock's fallback (1003b263) —
    /// a new window, the view's size, its top-left at the drop point, if tearing out was armed.
    /// </summary>
    public void EndDrag(Vector2 pointer)
    {
        AoDockableView view = _dragView;
        AoDockWindow source = _dragSource;
        bool canTearOut = _dragCanTearOut;
        CancelDrag();

        if (view == null || source == null || view.DockArea != source)
            return;

        AoDockWindow target = TabBarAt(pointer, out int dropIndex);
        if (target != null)
        {
            if (target == source && source.Views.Count == 1)
                return;

            source.Undock(view);

            // Stock reads the tab at the drop index after the view has left, so the index is
            // simply clamped to what is there now.
            target.Dock(Mathf.Min(dropIndex, target.Views.Count), view);
            return;
        }

        if (!canTearOut)
            return;

        // The window sizes to the view, so it keeps the view's size as stock's rect does.
        // Stock's fallback does not undock from the source itself (1003b263 docks straight into
        // the new area); here the view is taken out first so it is never in two areas.
        Vector2 at = _layer.WorldToLocal(pointer);
        source.Undock(view);
        Create(at).Dock(int.MaxValue, view);
    }

    public void CancelDrag()
    {
        bool wasDragging = _dragView != null;
        _dragImage?.RemoveFromHierarchy();
        _dragImage = null;
        _dragView = null;
        _dragSource = null;
        _dragCanTearOut = false;
        if (wasDragging)
            DraggingChanged?.Invoke(false);
    }

    /// <summary>A click outside every window puts the held tab down there — a tear-out, if armed.</summary>
    public void ClickOutside(Vector2 pointer)
    {
        if (IsDragging && Time.frameCount != _dragFrame)
            EndDrag(pointer);
    }

    // The click that puts a held tab down inside a window: nothing under it sees it.
    void OnLayerPointerDown(PointerDownEvent evt)
    {
        if (!IsDragging || Time.frameCount == _dragFrame)
            return;

        evt.StopPropagation();
        EndDrag(evt.position);
    }

    /// <summary>The frontmost window whose tab bar is under the pointer, and the tab index there.</summary>
    AoDockWindow TabBarAt(Vector2 pointer, out int index)
    {
        index = 0;
        for (int i = _layer.childCount - 1; i >= 0; i--)
        {
            if (_layer[i] is not AoDockWindow window)
                continue;

            if (window.TabBar.worldBound.Contains(pointer))
            {
                index = window.TabIndexAt(pointer);
                return window;
            }

            // A window in front hides the tab bars behind it.
            if (window.worldBound.Contains(pointer))
                return null;
        }

        return null;
    }
}

/// <summary>
/// Port of stock <c>DockWindow_c : Window, DockArea_c</c> (GUI.dll, type name
/// <c>DockTabbedWindow</c>) — a window whose tabs are dockable views.
///
/// <list type="bullet">
///   <item>Docking a view (1003c384) inserts its tab, selects it, and keeps the window's frame.</item>
///   <item>Undocking one (1003c2f9) deletes its tab; the window goes away with its last tab.</item>
///   <item>A tab's title follows its view's (1003c117).</item>
///   <item>Right-clicking a tab picks the view up and it follows the pointer; the next click
///     docks it on the tab bar under it, or tears it out into a new window
///     (<see cref="AoDockManager"/>). Left-dragging the tab bar moves the window.</item>
/// </list>
///
/// <para>
/// <b>Not read:</b> which mouse button stock's tab drag takes, and that it follows the pointer
/// without a button held (both as observed in the live game); what the window's close button does to its tabs (here it closes every view in it);
/// and which tab is selected after one is removed (here the one that took its place).
/// </para>
/// </summary>
public class AoDockWindow : AoView, IAoDockArea
{
    readonly AoDockManager _manager;
    readonly VisualElement _header;
    readonly VisualElement _tabBar;
    readonly VisualElement _accessory;
    readonly VisualElement _body;
    readonly List<AoDockableView> _views = new();
    readonly List<VisualElement> _tabs = new();
    int _selected = -1;

    /// <summary>The close button was pressed; the owner decides what closing its views means.</summary>
    public event Action<AoDockWindow> CloseRequested;

    public AoDockWindow(AoDockManager manager, string dockName)
    {
        _manager = manager;
        DockName = dockName;

        AddToClassList("ao-dock-window");
        style.position = Position.Absolute;
        pickingMode = PickingMode.Position;
        RegisterCallback<PointerDownEvent>(_ => BringToFront(), TrickleDown.TrickleDown);

        // The header: the tabs from the left, then the selected view's own control, then the
        // close button in the top-right corner.
        _header = new VisualElement { name = "header" };
        _header.AddToClassList("ao-dock-window__header");
        _header.style.flexDirection = FlexDirection.Row;
        _header.style.alignItems = Align.FlexEnd;
        Add(_header);

        _tabBar = new VisualElement { name = "tabs" };
        _tabBar.AddToClassList("ao-dock-window__tabs");
        _tabBar.style.flexDirection = FlexDirection.Row;
        _tabBar.style.flexGrow = 1f;
        _tabBar.style.flexShrink = 1f;
        _tabBar.style.minWidth = 0f;
        _tabBar.style.overflow = Overflow.Hidden;
        _header.Add(_tabBar);
        RegisterWindowMove(_tabBar);

        _accessory = new VisualElement { name = "accessory" };
        _accessory.AddToClassList("ao-dock-window__accessory");
        _accessory.style.flexDirection = FlexDirection.Row;
        _accessory.style.alignSelf = Align.Center;
        _header.Add(_accessory);

        // A small square button with a drawn X, as stock's header buttons are.
        var close = new Button(() => CloseRequested?.Invoke(this)) { name = "close", tabIndex = -1 };
        close.AddToClassList("ao-dock-window__close");
        close.style.alignItems = Align.Center;
        close.style.justifyContent = Justify.Center;
        close.Add(new AoCross());
        _header.Add(close);

        _body = new VisualElement { name = "body" };
        _body.AddToClassList("ao-dock-window__body");
        Add(_body);

        // The frame resizes the window from every edge and corner. Each resizes the selected
        // view, which may snap (a grid to whole cells) or refuse; the window follows the view's
        // size, and dragging the left or top edge keeps the opposite edge where it was.
        AddResizeEdge("resize-left", left: true);
        AddResizeEdge("resize-right", right: true);
        AddResizeEdge("resize-top", top: true);
        AddResizeEdge("resize-bottom", bottom: true);
        AddResizeEdge("resize-top-left", left: true, top: true);
        AddResizeEdge("resize-top-right", right: true, top: true);
        AddResizeEdge("resize-bottom-left", left: true, bottom: true);
        AddResizeEdge("resize-bottom-right", right: true, bottom: true);
    }

    // ---- resizing -----------------------------------------------------------------------------

    // How far into the window an edge can be grabbed, in pixels. Ours. The top band is thinner
    // so the tabs just under it keep their clicks.
    const float EdgeGrab = 5f;
    const float TopGrab = 3f;

    Vector2 _resizeStartPointer;
    Vector2 _resizeStartSize;
    Vector2 _resizeStartPosition;

    void AddResizeEdge(string name, bool left = false, bool right = false, bool top = false, bool bottom = false)
    {
        var edge = new VisualElement { name = name, pickingMode = PickingMode.Position };
        edge.AddToClassList("ao-dock-window__resize");
        edge.style.position = Position.Absolute;

        // Anchored sides sit 1px out, over the window's own 1px frame, so the frame line itself
        // can be grabbed.
        const float Outset = -1f;

        bool horizontal = left || right;
        bool vertical = top || bottom;
        float thickness = top ? TopGrab : EdgeGrab;

        if (horizontal && vertical)
        {
            // A corner: a grab-sized square, on top of the two edges it joins.
            if (left) edge.style.left = Outset; else edge.style.right = Outset;
            if (top) edge.style.top = Outset; else edge.style.bottom = Outset;
            edge.style.width = EdgeGrab;
            edge.style.height = EdgeGrab;
        }
        else if (horizontal)
        {
            if (left) edge.style.left = Outset; else edge.style.right = Outset;
            edge.style.top = EdgeGrab;
            edge.style.bottom = EdgeGrab;
            edge.style.width = EdgeGrab;
        }
        else
        {
            if (top) edge.style.top = Outset; else edge.style.bottom = Outset;
            edge.style.left = EdgeGrab;
            edge.style.right = EdgeGrab;
            edge.style.height = thickness;
        }

        hierarchy.Add(edge);

        edge.RegisterCallback<PointerDownEvent>(evt =>
        {
            AoDockableView view = SelectedView;
            if (evt.button != 0 || view == null || !view.Resizable)
                return;

            _resizeStartPointer = evt.position;
            _resizeStartSize = view.layout.size;
            _resizeStartPosition = new Vector2(resolvedStyle.left, resolvedStyle.top);
            edge.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        });

        edge.RegisterCallback<PointerMoveEvent>(evt =>
        {
            AoDockableView view = SelectedView;
            if (!edge.HasPointerCapture(evt.pointerId) || view == null)
                return;

            Vector2 delta = (Vector2)evt.position - _resizeStartPointer;
            var asked = new Vector2(
                _resizeStartSize.x + (right ? delta.x : left ? -delta.x : 0f),
                _resizeStartSize.y + (bottom ? delta.y : top ? -delta.y : 0f));

            Vector2 took = view.ResizeTo(asked);

            // From the left or the top, the opposite edge stays put.
            if (left)
                style.left = _resizeStartPosition.x + (_resizeStartSize.x - took.x);
            if (top)
                style.top = _resizeStartPosition.y + (_resizeStartSize.y - took.y);
        });

        edge.RegisterCallback<PointerUpEvent>(evt =>
        {
            if (evt.button == 0 && edge.HasPointerCapture(evt.pointerId))
                edge.ReleasePointer(evt.pointerId);
        });
    }

    public string DockName { get; }

    public IReadOnlyList<AoDockableView> Views => _views;

    public VisualElement TabBar => _tabBar;

    public AoDockableView SelectedView => _selected >= 0 && _selected < _views.Count ? _views[_selected] : null;

    // ---- DockArea_c ------------------------------------------------------------------------

    public void Dock(int index, AoDockableView view)
    {
        if (view == null || _views.Contains(view))
            return;

        index = Mathf.Clamp(index, 0, _views.Count);
        _views.Insert(index, view);

        VisualElement tab = CreateTab(view);
        _tabs.Insert(index, tab);
        _tabBar.Insert(index, tab);

        view.DockArea = this;
        view.TitleChanged += OnViewTitleChanged;
        Select(index);
    }

    public void Undock(AoDockableView view)
    {
        int index = _views.IndexOf(view);
        if (index < 0)
            return;

        view.TitleChanged -= OnViewTitleChanged;
        view.DockArea = null;
        view.RemoveFromHierarchy();

        _tabs[index].RemoveFromHierarchy();
        _tabs.RemoveAt(index);
        _views.RemoveAt(index);

        if (_views.Count == 0)
        {
            _selected = -1;
            _manager.Remove(this);
            return;
        }

        _selected = -1;
        Select(Mathf.Min(index, _views.Count - 1));
    }

    public void Activate(AoDockableView view)
    {
        int index = _views.IndexOf(view);
        if (index < 0)
            return;

        Select(index);
        BringToFront();
    }

    // ---- tabs ------------------------------------------------------------------------------

    public void Select(int index)
    {
        if (index < 0 || index >= _views.Count || index == _selected)
            return;

        _selected = index;
        _body.Clear();
        _body.Add(_views[index]);
        FollowWidthOf(_views[index]);

        _accessory.Clear();
        if (_views[index].HeaderAccessory != null)
            _accessory.Add(_views[index].HeaderAccessory);

        for (int i = 0; i < _tabs.Count; i++)
            _tabs[i].EnableInClassList("ao-dock-window__tab--selected", i == index);
    }

    // The window is exactly as wide as the selected view, frame included. It does not size to
    // its header: a long tab title would otherwise hold the window wider than a narrow view (a
    // one-column grid) and leave a strip beside it. The tabs shrink and cut their titles instead.
    AoDockableView _widthSource;

    void FollowWidthOf(AoDockableView view)
    {
        _widthSource?.UnregisterCallback<GeometryChangedEvent>(OnWidthSourceChanged);
        _widthSource = view;
        _widthSource?.RegisterCallback<GeometryChangedEvent>(OnWidthSourceChanged);
        MatchWidth();
    }

    void OnWidthSourceChanged(GeometryChangedEvent evt) => MatchWidth();

    void MatchWidth()
    {
        float width = _widthSource?.layout.width ?? float.NaN;
        if (float.IsNaN(width) || width <= 0f)
            return;

        style.width = width + resolvedStyle.borderLeftWidth + resolvedStyle.borderRightWidth;
    }

    /// <summary>The tab under a panel point, or the tab count past the last one (TabView's drop index).</summary>
    public int TabIndexAt(Vector2 pointer)
    {
        for (int i = 0; i < _tabs.Count; i++)
            if (_tabs[i].worldBound.Contains(pointer))
                return i;
        return _tabs.Count;
    }

    VisualElement CreateTab(AoDockableView view)
    {
        var tab = new AoLabel { RawText = view.Title, pickingMode = PickingMode.Position };
        tab.AddToClassList("ao-dock-window__tab");
        tab.style.flexShrink = 1f;
        tab.style.minWidth = 0f;

        tab.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button == 0)
                Select(_views.IndexOf(view));
            else if (evt.button == 1 && !_manager.IsDragging)
            {
                _manager.BeginDrag(this, view, tab, evt.position);
                evt.StopPropagation();
            }
        });

        return tab;
    }

    void OnViewTitleChanged(AoDockableView view)
    {
        int index = _views.IndexOf(view);
        if (index >= 0 && _tabs[index] is AoLabel label)
            label.RawText = view.Title;
    }

    // ---- moving the window -------------------------------------------------------------------

    Vector2 _moveStartPointer;
    Vector2 _moveStartPosition;

    void RegisterWindowMove(VisualElement handle)
    {
        handle.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button != 0)
                return;

            _moveStartPointer = evt.position;
            _moveStartPosition = new Vector2(resolvedStyle.left, resolvedStyle.top);
            handle.CapturePointer(evt.pointerId);
        });

        handle.RegisterCallback<PointerMoveEvent>(evt =>
        {
            if (!handle.HasPointerCapture(evt.pointerId))
                return;

            Vector2 delta = (Vector2)evt.position - _moveStartPointer;
            style.left = _moveStartPosition.x + delta.x;
            style.top = _moveStartPosition.y + delta.y;
        });

        handle.RegisterCallback<PointerUpEvent>(evt =>
        {
            if (evt.button == 0 && handle.HasPointerCapture(evt.pointerId))
                handle.ReleasePointer(evt.pointerId);
        });
    }
}
