using System;

/// <summary>
/// Stock <c>GfxControlScatter_t</c> (type 3029, 0xbd5; vftable <c>Gamecode 1016f6cc</c>, loader
/// <c>10110cc2</c>, slot build <c>10110db5</c>, Process <c>101108a8</c>): N timed copies of one child,
/// each placed by its own scatter offset.
///
/// Fields: 0 flags, 1/2/3 an offset added to every copy, 4 the position mode, 5 the time mode,
/// 6 the slot count, 7 the duration (the base timer's +0x10), 8 the scatter distance, 9 the second
/// scatter scalar, 10 the child's id. Every record has exactly 11 fields; the line cursor (+0xa8) and
/// the cycle base (+0x44) are zeroed by the constructor, not loaded.
///
/// Flags: 0x400 start the whole schedule again when it runs dry, 0x1000 drop each copy to the ground,
/// 0x2000 make the copy on the Scatter's own locator instead of at the point it computed, 0x4000 end
/// at once when terminated.
///
/// The slots are stamped once (<c>10110e03</c>), 12 bytes each (time, a fired byte, the child):
/// time mode 0 gives each slot <c>r * duration</c>, mode 1 gives it <c>duration * i / count</c>, and
/// any other mode leaves it at 0.
///
/// A slot fires when its time has passed <c>age - cycleBase</c>. Its offset (<c>101109d2</c>) is:
/// <list type="bullet">
/// <item>mode 0: <c>(2r - 1, 2r - 1, 2r - 1) * field8</c> — a point in a cube.</item>
/// <item>mode 1: <c>(0, 0, cursor) + (2r - 1, 2r - 1, 2r - 1) * field9</c>, then the cursor advances by
/// <c>field8 / count</c> — a line of copies, each with its own jitter.</item>
/// <item>anything else: no offset.</item>
/// </list>
/// Without 0x2000 the copy is made at a world point (<c>100cea4b</c>): the locator's base, plus that
/// offset, dropped to the ground with 0x1000, plus fields 1-3. With 0x2000 it is made on the locator
/// itself (<c>100cf4fe</c> / <c>100cfdc6</c>), and the offset is not used.
///
/// The children are made through the factories **without registering them**, so the Scatter processes
/// and draws them itself, exactly as Toggle does (§5.36).
///
/// Once every slot has fired and every child has gone: with 0x400 the fired bytes are cleared and the
/// cycle base becomes the age (<c>10110c96</c>), so it runs for ever; without it the control is ready.
///
/// No Unity dependency so the recovered maths can be asserted from plain unit tests.
/// </summary>
public sealed class ScatterSim
{
    public const int FlagRepeat = 0x400;
    public const int FlagGround = 0x1000;
    public const int FlagOnLocator = 0x2000;
    public const int FlagEndOnTerminate = 0x4000;

    /// <summary>Position mode 0: a point in a cube of half-size field 8.</summary>
    public const int PositionCube = 0;

    /// <summary>Position mode 1: a line along z, each copy jittered by field 9.</summary>
    public const int PositionLine = 1;

    public const int TimeRandom = 0;
    public const int TimeEven = 1;

    /// <summary>Stock allocates whatever field 6 asks for; the port caps it.</summary>
    public const int MaxSlots = 256;

    public readonly int Flags;
    public readonly float OffsetX, OffsetY, OffsetZ;
    public readonly int PositionMode;
    public readonly int TimeMode;
    public readonly float Duration;
    public readonly float Distance;
    public readonly float Jitter;
    public readonly int ChildId;

    readonly float[] _times;
    readonly bool[] _fired;
    readonly Func<float> _random;

    /// <summary>Stock +0xa8: how far along the line the next copy sits.</summary>
    public float Cursor { get; private set; }

    /// <summary>Stock +0x44: the age the current cycle started at.</summary>
    public float CycleBase { get; private set; }

    public int Count => _times.Length;
    public bool Repeats => (Flags & FlagRepeat) != 0;
    public bool OnLocator => (Flags & FlagOnLocator) != 0;
    public bool Ground => (Flags & FlagGround) != 0;
    public bool EndsOnTerminate => (Flags & FlagEndOnTerminate) != 0;

    public float FireTime(int i) => _times[i];
    public bool Fired(int i) => _fired[i];
    public void MarkFired(int i) => _fired[i] = true;

    public ScatterSim(float[] fields, Func<float> random)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
        Flags = Int(fields, 0);
        OffsetX = F(fields, 1);
        OffsetY = F(fields, 2);
        OffsetZ = F(fields, 3);
        PositionMode = Int(fields, 4);
        TimeMode = Int(fields, 5);
        int count = Math.Max(0, Math.Min(Int(fields, 6), MaxSlots));
        Duration = F(fields, 7);
        Distance = F(fields, 8);
        Jitter = F(fields, 9);
        ChildId = Int(fields, 10);

        _times = new float[count];
        _fired = new bool[count];
        // 10110e03: stamped once, in slot order.
        for (int i = 0; i < count; i++)
        {
            if (TimeMode == TimeRandom)
                _times[i] = (float)(_random() * (Duration - 0.0) + 0.0);
            else if (TimeMode == TimeEven)
                _times[i] = Duration * i / count;
        }
    }

    /// <summary><c>10110979</c>: a slot fires on the time since this cycle began.</summary>
    public float Elapsed(float age) => age - CycleBase;

    /// <summary>
    /// The next copy's scatter offset (<c>101109d2</c>), advancing the line cursor in mode 1. Stock
    /// draws x, y then z; the port takes any uniform [0, 1).
    /// </summary>
    public void NextOffset(out float x, out float y, out float z)
    {
        x = 0f;
        y = 0f;
        z = 0f;

        if (PositionMode == PositionCube)
        {
            x = Draw() * Distance;
            y = Draw() * Distance;
            z = Draw() * Distance;
            return;
        }

        if (PositionMode != PositionLine)
            return;

        float jx = Draw(), jy = Draw(), jz = Draw();
        x = jx * Jitter;
        y = jy * Jitter;
        z = Cursor + jz * Jitter;
        // 10110a5e: the cursor walks the whole distance over the slot count.
        if (Count > 0)
            Cursor += Distance / Count;
    }

    /// <summary>Stock's <c>2r - 1</c>.</summary>
    float Draw() => (float)(_random() * 2.0 - 1.0);

    /// <summary><c>10110c96</c>: with 0x400 the schedule starts over from this age.</summary>
    public void Rearm(float age)
    {
        for (int i = 0; i < _fired.Length; i++)
            _fired[i] = false;
        CycleBase = age;
    }

    static float F(float[] f, int i) => f != null && i >= 0 && i < f.Length ? f[i] : 0f;

    static int Int(float[] f, int i) => f != null && i >= 0 && i < f.Length ? BitConverter.SingleToInt32Bits(f[i]) : 0;
}
