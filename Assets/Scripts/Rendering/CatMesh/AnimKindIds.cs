/// <summary>
/// Stock animation kind ids used by weapon AnimSet and locomotion.
/// </summary>
public static class AnimKindIds
{
    public const int Wave = 0x3e;
    public const int SocialKindMax = 0x48;

    public const int Walk = 0x64;
    public const int Run = 0x65;
    public const int SneakCool = 0x66;
    public const int Crawl = 0x67;
    public const int IdleStand = 0x78;
    public const int Swim = 0x85;
    public const int WalkLeft = 0x86;
    public const int WalkRight = 0x87;
    public const int WalkBack = 0x88;
    public const int IdleCrawl = 0x9a;
    public const int JumpStand = 0x9c;
    public const int JumpForward = 0x9d;
    public const int Hover = 0xb3;
    public const int IdleBow = 0xb6;
    public const int BowShot = 0xb7;
    public const int JumpLandWalk = 0xb9;
    public const int JumpLandRun = 0xba;
    public const int IdleSwim = 0xc3;
    public const int TurnLeft = 0xc4;
    public const int TurnRight = 0xc5;
    public const int SpellSys = 0xcb;
    public const int RunBack = 0xde;

    public const int IdleBlade = 0x3e9;
    public const int Blade1hSlash = 0x3eb;
    public const int Blade1hStab = 0x3ec;
    public const int Blade1hSlashL = 0x3ee;
    public const int Blade1hStabL = 0x3ef;
    public const int IdleSmallarms = 0x3f3;
    public const int SmallarmsShot = 0x3f5;
    public const int SmallarmsShotL = 0x3f8;
    public const int RifleStart = 0x3fc;
    public const int IdleRifle = 0x3fd;
    public const int RifleShot = 0x3ff;
    public const int UnarmedStart = 0x406;
    public const int IdleUnarmed = 0x407;
    public const int UnarmedStop = 0x408;
    public const int UnarmedRSwing = 0x40a;
    public const int Blade2hChop = 0x41a;
    public const int Blade2hDowncut = 0x41b;
    public const int Blade2hSlash = 0x41c;
    public const int Blade2hStab = 0x41d;
    public const int Idle2h = 0x41e;
    public const int Walk2h = 0x421;
    public const int Run2h = 0x422;
    public const int IdleBazooka = 0x424;
    public const int BazookaShot = 0x426;

    public const int MapIdle = 0x10;
    public const int MapDraw = 0x1a;
    public const int MapAttack = 0xb;
    public const int MapIdle2h = 0x29;
    public const int Map2hStart = 0x27;
    public const int Map2hStop = 0x28;

    public const int ItemAnimSet = 0x161;
    public const int ItemAttackDelay = 0x126;

    public const int EquipRight = 6;
    public const int EquipLeft = 8;
    public const int EquipUtil = 0;
    public const int EquipRightAlt = 0x3d;
    public const int EquipLeftAlt = 0x3f;

    public const int FightEnterAction = 0x0b;
    public const int FightLeaveAction = 0x4e;
    /// <summary>CharacterAction 0x62: muzzle FX only. AttackInfo owns the swing overlay.</summary>
    public const int AttackSwingAction = 0x62;

    public static bool IsLongRangeAnimSet(int animSet)
        => animSet == 2 || animSet == 3 || animSet == 6 || animSet == 8;

    public static int NormalizeEquipSlot(int slot)
    {
        if (slot == EquipRightAlt)
            return EquipRight;
        if (slot == EquipLeftAlt)
            return EquipLeft;
        return slot;
    }
}
