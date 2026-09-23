using System;

/// <summary>
/// Stock <c>_GfxControlMesh_t</c> (typeCode 3010, 0xbc2; vftable <c>Gamecode 1016d31c</c>, Process
/// <c>100e52f3</c>, field loader <c>100e5502</c>, build <c>100e53eb</c>): one of four tower-wreck
/// models stood on the locator, turned about the world y and faded in and out. Nothing here depends
/// on Unity.
///
/// Fields: 0 flags, 1-6 the locator offset and turn, 7 attach, 8 duration, <b>9 which model</b>,
/// 10 the fade-in, 11 the fade-out, 12 the height it is lifted, 13 the turn in radians per second.
/// Only 5 records exist and they are all 14 fields.
///
/// <b>Field 9 picks the model</b> (<c>100e53fb</c>, a four-way switch that bails out for anything
/// else, leaving the control with no visual and killing it). The names are literals in Gamecode,
/// looked up as RDB type 1010001 — see <see cref="ModelNames"/>.
///
/// The alpha is a plain ramp the loader pre-divides for: up over field 10, held at 1, then down over
/// field 11 (<c>100e5325</c>). It reaches the visual through <c>VisualMesh_t::SetTransparency</c>,
/// with the position and the turn going through <c>SetPosition</c> and <c>SetRotation</c>.
/// </summary>
public sealed class MeshSim
{
    /// <summary><c>100e536d</c>: the model's foot is put on the ground under the locator.</summary>
    public const int FlagGroundSnap = 0x100;

    /// <summary><c>100e530d</c>: a dead locator does not end a control that has this bit.</summary>
    public const int FlagOutlivesLocator = 0x200;

    /// <summary>
    /// The four literals at <c>1016d288</c>, <c>1016d2ac</c>, <c>1016d2d4</c> and <c>1016d2f8</c>,
    /// in the order field 9 selects them.
    /// </summary>
    public static readonly string[] ModelNames =
    {
        "tower_destroyed_buff&debuff.abiff",
        "tower_destroyed_buff&debuff_LL.abiff",
        "tower_destroyed_controller.abiff",
        "tower_destroyed_guard.abiff",
    };

    readonly float[] _fields;

    public int Flags { get; }
    public float Duration { get; }

    /// <summary>Field 9. Anything outside 0-3 leaves stock with no visual at all.</summary>
    public int ModelIndex { get; }

    public float FadeIn { get; }
    public float FadeOut { get; }

    /// <summary>Field 12: how far the model is lifted above the ground or the locator.</summary>
    public float HeightOffset { get; }

    /// <summary>Field 13: radians per second about the world y.</summary>
    public float SpinRate { get; }

    /// <summary>Stock <c>+0x5c</c> and <c>+0x60</c>: the loader stores the reciprocals.</summary>
    public float InvFadeIn { get; }
    public float InvFadeOut { get; }

    /// <summary>Stock <c>+0x64</c>: <c>duration - field 11</c>.</summary>
    public float FadeOutStart { get; }

    /// <summary>Stock <c>+0x58</c>, what reaches <c>SetTransparency</c>.</summary>
    public float Alpha { get; private set; }

    /// <summary>Stock <c>+0x44</c>..<c>+0x4c</c>, where the model stands.</summary>
    public float X { get; private set; }
    public float Y { get; private set; }
    public float Z { get; private set; }

    /// <summary>The turn about the world y, radians.</summary>
    public float Angle { get; private set; }

    public bool Dead { get; private set; }
    public bool OutlivesLocator => (Flags & FlagOutlivesLocator) != 0;
    public bool GroundSnap => (Flags & FlagGroundSnap) != 0;

    /// <summary>The model this record asks for, or null when field 9 is out of range.</summary>
    public string ModelName
        => ModelIndex >= 0 && ModelIndex < ModelNames.Length ? ModelNames[ModelIndex] : null;

    /// <summary>The ground height under a world point — stock's <c>100d33f3</c>.</summary>
    public Func<float, float, float> GroundHeight { get; set; }

    public MeshSim(float[] fields)
    {
        _fields = fields ?? Array.Empty<float>();

        Flags = Int(0);
        Duration = F(8);
        ModelIndex = Int(9);
        FadeIn = F(10);
        FadeOut = F(11);
        HeightOffset = F(12);
        SpinRate = F(13);

        // 100e5567: the loader divides once so Process can multiply.
        InvFadeIn = FadeIn != 0f ? (float)(1.0 / FadeIn) : 0f;
        InvFadeOut = FadeOut != 0f ? (float)(1.0 / FadeOut) : 0f;
        FadeOutStart = (float)(Duration - (double)FadeOut);

        // 100e5561: it starts invisible.
        Alpha = 0f;

        // 100e540c: a field 9 stock has no model for means no visual, and no visual means dead.
        if (ModelName == null)
            Dead = true;
    }

    /// <summary>
    /// One Process call (<c>100e52f3</c>). <paramref name="locatorValid"/> is stock's
    /// <c>10106591</c> and the three floats its <c>10106306</c> position.
    /// </summary>
    public void Step(float age, bool locatorValid, float lx, float ly, float lz)
    {
        if (Dead)
            return;

        // 100e5301: an invalid locator ends it unless flag 0x200 says otherwise.
        if (!locatorValid && !OutlivesLocator)
        {
            Dead = true;
            return;
        }

        // 100e5325: up, held, down — all from the age, with the divisions already done.
        if (age < FadeIn)
            Alpha = (float)(InvFadeIn * (double)age);
        else if (age < FadeOutStart)
            Alpha = 1f;
        else
            Alpha = (float)(1.0 - (age - (double)FadeOutStart) * InvFadeOut);

        // 100e5364: stock reads the locator every call whatever it answered, so the height below is
        // always worked out afresh rather than added to what the last call left.
        X = lx;
        Z = lz;
        float from = ly;

        // 100e5382: with flag 0x100 the model stands on the ground rather than on the locator.
        if (GroundSnap && GroundHeight != null)
            from = GroundHeight(X, Z);

        // 100e538c: then it is lifted by field 12, whichever way its height was found.
        Y = (float)(HeightOffset + (double)from);

        // 100e53a9: a plain turn about the world y at field 13 radians a second.
        Angle = (float)(SpinRate * (double)age);
    }

    float F(int i) => i >= 0 && i < _fields.Length ? _fields[i] : 0f;

    int Int(int i)
        => i >= 0 && i < _fields.Length ? BitConverter.SingleToInt32Bits(_fields[i]) : 0;
}
