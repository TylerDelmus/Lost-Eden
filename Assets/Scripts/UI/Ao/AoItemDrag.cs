using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// What an item drag carries — stock's <c>DragObject_c</c> of type <c>inventory/item</c>
/// (built at 1003d4a4, parsed at 1003d6f2): the source container, the item, and a split count
/// (0 moves the whole stack).
/// </summary>
public sealed class AoItemDragPayload
{
    public const string MimeType = "inventory/item";

    public AoItemContainerView Source;
    public AoMultiListViewItem Item;
    public Texture2D Icon;
    public int SplitCount;
}

/// <summary>An item put down over a container: onto an item there, or onto empty space.</summary>
public readonly struct AoItemDrop
{
    public readonly AoItemDragPayload Payload;
    public readonly AoItemContainerView Target;
    public readonly AoMultiListViewItem TargetItem;

    /// <summary>The button that put it down, numbered as UI Toolkit does (0 left, 1 right).</summary>
    public readonly int Button;

    public AoItemDrop(AoItemDragPayload payload, AoItemContainerView target, AoMultiListViewItem targetItem, int button)
    {
        Payload = payload;
        Target = target;
        TargetItem = targetItem;
        Button = button;
    }

    public bool SameContainer => Payload != null && Payload.Source == Target;
}

/// <summary>
/// The item on the cursor, for one layer of windows. A click on an item picks it up (stock's
/// list fires its pick-up on the press, 10041aa4); the icon then follows the pointer with no
/// button held, and the next left click puts it down: over a container, that container receives
/// the drop; anywhere else it goes back. A right click or Escape also puts it back.
///
/// <para>
/// The layer's owner feeds it the pointer each frame (<see cref="Follow"/>) and the clicks
/// that land outside every window (<see cref="ClickOutside"/>), since those never reach the
/// panel. Clicks inside a window are caught here before anything under them sees them.
/// </para>
///
/// <para>
/// <b>Not read:</b> stock's drag image and hot spot for an inventory item (its special-action
/// drag, 100721c1, uses a 31px icon held at 16,16), and what stock does with an item put down
/// over the world (<c>N3Msg_DropItem</c> exists; here it simply goes back).
/// </para>
/// </summary>
public sealed class AoItemDragController
{
    const float ImageSize = 48f;

    readonly VisualElement _layer;
    AoItemDragPayload _held;
    VisualElement _image;
    int _heldFrame = -1;

    /// <summary>An item was picked up or put down; while held, the pointer is the UI's.</summary>
    public event Action<bool> HoldingChanged;

    public AoItemDragController(VisualElement layer)
    {
        _layer = layer;
        _layer.RegisterCallback<PointerDownEvent>(OnLayerPointerDown, TrickleDown.TrickleDown);
    }

    public bool IsHolding => _held != null;

    /// <summary>Puts an item on the cursor.</summary>
    public void PickUp(AoItemDragPayload payload, Vector2 panelPoint)
    {
        PutBack();
        if (payload == null)
            return;

        _held = payload;
        _heldFrame = Time.frameCount;

        _image = new VisualElement { pickingMode = PickingMode.Ignore };
        _image.AddToClassList("ao-item-drag-image");
        _image.style.position = Position.Absolute;
        _image.style.width = ImageSize;
        _image.style.height = ImageSize;
        if (payload.Icon != null)
            _image.style.backgroundImage = new StyleBackground(payload.Icon);
        _layer.Add(_image);
        Follow(panelPoint);

        HoldingChanged?.Invoke(true);
    }

    /// <summary>Drops the held item without moving it.</summary>
    public void PutBack()
    {
        if (_held == null)
            return;

        _image?.RemoveFromHierarchy();
        _image = null;
        _held = null;
        HoldingChanged?.Invoke(false);
    }

    /// <summary>Moves the held item's icon to a panel point.</summary>
    public void Follow(Vector2 panelPoint)
    {
        if (_image == null)
            return;

        Vector2 local = _layer.WorldToLocal(panelPoint);
        _image.style.left = local.x - ImageSize * 0.5f;
        _image.style.top = local.y - ImageSize * 0.5f;
    }

    /// <summary>A click that landed outside every window: the held item goes back.</summary>
    public void ClickOutside()
    {
        if (_held != null && Time.frameCount != _heldFrame)
            PutBack();
    }

    /// <summary>Whether a panel point is over one of the layer's windows.</summary>
    public bool IsOverWindow(Vector2 panelPoint)
    {
        for (int i = 0; i < _layer.childCount; i++)
            if (_layer[i] != _image && _layer[i].worldBound.Contains(panelPoint))
                return true;
        return false;
    }

    // Any click inside a window while an item is held puts it down, and nothing under the
    // click sees it — not the item there (which would be picked up), not the tab bar.
    void OnLayerPointerDown(PointerDownEvent evt)
    {
        if (_held == null || Time.frameCount == _heldFrame)
            return;

        evt.StopPropagation();

        AoItemDragPayload payload = _held;
        PutBack();

        if (evt.button == 0)
            Drop(payload, evt.position, evt.button);
    }

    void Drop(AoItemDragPayload payload, Vector2 panelPoint, int button)
    {
        for (int i = _layer.childCount - 1; i >= 0; i--)
        {
            VisualElement window = _layer[i];
            if (!window.worldBound.Contains(panelPoint))
                continue;

            AoItemContainerView target = null;
            window.Query<AoItemContainerView>().ForEach(view =>
            {
                if (target == null && view.worldBound.Contains(panelPoint))
                    target = view;
            });

            target?.ReceiveDrop(new AoItemDrop(payload, target, target.List.ItemAt(panelPoint), button));
            return;
        }
    }
}
