using System;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// The panel every dock window lives in, and its <see cref="AoDockManager"/>. Views are shown
/// through <see cref="Show"/> and <see cref="Hide"/>; where they end up after that — which
/// window, which tab — is the player's doing.
/// </summary>
[DisallowMultipleComponent]
public sealed class DockHostView : MonoBehaviour
{
    const string ResourcePath = "UI/DockHost";
    const int SortOrder = 90;

    [Inject] IUINotifyService _uiNotify;

    UiMenu _menu;
    AoDockManager _manager;
    AoItemDragController _itemDrag;
    Action _onReady;

    public AoDockManager Manager => _manager;

    /// <summary>Item drags between the windows in this layer.</summary>
    public AoItemDragController ItemDrag => _itemDrag;

    void Awake()
    {
        _menu = UserInterface.Load(this, ResourcePath, SortOrder, startVisible: true, logName: "DockHost",
                                   centerPanelRoot: false);
        _menu?.WhenReady(OnMenuReady);
    }

    void OnDestroy()
    {
        _uiNotify?.RemoveHitTest(IsOverUI);
        _itemDrag?.PutBack();
        UserInterface.Unregister(_menu);
        _menu = null;
    }

    // A held item or tab follows the pointer even over the world, where the panel gets no
    // events. A click there puts it down: an item goes back, a tab tears out into a new window
    // (when that is armed). Escape puts either back.
    void Update()
    {
        if (_manager == null)
            return;

        bool holdingItem = _itemDrag != null && _itemDrag.IsHolding;
        bool holdingTab = _manager.IsDragging;
        if (!holdingItem && !holdingTab)
            return;

        IPanel panel = _manager.Layer.panel;
        Mouse mouse = Mouse.current;
        if (panel == null || mouse == null)
            return;

        Vector2 screen = mouse.position.ReadValue();
        Vector2 panelPoint = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screen.x, Screen.height - screen.y));
        bool clicked = mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame;
        bool escape = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;

        if (holdingItem)
        {
            _itemDrag.Follow(panelPoint);
            if (clicked && !_itemDrag.IsOverWindow(panelPoint))
                _itemDrag.ClickOutside();
            if (escape)
                _itemDrag.PutBack();
        }

        if (holdingTab)
        {
            _manager.MoveDrag(panelPoint);
            if (clicked && !_itemDrag.IsOverWindow(panelPoint))
                _manager.ClickOutside(panelPoint);
            if (escape)
                _manager.CancelDrag();
        }
    }

    void OnMenuReady(VisualElement root)
    {
        _manager = new AoDockManager(root);
        _itemDrag = new AoItemDragController(root);
        // While an item or a tab is on the cursor the pointer is the UI's everywhere, so the
        // camera and the world leave clicks alone.
        void OnHolding(bool holding)
        {
            if (holding)
                _uiNotify?.NotifyDragStart(root);
            else if (!_itemDrag.IsHolding && !_manager.IsDragging)
                _uiNotify?.NotifyDragEnd(root);
        }

        _itemDrag.HoldingChanged += OnHolding;
        _manager.DraggingChanged += OnHolding;
        _manager.WindowAdded += OnWindowAdded;
        _uiNotify?.AddHitTest(IsOverUI);

        Action callbacks = _onReady;
        _onReady = null;
        callbacks?.Invoke();
    }

    /// <summary>Runs <paramref name="onReady"/> once the layer exists.</summary>
    public void WhenReady(Action onReady)
    {
        if (_manager != null)
            onReady?.Invoke();
        else
            _onReady += onReady;
    }

    /// <summary>
    /// Brings a view forward, opening a window for it at <paramref name="position"/> if it is
    /// not docked anywhere.
    /// </summary>
    public void Show(AoDockableView view, Vector2 position)
    {
        if (_manager == null || view == null)
            return;

        if (view.DockArea != null)
        {
            view.DockArea.Activate(view);
            return;
        }

        _manager.Create(position).Dock(int.MaxValue, view);
    }

    /// <summary>Takes a view out of whatever window holds it.</summary>
    public void Hide(AoDockableView view) => view?.DockArea?.Undock(view);

    void OnWindowAdded(AoDockWindow window) => window.CloseRequested += CloseWindow;

    // While the pointer is over anything this panel can pick — a window, its resize edges, a
    // dropdown's popup on the panel root — clicks and the wheel are the UI's, not the world's.
    // The layer and the full-screen containers above it are click-through, so they don't count.
    bool IsOverUI(Vector2 screen)
    {
        VisualElement layer = _manager?.Layer;
        IPanel panel = layer?.panel;
        if (panel == null)
            return false;

        Vector2 panelPoint = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screen.x, Screen.height - screen.y));
        VisualElement picked = panel.Pick(panelPoint);
        return picked != null && picked != layer && !picked.Contains(layer);
    }

    /// <summary>The window's close button closes every view in it.</summary>
    static void CloseWindow(AoDockWindow window)
    {
        for (int i = window.Views.Count - 1; i >= 0; i--)
            window.Undock(window.Views[i]);
    }
}
