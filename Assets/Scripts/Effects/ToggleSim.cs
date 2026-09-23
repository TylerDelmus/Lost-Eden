using System;

/// <summary>
/// Stock <c>GfxControlToggle_t</c> (typeCode 3036, 0xbdc; vftable <c>Gamecode 1016f984</c>, loader
/// <c>1011265a</c>, Process <c>10112952</c>, terminate <c>101124c3</c>): runs one child effect while its
/// conditions hold, e.g. the vehicle buffs' 72412 / 72416, which start their exhaust when the vehicle drives
/// forward.
///
/// Fields: 0 flags, 1 duration, 2 the child's id, 3 a mask on the playfield resource's +0x50, 4 a count and
/// 5.. that many playfield ids. Flags: bits 0-2 the locator's; 0x400 stay alive to run the child again after
/// it ends; 0x800 the ids are playfields it may not run in (else the only ones it may); 0x1000 the child is
/// made without a locator (<c>100ce912</c>); 0x2000 on a dynel, start the child only when the dynel starts
/// moving forward; 0x8000 end it (gracefully) when the dynel stops.
///
/// The playfield test (<c>101125ed</c>): a mask set but not met fails; then without 0x800 the playfield must be
/// in the list (an empty list fails), with 0x800 it must not be (an empty list passes).
///
/// The moving flag (+0x90, 0 at first; <c>10112580</c>), with 0x2000: the dynel's <c>Vehicle_t</c> (its mover:
/// direction +0x90, +1 at first and -1 while backing, set by <c>SetDirection</c>; speed +0xcc) — with a
/// direction above 0 it is speed &gt; 0; with the direction below it holds.
///
/// No Unity dependency so the recovered maths can be asserted from plain unit tests.
/// </summary>
public sealed class ToggleSim
{
    public const int FlagRearm = 0x400;
    public const int FlagExcludeList = 0x800;
    public const int FlagNoLocator = 0x1000;
    public const int FlagStartMoving = 0x2000;
    public const int FlagStopWithDynel = 0x8000;
    public const int MovementFlags = 0x6000;

    readonly int[] _playfields;

    public int Flags { get; private set; }
    public float Duration { get; }
    public int ChildId { get; }
    public int PlayfieldMask { get; }
    public int PlayfieldCount => _playfields.Length;

    /// <summary>Stock +0x90.</summary>
    public bool Moving { get; private set; }

    public bool Rearm => (Flags & FlagRearm) != 0;

    public ToggleSim(float[] fields)
    {
        Flags = Int(fields, 0);
        Duration = F(fields, 1);
        ChildId = Int(fields, 2);
        PlayfieldMask = Int(fields, 3);
        int n = Math.Max(0, Int(fields, 4));
        _playfields = new int[n];
        for (int i = 0; i < n; i++)
            _playfields[i] = Int(fields, 5 + i);
    }

    /// <summary><c>101125ed</c>: whether the child may run in this playfield.</summary>
    public bool PlayfieldAllows(int playfieldId, int playfieldFlags)
    {
        if (PlayfieldMask != 0 && (playfieldFlags & PlayfieldMask) == 0)
            return false;

        bool listed = Array.IndexOf(_playfields, playfieldId) >= 0;
        if ((Flags & FlagExcludeList) == 0)
            return _playfields.Length > 0 && listed;
        return !listed;
    }

    /// <summary><c>10112580</c>: the moving flag from the dynel's mover.</summary>
    public void UpdateMoving(int direction, float speed)
    {
        if ((Flags & MovementFlags) == 0 || (Flags & FlagStartMoving) == 0 || direction <= 0)
            return;
        Moving = 0f < speed;
    }

    /// <summary>
    /// <c>10112a07</c>..<c>10112a2c</c>: on a dynel, whether to make the child now — with 0x2000 only as the dynel
    /// starts moving.
    /// </summary>
    public bool StartOnDynel(int direction, float speed)
    {
        bool was = Moving;
        UpdateMoving(direction, speed);
        if ((Flags & FlagStartMoving) == 0)
            return true;
        return Moving && !was;
    }

    /// <summary><c>10112a88</c>..<c>10112ab3</c>: with 0x8000, whether the child is to end now, as the dynel stops.</summary>
    public bool StopWithDynel(int direction, float speed)
    {
        if ((Flags & FlagStopWithDynel) == 0)
            return false;
        bool was = Moving;
        UpdateMoving(direction, speed);
        return !Moving && was;
    }

    /// <summary><c>101124c3</c>: a terminate clears 0x400.</summary>
    public void Terminate() => Flags &= ~FlagRearm;

    static float F(float[] f, int i) => f != null && i >= 0 && i < f.Length ? f[i] : 0f;

    static int Int(float[] f, int i) => f != null && i >= 0 && i < f.Length ? BitConverter.SingleToInt32Bits(f[i]) : 0;
}
