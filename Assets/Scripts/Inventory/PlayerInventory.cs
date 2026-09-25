using System;
using System.Collections.Generic;
using AOSharp.Common.GameData;
using SmokeLounge.AOtomation.Messaging.GameData;
using UnityEngine;

public sealed class PlayerInventory
{
    readonly Dictionary<IdentityType, ItemContainer> _containers =
        new Dictionary<IdentityType, ItemContainer>();

    public ItemContainer Inventory => _containers[IdentityType.Inventory];
    public ItemContainer Equipment => _containers[IdentityType.WeaponPage];
    public ItemContainer Armor => _containers[IdentityType.ArmorPage];
    public ItemContainer Implant => _containers[IdentityType.ImplantPage];
    public ItemContainer Social => _containers[IdentityType.SocialPage];

    // Containers the server has sent the contents of — backpacks opened, in stock's terms
    // (InventoryUpdateMessage) — keyed by the container's identity, with the handle their
    // items' identities are built from.
    readonly Dictionary<Identity, ItemContainer> _opened = new Dictionary<Identity, ItemContainer>();
    readonly Dictionary<Identity, int> _openedHandles = new Dictionary<Identity, int>();

    /// <summary>The items moved, or a container's contents arrived.</summary>
    public event Action Changed;

    /// <summary>A container's contents arrived — a backpack was opened.</summary>
    public event Action<ItemContainer> ContainerOpened;

    public IReadOnlyDictionary<Identity, ItemContainer> OpenContainers => _opened;

    public PlayerInventory(int localPlayerId)
    {
        AddPage(IdentityType.Inventory, localPlayerId, 0x40, 30);
        AddPage(IdentityType.WeaponPage, localPlayerId, 0x00, 16);
        AddPage(IdentityType.ArmorPage, localPlayerId, 0x11, 15);
        AddPage(IdentityType.ImplantPage, localPlayerId, 0x21, 15);
        AddPage(IdentityType.SocialPage, localPlayerId, 0x31, 15);
    }

    void AddPage(IdentityType type, int localPlayerId, int offset, int capacity)
    {
        _containers[type] = new ItemContainer(new Identity(type, localPlayerId), offset, capacity)
        {
            Flags = ContainerFlags.CanAdd | ContainerFlags.CanRemove,
        };
    }

    public bool TryGet(IdentityType type, out ItemContainer container)
        => _containers.TryGetValue(type, out container);

    public bool TryGetItem(Identity slot, out InventoryItem item)
    {
        item = null;
        if (!_containers.TryGetValue(slot.Type, out ItemContainer container))
            return false;

        for (int i = 0; i < container.Items.Count; i++)
        {
            if (container.Items[i].Slot == slot)
            {
                item = container.Items[i];
                return true;
            }
        }

        return false;
    }

    public bool TryGetByPlacement(int placement, out InventoryItem item)
    {
        item = null;
        if (!TryFindContainer(placement, out ItemContainer container))
            return false;

        for (int i = 0; i < container.Items.Count; i++)
        {
            if (container.Items[i].Slot.Instance == placement)
            {
                item = container.Items[i];
                return true;
            }
        }

        return false;
    }

    public void Apply(InventorySlot[] slots, ItemTemplateCache templates)
    {
        if (templates == null)
            throw new ArgumentNullException(nameof(templates));

        foreach (ItemContainer container in _containers.Values)
            container.Clear();

        if (slots == null || slots.Length == 0)
            return;

        for (int i = 0; i < slots.Length; i++)
        {
            InventorySlot slot = slots[i];
            if (slot == null)
                continue;

            if (!TryFindContainer(slot.Placement, out ItemContainer container))
            {
                Debug.LogWarning(
                    $"[PlayerInventory] No container for placement 0x{slot.Placement:X} " +
                    $"(item {slot.ItemLowId}/{slot.ItemHighId} QL{slot.Quality})");
                continue;
            }

            Item item;
            try
            {
                item = templates.GetItem(slot.ItemLowId, slot.ItemHighId, slot.Quality);
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[PlayerInventory] Failed to resolve item {slot.ItemLowId}/{slot.ItemHighId} " +
                    $"QL{slot.Quality}: {ex.Message}");
                continue;
            }

            var inventoryItem = new InventoryItem(
                new Identity(container.Identity.Type, slot.Placement),
                slot.Identity,
                slot.Flags,
                slot.Count,
                item);

            container.Add(inventoryItem);
        }
    }

    // ---- containers and moves ---------------------------------------------------------------

    /// <summary>
    /// An item inside a backpack is addressed as <c>(Backpack, handle &lt;&lt; 16 | placement)</c>.
    /// The low half is grounded: stock's <c>N3Msg_ContainerAddItem</c> (Gamecode 10028558) takes
    /// a Backpack item's slot as <c>instance &amp; 0xffff</c>. That the high half is the
    /// <c>Handle</c> of the container's InventoryUpdateMessage is inferred, not read.
    /// </summary>
    public static Identity BackpackItemIdentity(int handle, int placement)
        => new Identity(IdentityType.Backpack, (handle << 16) | (placement & 0xffff));

    /// <summary>
    /// A container's contents from the server (InventoryUpdateMessage): replaces what was known
    /// of it, and raises <see cref="ContainerOpened"/>.
    /// </summary>
    public ItemContainer ApplyContainerContents(Identity container, int handle, InventorySlot[] slots,
                                                ItemTemplateCache templates)
    {
        if (!_opened.TryGetValue(container, out ItemContainer bag))
        {
            bag = new ItemContainer(container, 0, 0x10000)
            {
                Flags = ContainerFlags.CanAdd | ContainerFlags.CanRemove,
            };
            _opened[container] = bag;
        }

        _openedHandles[container] = handle;
        bag.Clear();

        if (slots != null)
        {
            foreach (InventorySlot slot in slots)
            {
                if (slot == null)
                    continue;

                Item item = ResolveItem(slot, templates);
                if (item != null)
                    bag.Add(new InventoryItem(BackpackItemIdentity(handle, slot.Placement), slot.Identity,
                                              slot.Flags, slot.Count, item));
            }
        }

        ContainerOpened?.Invoke(bag);
        Changed?.Invoke();
        return bag;
    }

    /// <summary>
    /// The server's ContainerAddItem: the item at <paramref name="source"/> is now in
    /// <paramref name="target"/> at <paramref name="slot"/>. The item leaves whichever known
    /// container held it; it is placed only if the target is one of the player's pages or an
    /// opened container. Returns whether anything changed.
    /// </summary>
    public bool ApplyContainerAddItem(Identity source, Identity target, int slot)
    {
        if (!TryFindAnywhere(source, out ItemContainer from, out InventoryItem item))
            return false;

        from.Remove(item);

        ItemContainer to = null;
        Identity newSlot = default;
        foreach (ItemContainer page in _containers.Values)
        {
            if (page.Identity == target && page.ContainsPlacement(slot))
            {
                to = page;
                newSlot = new Identity(page.Identity.Type, slot);
                break;
            }
        }

        // How the server names the player's own inventory as a move's target is not read; the
        // page identity (above) and the character's identity (here, the slot picks the page)
        // are both accepted.
        if (to == null && target.Type == IdentityType.SimpleChar && TryFindContainer(slot, out ItemContainer byPlacement))
        {
            to = byPlacement;
            newSlot = new Identity(byPlacement.Identity.Type, slot);
        }

        if (to == null && _opened.TryGetValue(target, out ItemContainer bag))
        {
            to = bag;
            newSlot = BackpackItemIdentity(_openedHandles[target], slot);
        }

        to?.Add(new InventoryItem(newSlot, item.UniqueIdentity, item.Flags, item.Count, item.Item));
        Changed?.Invoke();
        return true;
    }

    /// <summary>An item by the identity moves address it by, in the pages or an opened container.</summary>
    public bool TryFindAnywhere(Identity slot, out ItemContainer container, out InventoryItem item)
    {
        foreach (ItemContainer candidate in _containers.Values)
            if (Find(candidate, slot, out item)) { container = candidate; return true; }

        foreach (ItemContainer candidate in _opened.Values)
            if (Find(candidate, slot, out item)) { container = candidate; return true; }

        container = null;
        item = null;
        return false;

        static bool Find(ItemContainer c, Identity s, out InventoryItem found)
        {
            for (int i = 0; i < c.Items.Count; i++)
                if (c.Items[i].Slot == s) { found = c.Items[i]; return true; }
            found = null;
            return false;
        }
    }

    static Item ResolveItem(InventorySlot slot, ItemTemplateCache templates)
    {
        try
        {
            return templates.GetItem(slot.ItemLowId, slot.ItemHighId, slot.Quality);
        }
        catch (Exception ex)
        {
            Debug.LogWarning(
                $"[PlayerInventory] Failed to resolve item {slot.ItemLowId}/{slot.ItemHighId} QL{slot.Quality}: {ex.Message}");
            return null;
        }
    }

    bool TryFindContainer(int placement, out ItemContainer container)
    {
        foreach (ItemContainer candidate in _containers.Values)
        {
            if (candidate.ContainsPlacement(placement))
            {
                container = candidate;
                return true;
            }
        }

        container = null;
        return false;
    }
}
