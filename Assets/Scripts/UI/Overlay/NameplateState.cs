using System;

[Flags]
public enum NameplateState
{
    Default = 0,
    PickupItem = 1 << 0,
    InCombat = 1 << 1,
    ItemRarity_Common = 1 << 2,
    ItemRarity_Uncommon = 1 << 3,
    ItemRarity_Rare = 1 << 4,
    ItemRarity_Epic = 1 << 5,
    ItemRarity_Legendary = 1 << 6,
    HealthVisible = 1 << 7,
    HasLevel = 1 << 8,
    Disabled = 1 << 9
}
