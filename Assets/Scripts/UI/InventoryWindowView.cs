using System.Collections.Generic;
using AODB.Common.Enums;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.UIElements;
using Identity = AOSharp.Common.GameData.Identity;

/// <summary>
/// The player's containers on screen: the Inventory page (placements 0x40 onward) and every
/// backpack the server has sent the contents of, each in its own dockable view (stock's
/// <c>InventoryView_c</c> is a <c>DockableView_c</c>) holding an <see cref="AoInventoryViewBase"/>.
/// The inventory opens and closes through <see cref="IGameHud"/>; a backpack opens when the
/// server sends its contents. All of them refresh whenever <see cref="PlayerInventory"/> changes.
///
/// <para>
/// Items move between them by drag and drop, following stock's drop slot (100cc855): an item
/// dropped on another container goes to the player's inventory with
/// <c>MoveItemToInventory</c> when that container is the main inventory, and with
/// <c>ContainerAddItem</c> otherwise. A right press uses the item, which is how a backpack opens.
/// </para>
///
/// <para>
/// <b>Not ported</b> from that drop slot: splitting a stack (<c>SplitItem</c>), stacking onto
/// another item, using one item on another (a right-button drop), moving within a container
/// (the positional grid), and the refusal onto an item carrying stock's +0xf0 flag.
/// Items go in placement order; stock restores each item's grid position from its own config.
/// </para>
/// </summary>
[DisallowMultipleComponent]
public sealed class InventoryWindowView : MonoBehaviour
{
    const string ResourcePath = "UI/InventoryWindow";
    const string GridChoice = "Grid";
    const string ListChoice = "List";

    // Where windows first open, in the dock layer. Ours: the inventory near the right edge, and
    // each backpack stepped down and left from it.
    const float OpenFromRight = 420f;
    const float OpenTop = 160f;
    static readonly Vector2 BackpackStep = new(-40f, 40f);

    [Inject] PlayerController _playerController;
    [Inject] IconTextureCache _icons;
    [Inject] DockHostView _dockHost;
    [Inject] ItemMoveService _moves;

    /// <summary>One container on screen.</summary>
    sealed class Pane
    {
        public AoDockableView Dockable;
        public AoInventoryViewBase View;
        public AoDropdownMenu Mode;
        public ItemContainer Model;
        public bool IsMainInventory;
    }

    Pane _main;
    readonly Dictionary<Identity, Pane> _backpacks = new();
    PlayerInventory _subscribedInventory;
    bool _subscribed;

    public bool IsVisible => _main?.Dockable?.DockArea != null;

    void Start()
    {
        _main = CreatePane("inventory", "Inventory", isMainInventory: true);
        Subscribe();
        OnInventoryReady();
    }

    void OnDestroy()
    {
        Unsubscribe();
        if (_main != null)
            _dockHost?.Hide(_main.Dockable);
        foreach (Pane pane in _backpacks.Values)
            _dockHost?.Hide(pane.Dockable);
    }

    // ---- open / close --------------------------------------------------------------------

    public void Show()
    {
        if (_main == null || _dockHost == null)
            return;

        Refresh(_main);
        _dockHost.WhenReady(() => _dockHost.Show(_main.Dockable, MainOpenPosition()));
    }

    public void Hide() => _dockHost?.Hide(_main?.Dockable);

    public void Toggle()
    {
        if (IsVisible)
            Hide();
        else
            Show();
    }

    Vector2 MainOpenPosition()
    {
        float width = _dockHost.Manager.Layer.layout.width;
        float x = float.IsNaN(width) ? 0f : Mathf.Max(0f, width - OpenFromRight);
        return new Vector2(x, OpenTop);
    }

    // ---- panes -------------------------------------------------------------------------------

    Pane CreatePane(string viewId, string title, bool isMainInventory)
    {
        VisualTreeAsset template = UserInterface.LoadTemplate(ResourcePath);
        if (template == null)
        {
            Debug.LogError($"[Inventory] Missing VisualTreeAsset at Resources/{ResourcePath}");
            return null;
        }

        AoDockableView dockable = template.Instantiate().Q<AoDockableView>("inventory");
        if (dockable == null)
            return null;

        // A pane moves between windows on its own, so it carries its own layout sheet.
        UserInterface.EnsureStylesheet(dockable, ResourcePath);
        dockable.RemoveFromHierarchy();
        dockable.ViewId = viewId;
        dockable.Title = title;

        var pane = new Pane
        {
            Dockable = dockable,
            View = dockable.Q<AoInventoryViewBase>("inventory-view"),
            Mode = new AoDropdownMenu { name = "mode-dropdown" },
            IsMainInventory = isMainInventory,
        };

        // The grid/list picker sits in the window's header while this pane's tab is selected.
        pane.Mode.AppendItem(AoLayoutMode.Grid, GridChoice);
        pane.Mode.AppendItem(AoLayoutMode.List, ListChoice);
        pane.Mode.SelectByIndex(0);
        pane.Mode.SelectionChanged += index =>
            pane.View.List.LayoutMode = (AoLayoutMode)pane.Mode.GetItemID(index);
        dockable.HeaderAccessory = pane.Mode;

        // Resizing the window snaps the grid to whole cells.
        if (pane.View != null)
            dockable.SnapSize = size => pane.View.List.SnapViewSize(size);

        if (pane.View != null)
        {
            pane.View.ItemPickedUp += (item, evt) => BeginItemDrag(pane, item, evt);
            pane.View.ItemUseRequested += item => _moves?.Use(item.UserData as InventoryItem);
            pane.View.ItemDropped += drop => OnItemDropped(pane, drop);
        }

        return pane;
    }

    /// <summary>Rebuilds a pane's list from its container.</summary>
    void Refresh(Pane pane)
    {
        if (pane?.View == null)
            return;

        AoItemListViewBase list = pane.View.List;
        list.Clear();

        if (pane.IsMainInventory)
            pane.Model = _playerController != null ? _playerController.Inventory?.Inventory : null;

        if (pane.Model == null)
            return;

        var items = new List<InventoryItem>(pane.Model.Items);
        items.Sort((a, b) => a.Slot.Instance.CompareTo(b.Slot.Instance));

        foreach (InventoryItem entry in items)
        {
            Item item = entry.Item;
            if (item == null)
                continue;

            var row = new AoInventoryListViewItem(IconOf(item), item.Template?.Name, entry.Count, item.Quality)
            {
                UserData = entry
            };
            list.AddItem(row);
        }
    }

    void RefreshAll()
    {
        Refresh(_main);
        foreach (Pane pane in _backpacks.Values)
            Refresh(pane);
    }

    Texture2D IconOf(Item item) =>
        item != null && _icons != null && item.TryGetStat((int)StatId.icon, out int iconId)
            ? _icons.GetIcon(iconId)
            : null;

    // ---- backpacks -------------------------------------------------------------------------

    /// <summary>The server sent a container's contents: show it, opening a window for it if needed.</summary>
    void OnContainerOpened(ItemContainer container)
    {
        if (!_backpacks.TryGetValue(container.Identity, out Pane pane))
        {
            pane = CreatePane($"container:{container.Identity.Type}:{container.Identity.Instance}",
                              TitleOf(container.Identity), isMainInventory: false);
            if (pane == null)
                return;

            pane.Model = container;
            _backpacks[container.Identity] = pane;
        }

        pane.Model = container;
        Refresh(pane);

        _dockHost?.WhenReady(() =>
        {
            Vector2 at = MainOpenPosition() + BackpackStep * _backpacks.Count;
            _dockHost.Show(pane.Dockable, at);
        });
    }

    /// <summary>The backpack's own item name, found by its identity among the player's items.</summary>
    string TitleOf(Identity container)
    {
        PlayerInventory inventory = _playerController != null ? _playerController.Inventory : null;
        if (inventory != null)
        {
            foreach (ItemContainer page in new[] { inventory.Inventory, inventory.Equipment })
                foreach (InventoryItem item in page.Items)
                    if (item.UniqueIdentity == container && item.Item?.Template?.Name is string name)
                        return name;
        }

        return "Backpack";
    }

    // ---- drag and drop -----------------------------------------------------------------------

    void BeginItemDrag(Pane pane, AoMultiListViewItem item, PointerDownEvent evt)
    {
        if (_dockHost?.ItemDrag == null || item.UserData is not InventoryItem entry)
            return;

        if (_dockHost.ItemDrag.IsHolding)
            return;

        _dockHost.ItemDrag.PickUp(new AoItemDragPayload
        {
            Source = pane.View,
            Item = item,
            Icon = IconOf(entry.Item),
            SplitCount = 0,
        }, evt.position);
    }

    /// <summary>
    /// Stock's drop slot (100cc855), the parts that apply here: a left-button drop from another
    /// container moves the item in — to the player's inventory when this is the main inventory,
    /// into this container otherwise. Everything else is not ported (see the class summary).
    /// </summary>
    void OnItemDropped(Pane target, AoItemDrop drop)
    {
        if (_moves == null || drop.Payload?.Item?.UserData is not InventoryItem item)
            return;

        if (drop.Button != 0 || drop.SameContainer || drop.Payload.SplitCount > 0)
            return;

        if (target.IsMainInventory)
            _moves.MoveToInventory(item);
        else if (target.Model != null)
            _moves.AddToContainer(target.Model.Identity, item);
    }

    // ---- inventory arrival -------------------------------------------------------------------

    void Subscribe()
    {
        if (_subscribed || _playerController == null)
            return;

        _playerController.InventoryReady += OnInventoryReady;
        _subscribed = true;
    }

    void Unsubscribe()
    {
        if (_subscribed && _playerController != null)
            _playerController.InventoryReady -= OnInventoryReady;
        _subscribed = false;
        WatchInventory(null);
    }

    /// <summary>The player's inventory is a new object per character: follow the current one.</summary>
    void OnInventoryReady()
    {
        WatchInventory(_playerController != null ? _playerController.Inventory : null);
        RefreshAll();
    }

    void WatchInventory(PlayerInventory inventory)
    {
        if (_subscribedInventory == inventory)
            return;

        if (_subscribedInventory != null)
        {
            _subscribedInventory.Changed -= RefreshAll;
            _subscribedInventory.ContainerOpened -= OnContainerOpened;
        }

        _subscribedInventory = inventory;

        if (inventory != null)
        {
            inventory.Changed += RefreshAll;
            inventory.ContainerOpened += OnContainerOpened;
        }
    }
}
