using UnityEngine.UIElements;

/// <summary>
/// Port of stock <c>BitmapView_c : GUIControl_c : View</c> (GUI.dll) — a single GUI image.
/// Stock addresses its art by numeric id rather than by name, so this carries
/// <c>bitmap_id</c> and leaves resolution to the visual pass; <see cref="UvgaImage"/> already
/// does name-based lookup and is the obvious backing once ids are mapped.
/// </summary>
[UxmlElement]
public partial class AoBitmapView : AoGuiControl
{
    public AoBitmapView() => AddToClassList("ao-bitmap-view");

    int _bitmapId;

    /// <summary>Stock <c>bitmap_id</c>: numeric GUI art id.</summary>
    [UxmlAttribute("bitmap_id")]
    public int BitmapId
    {
        get => _bitmapId;
        set
        {
            _bitmapId = value;
            BitmapIdChanged?.Invoke(value);
        }
    }

    /// <summary>Stock signals this through SignalTarget_c; here it is a plain event.</summary>
    public event System.Action<int> BitmapIdChanged;
}
