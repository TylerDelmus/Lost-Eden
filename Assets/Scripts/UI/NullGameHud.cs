/// <summary>
/// Placeholder <see cref="IGameHud"/> while the inventory window is rebuilt on the Ao* widgets.
///
/// The interface is the seam that keeps input from knowing about views, so it stays; only the
/// implementation went with the old inventory. Replace this with the real router once the new
/// window exists — <see cref="InputController"/> already calls Toggle(WindowId.Inventory).
/// </summary>
public sealed class NullGameHud : IGameHud
{
    public void Open(WindowId window) { }
    public void Close(WindowId window) { }
    public void Toggle(WindowId window) { }
    public bool IsOpen(WindowId window) => false;
}
