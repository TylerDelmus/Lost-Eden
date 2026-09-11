using System.Collections.Generic;

/// <summary>
/// Kind id → PC filename token (the stem in male_{token}_01_01.ani).
/// </summary>
public static class AnimKindCatalog
{
    static readonly Dictionary<int, string> Names = new Dictionary<int, string>
    {
        { AnimKindIds.Wave, "wave" },
        { AnimKindIds.Walk, "walk" },
        { AnimKindIds.Run, "run" },
        { AnimKindIds.SneakCool, "sneakcool" },
        { AnimKindIds.Crawl, "crawl" },
        { AnimKindIds.IdleStand, "idle-stand" },
        { AnimKindIds.Swim, "swim" },
        { AnimKindIds.WalkLeft, "walk-left" },
        { AnimKindIds.WalkRight, "walk-right" },
        { AnimKindIds.WalkBack, "walk-back" },
        { AnimKindIds.IdleCrawl, "idle-crawl" },
        { AnimKindIds.JumpStand, "jump-stand" },
        { AnimKindIds.JumpForward, "jump-forward" },
        { AnimKindIds.Hover, "hover" },
        { AnimKindIds.IdleBow, "idle-bow" },
        { AnimKindIds.BowShot, "bow-shot" },
        { AnimKindIds.JumpLandWalk, "jump-land-walk" },
        { AnimKindIds.JumpLandRun, "jump-land-run" },
        { AnimKindIds.IdleSwim, "idle-swim" },
        { AnimKindIds.TurnLeft, "turn-left" },
        { AnimKindIds.TurnRight, "turn-right" },
        { AnimKindIds.SpellSys, "spell-sys" },
        { AnimKindIds.RunBack, "run-back" },
        { AnimKindIds.IdleBlade, "idle-blade" },
        { AnimKindIds.Blade1hSlash, "blade1h-slash" },
        { AnimKindIds.Blade1hStab, "blade1h-stab" },
        { AnimKindIds.Blade1hSlashL, "blade1hl-slash" },
        { AnimKindIds.Blade1hStabL, "blade1hl-stab" },
        { AnimKindIds.IdleSmallarms, "idle-smallarms" },
        { AnimKindIds.SmallarmsShot, "smallarms-shot" },
        { AnimKindIds.SmallarmsShotL, "smallarms-shotl" },
        { AnimKindIds.RifleStart, "rifle-start" },
        { AnimKindIds.IdleRifle, "idle-rifle" },
        { AnimKindIds.RifleShot, "rifle-shot" },
        { AnimKindIds.UnarmedStart, "unarmed-start" },
        { AnimKindIds.IdleUnarmed, "idle-unarmed" },
        { AnimKindIds.UnarmedStop, "unarmed-stop" },
        { AnimKindIds.UnarmedRSwing, "unarmed-rswing" },
        { AnimKindIds.Blade2hChop, "blade2h-chop" },
        { AnimKindIds.Blade2hDowncut, "blade2h-downcut" },
        { AnimKindIds.Blade2hSlash, "blade2h-slash" },
        { AnimKindIds.Blade2hStab, "blade2h-stab" },
        { AnimKindIds.Idle2h, "idle-2h" },
        { AnimKindIds.Walk2h, "walk-2h" },
        { AnimKindIds.Run2h, "run-2h" },
        { AnimKindIds.IdleBazooka, "idle-bazooka" },
        { AnimKindIds.BazookaShot, "bazooka-shot" },
    };

    public static bool TryGetName(int kindId, out string name)
        => Names.TryGetValue(kindId, out name) && !string.IsNullOrEmpty(name);

    public static string StartNameForAnimSet(int animSet) => animSet switch
    {
        0 => "smallarms-start",
        1 or 2 => "blade-start",
        3 => "rifle-start",
        6 => "bow-start",
        8 => "bazooka-start",
        _ => null,
    };

    public static string StopNameForAnimSet(int animSet) => animSet switch
    {
        0 => "smallarms-stop",
        1 or 2 => "blade-stop",
        3 => "rifle-stop",
        6 => "bow-stop",
        8 => "bazooka-stop",
        _ => null,
    };
}
