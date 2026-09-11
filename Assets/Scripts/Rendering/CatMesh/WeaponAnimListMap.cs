using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-weapon-item kind multimap filled from AnimSet (item stat 0x161), not a per-weapon RDB table.
/// </summary>
public sealed class WeaponAnimListMap
{
    readonly Dictionary<int, List<int>> _rows = new Dictionary<int, List<int>>();

    public int AnimSet { get; private set; } = -1;
    public bool OffHand { get; private set; }
    public bool IsEmpty => _rows.Count == 0;

    public void Clear()
    {
        _rows.Clear();
        AnimSet = -1;
        OffHand = false;
    }

    public void Populate(int animSet, bool offHand)
    {
        Clear();
        if (animSet == 4 || animSet == 5)
            return;

        AnimSet = animSet;
        OffHand = offHand;

        switch (animSet)
        {
            case 0:
                Add(AnimKindIds.MapIdle, AnimKindIds.IdleSmallarms);
                Add(AnimKindIds.MapAttack, offHand ? AnimKindIds.SmallarmsShotL : AnimKindIds.SmallarmsShot);
                break;
            case 1:
                Add(AnimKindIds.MapIdle, AnimKindIds.IdleBlade);
                if (offHand)
                {
                    Add(AnimKindIds.MapAttack, AnimKindIds.Blade1hSlashL);
                    Add(AnimKindIds.MapAttack, AnimKindIds.Blade1hStabL);
                    Add(AnimKindIds.MapAttack, AnimKindIds.Blade1hSlashL);
                }
                else
                {
                    Add(AnimKindIds.MapAttack, AnimKindIds.Blade1hSlash);
                    Add(AnimKindIds.MapAttack, AnimKindIds.Blade1hStab);
                    Add(AnimKindIds.MapAttack, AnimKindIds.Blade1hSlash);
                }
                break;
            case 2:
                Add(AnimKindIds.MapIdle, AnimKindIds.IdleBlade);
                Add(AnimKindIds.MapAttack, AnimKindIds.Blade2hChop);
                Add(AnimKindIds.MapAttack, AnimKindIds.Blade2hDowncut);
                Add(AnimKindIds.MapAttack, AnimKindIds.Blade2hSlash);
                Add(AnimKindIds.MapAttack, AnimKindIds.Blade2hStab);
                break;
            case 3:
                Add(AnimKindIds.MapIdle, AnimKindIds.IdleRifle);
                Add(AnimKindIds.MapIdle2h, AnimKindIds.Idle2h);
                Add(AnimKindIds.MapDraw, AnimKindIds.RifleStart);
                Add(AnimKindIds.MapAttack, AnimKindIds.RifleShot);
                break;
            case 6:
                Add(AnimKindIds.MapIdle, AnimKindIds.IdleBow);
                Add(AnimKindIds.MapAttack, AnimKindIds.BowShot);
                break;
            case 7:
                Add(AnimKindIds.MapIdle, AnimKindIds.SpellSys);
                Add(AnimKindIds.MapAttack, AnimKindIds.SpellSys);
                break;
            case 8:
                Add(AnimKindIds.MapIdle, AnimKindIds.IdleBazooka);
                Add(AnimKindIds.MapAttack, AnimKindIds.BazookaShot);
                break;
        }
    }

    public int PickRandomKindId(int key)
    {
        if (!_rows.TryGetValue(key, out List<int> kinds) || kinds == null || kinds.Count == 0)
            return 0;

        if (kinds.Count == 1)
            return kinds[0];

        return kinds[Random.Range(0, kinds.Count)];
    }

    public bool TryGetKinds(int key, out List<int> kinds)
        => _rows.TryGetValue(key, out kinds) && kinds != null && kinds.Count > 0;

    void Add(int key, int kind)
    {
        if (!_rows.TryGetValue(key, out List<int> list))
        {
            list = new List<int>(4);
            _rows[key] = list;
        }

        list.Add(kind);
    }
}
