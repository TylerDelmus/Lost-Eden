public enum WindowId
{
    Inventory
}

/// <summary>
/// Narrow facade for opening/closing HUD windows from input. Views stay UI-side.
/// </summary>
public interface IGameHud
{
    void Open(WindowId window);
    void Close(WindowId window);
    void Toggle(WindowId window);
    bool IsOpen(WindowId window);
}
