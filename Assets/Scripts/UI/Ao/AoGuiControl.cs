using UnityEngine.UIElements;

/// <summary>
/// Port of stock <c>GUIControl_c : View</c> (GUI.dll, vftable 101c600c, 62 virtuals).
///
/// This is the base for every *registered* control — the string table beside its vftable holds
/// the factory's type names (FTextButton, ComboBox, BitmapView, PowerBar, ScrollView,
/// ScrollViewChild …), so GUIControl_c is what makes a widget constructible by name from the
/// GUI XML. Plain layout boxes (BorderView_c, ItemSlotView_c, TextInputView_c, MultiListView_c)
/// derive straight from View instead and are *not* controls.
///
/// It adds two virtuals' worth of state over View: a source name and a "final" flag.
/// </summary>
[UxmlElement]
public partial class AoGuiControl : AoView
{
    public AoGuiControl() => AddToClassList("ao-gui-control");

    /// <summary>
    /// Stock <c>source_name</c>: the key this control binds to in the owning view's data,
    /// which is how stock wires a control to a DistributedValue_c without an explicit hookup.
    /// </summary>
    [UxmlAttribute("source_name")]
    public string SourceName { get; set; }

    /// <summary>
    /// Stock <c>final</c>: marks the control as not further overridable by a derived layout.
    /// Carried for fidelity; nothing consumes it yet.
    /// </summary>
    [UxmlAttribute("final")]
    public bool Final { get; set; }
}
