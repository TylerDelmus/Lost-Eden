/// <summary>
/// Stock AnimHolder walk/run/idle slots. Class 3 only overlays walk-2h / run-2h.
/// Idle is AnimSet + peace/fight; class 3 is the only set with two idle keys.
/// </summary>
public sealed class AnimHolder
{
    public int RunForward { get; private set; } = AnimKindIds.Run;
    public int WalkForward { get; private set; } = AnimKindIds.Walk;
    public int Hover { get; private set; } = AnimKindIds.Hover;
    public int Idle { get; private set; } = AnimKindIds.IdleStand;
    public int Sneak { get; private set; } = AnimKindIds.SneakCool;
    public int CrawlIdle { get; private set; } = AnimKindIds.IdleCrawl;

    public void ApplyMoveSlots(int animSet)
    {
        RunForward = AnimKindIds.Run;
        WalkForward = AnimKindIds.Walk;
        Hover = AnimKindIds.Hover;
        Sneak = AnimKindIds.SneakCool;
        CrawlIdle = AnimKindIds.IdleCrawl;
        if (animSet == 3)
        {
            WalkForward = AnimKindIds.Walk2h;
            RunForward = AnimKindIds.Run2h;
        }
    }

    public void ApplyUnarmedDefaults(bool fighting)
    {
        ApplyMoveSlots(-1);
        Idle = PickIdleKind(-1, fighting);
    }

    public void ApplyStance(int animSet, bool fighting)
    {
        ApplyMoveSlots(animSet);
        Idle = PickIdleKind(animSet, fighting);
    }

    /// <summary>
    /// Slot 6→8→0 AnimSet (or -1 unarmed), then peace vs fight.
    /// Class 3: peace idle-2h (K=0x29), fight idle-rifle (K=0x10).
    /// Class 7 K=0x10 is spell-sys (cast), not a stand idle.
    /// </summary>
    public static int PickIdleKind(int animSet, bool fighting)
    {
        if (animSet < 0)
            return fighting ? AnimKindIds.IdleUnarmed : AnimKindIds.IdleStand;

        return animSet switch
        {
            0 => AnimKindIds.IdleSmallarms,
            1 or 2 => AnimKindIds.IdleBlade,
            3 => fighting ? AnimKindIds.IdleRifle : AnimKindIds.Idle2h,
            6 => AnimKindIds.IdleBow,
            8 => AnimKindIds.IdleBazooka,
            _ => AnimKindIds.IdleStand,
        };
    }
}
