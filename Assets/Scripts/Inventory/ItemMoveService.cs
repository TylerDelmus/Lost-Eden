using AOSharp.Common.GameData;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;

/// <summary>
/// The client's item requests to the server — what stock's GUI reaches through
/// <c>N3InterfaceModule_t</c> (Interfaces.dll) into <c>n3EngineClientAnarchy_t</c> (Gamecode.dll).
/// Items are addressed by their slot identity (<see cref="InventoryItem.Slot"/>).
///
/// <list type="bullet">
///   <item><see cref="MoveToInventory"/>: stock's <c>MoveItemToInventory(item)</c> (Interfaces
///     100081be) is <c>N3Msg_MoveItemToInventory(item, 1, 0x6f)</c> — page 1, slot 0x6f.</item>
///   <item><see cref="AddToContainer"/>: <c>N3Msg_ContainerAddItem(container, item)</c> (Gamecode
///     10028433) sends the container and the item.</item>
///   <item><see cref="Use"/>: <c>N3Msg_UseItem</c> (Gamecode 10028883); a backpack opens this way.</item>
/// </list>
///
/// <para>
/// <b>Not read:</b> the checks stock makes before sending (free slots, whether the item can be
/// taken off, whether it is already there — each with its own feedback text), which are the
/// server's to refuse here; and the message builders themselves (Gamecode 100151e7, 100153b3).
/// The wire layout is the protocol library's: ClientContainerAddItem, ClientMoveItemToInventory,
/// GenericCmdMessage. GenericCmd's first two fields (<c>Temp1</c>, <c>Count</c>) are sent as 1,
/// which is the library's convention, not a read value.
/// </para>
/// </summary>
public sealed class ItemMoveService
{
    /// <summary>Stock's "first free slot" (Interfaces 100081cb pushes 0x6f).</summary>
    public const int AnyFreeSlot = 0x6f;

    readonly NetworkClient _network;
    readonly PlayerController _player;

    public ItemMoveService(NetworkClient network, PlayerController player)
    {
        _network = network;
        _player = player;
    }

    public void MoveToInventory(InventoryItem item)
    {
        if (item == null)
            return;

        _network.Send(new ClientMoveItemToInventory { SourceContainer = item.Slot, Slot = AnyFreeSlot });
    }

    public void AddToContainer(Identity container, InventoryItem item)
    {
        if (item == null)
            return;

        _network.Send(new ClientContainerAddItem { Target = container, Source = item.Slot });
    }

    public void Use(InventoryItem item)
    {
        if (item == null || !_player.TryGetLocalPlayer(out Character player))
            return;

        _network.Send(new GenericCmdMessage
        {
            Temp1 = 1,
            Count = 1,
            Action = GenericCmdAction.Use,
            User = player.Identity,
            Target = item.Slot,
        });
    }
}
