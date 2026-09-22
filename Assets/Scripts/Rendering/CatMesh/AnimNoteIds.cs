/// <summary>
/// Stock animation notes. A CATAnim carries named notes (a time in ms and a 32-byte name, read by
/// AODB as <c>AnimationIdentifiers</c>); DisplaySystem turns each name into an event id when it
/// loads the clip (<c>FUN_10075c84</c>, called from <c>10076488</c>), and Gamecode fires the id as
/// playback crosses the note's time (<c>FUN_1003c248</c> → <c>FUN_1003bedd</c> →
/// <c>FUN_100452d3</c>).
///
/// The lookup is <c>strncmp(name, key, strlen(key))</c> down a fixed list, first hit wins, so a
/// name only has to start with the key and an earlier short key shadows a later long one:
/// "stepfast" is <see cref="Step"/>, "idle_combat" is <see cref="Idle"/>. Unknown names, and
/// loopstart / loopend, are 0. Bytes below 0x20 or above 0x7f end the name (<c>10076467</c>).
/// </summary>
public static class AnimNoteIds
{
    public const int None = 0;
    public const int Get = 0x1;
    public const int Idle = 0xf;
    public const int Attack = 0xb;
    public const int Step = 0x26;
    public const int ItemShowB = 0x41;

    /// <summary>
    /// "effect1start". During a nano cast (charstate 5) it launches the nano's tracer
    /// (<c>FUN_100452d3</c> → <c>FUN_1004f989</c>).
    /// </summary>
    public const int Effect1Start = 0x42;

    // DisplaySystem FUN_10075c84, in its order.
    static readonly (string Key, int Id)[] Table =
    {
        ("loopstart", 0), ("loopend", 0),
        ("right", 0x26), ("left", 0x26), ("land", 0x85), ("step", 0x26), ("stepfast", 0x36),
        ("wingflap", 0x25), ("wingflapfast", 0x37), ("swim", 0x38), ("swimfast", 0x39),
        ("othermove", 0x3a), ("othermovefast", 0x3b), ("idle", 0xf),
        ("attack_start_1", 0x77), ("attack_start_2", 0x78), ("attack_start_3", 0x79),
        ("attack_start_4", 0x7a), ("attack_start_5", 0x7b), ("attack_start_6", 0x7c),
        ("attack_start_7", 0x7d), ("attack_start_8", 0x7e), ("attack_start_9", 0x7f),
        ("attack_effect_1", 0x8e), ("attack_effect_2", 0x8f), ("attack_effect_3", 0x90),
        ("attack_effect_4", 0x91), ("attack", 0xb),
        ("get", 0x1), ("drop", 0x2), ("use", 0x3), ("repair", 0x4), ("useitemonitem", 0x5),
        ("wear", 0x6), ("unwear", 0x7), ("wield", 0x8), ("unwield", 0x9),
        ("die", 0x1e), ("impact", 0x1f), ("doubleattack", 0xe), ("aimedshot", 0x15),
        ("burst", 0x16), ("fullauto", 0x17), ("leftattack", 0x18), ("quickattack", 0x19),
        ("flingshot", 0x1c), ("sneakattack", 0x1d), ("dimach", 0x24), ("brawl", 0x23),
        ("blurstart", 0x2d), ("blurend", 0x35),
        ("itemhide_l", 0x3c), ("itemhide_r", 0x3d), ("itemhide_b", 0x3e),
        ("itemshow_l", 0x3f), ("itemshow_r", 0x40), ("itemshow_b", 0x41),
        ("effect1start", 0x42), ("effect1stop", 0x43), ("effect2start", 0x44), ("effect2stop", 0x45),
        ("swish_punch", 0x73), ("swish_kick", 0x74), ("swish_whip", 0x75), ("swish_huge", 0x76),
        ("enter_combat", 0x80), ("exit_combat", 0x81), ("idle_combat", 0x82),
    };

    public static int FromName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return None;

        int end = 0;
        while (end < name.Length && name[end] >= 0x20 && name[end] <= 0x7f)
            end++;
        if (end < name.Length)
            name = name.Substring(0, end);

        for (int i = 0; i < Table.Length; i++)
        {
            if (name.StartsWith(Table[i].Key, System.StringComparison.Ordinal))
                return Table[i].Id;
        }
        return None;
    }

    /// <summary>
    /// Whether a note at <paramref name="time"/> fires on a step of the clip clock from
    /// <paramref name="from"/> to <paramref name="to"/>: after <paramref name="from"/> (or at it, on
    /// the clip's first step) and up to <paramref name="to"/>.
    /// </summary>
    public static bool Crossed(float time, float from, float to, bool includeFrom)
        => (includeFrom ? time >= from : time > from) && time <= to;
}
