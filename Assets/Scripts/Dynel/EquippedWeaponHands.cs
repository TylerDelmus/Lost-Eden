/// <summary>
/// Equipped weapon items for slots 6 / 8 / 0 and their AnimSet kind maps.
/// </summary>
public sealed class EquippedWeaponHands
{
    public Item Right { get; private set; }
    public Item Left { get; private set; }
    public Item Util { get; private set; }

    public WeaponAnimListMap RightMap { get; } = new WeaponAnimListMap();
    public WeaponAnimListMap LeftMap { get; } = new WeaponAnimListMap();
    public WeaponAnimListMap UtilMap { get; } = new WeaponAnimListMap();

    public Item StanceItem => Right ?? Left ?? Util;

    public WeaponAnimListMap StanceMap
    {
        get
        {
            if (Right != null)
                return RightMap;
            if (Left != null)
                return LeftMap;
            if (Util != null)
                return UtilMap;
            return null;
        }
    }

    public int StanceAnimSet
    {
        get
        {
            Item item = StanceItem;
            if (item == null)
                return -1;

            return item.TryGetStat(AnimKindIds.ItemAnimSet, out int animSet) ? animSet : 0;
        }
    }

    public void Clear()
    {
        Right = null;
        Left = null;
        Util = null;
        RightMap.Clear();
        LeftMap.Clear();
        UtilMap.Clear();
    }

    public void Set(Item right, Item left, Item util)
    {
        Right = right;
        Left = left;
        Util = util;
        Populate(RightMap, right, offHand: false);
        Populate(LeftMap, left, offHand: true);
        Populate(UtilMap, util, offHand: false);
    }

    public Item ItemInSlot(int slot)
    {
        switch (AnimKindIds.NormalizeEquipSlot(slot))
        {
            case AnimKindIds.EquipRight:
                return Right;
            case AnimKindIds.EquipLeft:
                return Left;
            case AnimKindIds.EquipUtil:
                return Util;
            default:
                return null;
        }
    }

    public WeaponAnimListMap MapForSlot(int slot)
    {
        switch (AnimKindIds.NormalizeEquipSlot(slot))
        {
            case AnimKindIds.EquipRight:
                return Right != null ? RightMap : null;
            case AnimKindIds.EquipLeft:
                return Left != null ? LeftMap : null;
            case AnimKindIds.EquipUtil:
                return Util != null ? UtilMap : null;
            default:
                return null;
        }
    }

    static void Populate(WeaponAnimListMap map, Item item, bool offHand)
    {
        map.Clear();
        if (item == null)
            return;

        int animSet = item.TryGetStat(AnimKindIds.ItemAnimSet, out int value) ? value : 0;
        map.Populate(animSet, offHand);
    }
}
