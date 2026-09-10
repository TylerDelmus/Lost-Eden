using Reflex.Attributes;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Inventory window: AO chrome shell + <see cref="ItemContainerGridView"/>.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(UIDocument))]
public class InventoryView : MonoBehaviour
{
    const string ContentResourcePath = "UI/ItemContainerGrid";
    const int SortOrder = 90;
    const int DefaultInventoryCapacity = 30;

    [Inject] IconTextureCache _icons;
    [Inject] PlayerController _playerController;

    UiWindow _window;
    ItemContainerGridView _grid;
    bool _subscribed;

    public bool IsReady => _window != null;

    public bool IsVisible => _window != null && _window.IsVisible;

    void Start()
    {
        EnsureLoaded();
        Subscribe();
    }

    void OnDestroy()
    {
        Unsubscribe();
        if (_window != null)
            UserInterface.Unregister(_window);
        _window = null;
        _grid = null;
    }

    public void Show()
    {
        EnsureLoaded();
        RefreshFromInventory();
        _window?.Show();
        UvgaTextureSource.RaiseChanged();
    }

    public void Hide()
    {
        _window?.Hide();
    }

    public void Toggle()
    {
        if (IsVisible)
            Hide();
        else
            Show();
    }

    public void RefreshFromInventory()
    {
        if (_grid == null)
            return;

        ItemContainer container = _playerController != null ? _playerController.Inventory?.Inventory : null;
        if (container != null)
        {
            if (_grid.Capacity != container.Capacity)
                _grid.Build(container.Capacity, _icons);
            _grid.Bind(container);
        }
        else
        {
            _grid.Clear();
        }
    }

    void Subscribe()
    {
        if (_subscribed || _playerController == null)
            return;

        _playerController.InventoryReady += OnInventoryReady;
        _subscribed = true;

        // Player may already be loaded if this view was created late.
        if (_playerController.Inventory != null)
            OnInventoryReady();
    }

    void Unsubscribe()
    {
        if (!_subscribed || _playerController == null)
            return;

        _playerController.InventoryReady -= OnInventoryReady;
        _subscribed = false;
    }

    void OnInventoryReady()
    {
        EnsureLoaded();
        RefreshFromInventory();
    }

    void EnsureLoaded()
    {
        if (_window != null)
            return;

        _window = UserInterface.LoadWindow(
            this,
            title: "Inventory",
            contentUxmlResourcePath: ContentResourcePath,
            sortOrder: SortOrder,
            startVisible: false,
            logName: "Inventory");

        if (_window == null)
            return;

        _grid = new ItemContainerGridView(_window.ContentRoot);
        int capacity = _playerController?.Inventory?.Inventory?.Capacity ?? DefaultInventoryCapacity;
        _grid.Build(capacity, _icons);
    }
}
