using System;

/// <summary>
/// Routes window open/close/toggle intents to concrete views.
/// </summary>
public sealed class GameHud : IGameHud
{
    readonly InventoryView _inventory;

    public GameHud(InventoryView inventory)
    {
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
    }

    public void Open(WindowId window)
    {
        switch (window)
        {
            case WindowId.Inventory:
                _inventory.Show();
                break;
        }
    }

    public void Close(WindowId window)
    {
        switch (window)
        {
            case WindowId.Inventory:
                _inventory.Hide();
                break;
        }
    }

    public void Toggle(WindowId window)
    {
        switch (window)
        {
            case WindowId.Inventory:
                _inventory.Toggle();
                break;
        }
    }

    public bool IsOpen(WindowId window)
    {
        return window switch
        {
            WindowId.Inventory => _inventory.IsVisible,
            _ => false
        };
    }
}
