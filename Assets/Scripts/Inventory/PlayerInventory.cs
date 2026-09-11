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
