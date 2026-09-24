/// <summary>
/// Routes <see cref="IGameHud"/> window intents from input to the views.
/// </summary>
public sealed class GameHud : IGameHud
{
    readonly InventoryWindowView _inventory;

    public GameHud(InventoryWindowView inventory)
    {
        _inventory = inventory;
    }

    public void Open(WindowId window)
    {
        if (window == WindowId.Inventory)
            _inventory?.Show();
    }

    public void Close(WindowId window)
    {
        if (window == WindowId.Inventory)
            _inventory?.Hide();
    }

    public void Toggle(WindowId window)
    {
        if (window == WindowId.Inventory)
            _inventory?.Toggle();
    }

    public bool IsOpen(WindowId window) =>
        window == WindowId.Inventory && _inventory != null && _inventory.IsVisible;
}
