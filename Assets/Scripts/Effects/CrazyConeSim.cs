using System;

/// <summary>
/// Stock <c>_GfxControlCrazyCone_t</c> (typeCode 3009, 0xbc1; vftable <c>Gamecode 1016c5c4</c>,
/// Process <c>100d6dcc</c>, field loader <c>100d6a6f</c>, build <c>100d73b2</c>): a nest of hollow
/// cones standing on the locator, spun about their shared axis, whose radii, height, colours and
/// texture offsets are all driven by a list of timed stages. Nothing here depends on Unity;
/// <see cref="ConeFrustum"/> builds the geometry.
///
/// <b>The record is not a flat field list.</b> Fields 0-20 are a header, and then there are
/// <b>field 20 stages of 29 fields each</b>, the first starting at field 21 — which is why the
/// shipped records have 50, 79, 108 or 137 fields (21 + 29n). The loader confirms it: its index
/// starts at 21, the array it allocates is <c>stageCount * 0x74</c> bytes and 0x74 is 29 floats.
///
/// <b>Field 10 is the number of cones, field 20 the number of stages</b> — two different counts that
/// are easy to confuse (record 60001 has 4 cones and 2 stages).
///
/// Header: 0 flags, 1-6 the locator offset and turn, 7 attach, 9 material, 10 the cone count,
/// 11 the segments per cone, 12 the top radius added per cone, 13 the bottom radius added per cone,
/// 14 the u span, 15 the v span, 16 the top radius, 17 the bottom radius, 18 the top height,
/// 19 the height the whole thing is lifted by, 20 the stage count.
///
/// A stage's 29 slots: 0 is its duration, 3-9 are values and 10-16 their matching rates, 17-20 are
/// four packed ARGB colours, 21-28 drive a sub-cycle. Slots 1 and 2 the loader never writes; they
/// are the stage's own accumulators. Slots 17-21 and 27-28 are read as ints, the rest as floats.
///
/// <b>What a stage does</b> (<c>100d6eae</c>): it Euler-integrates its seven value/rate pairs by dt,
/// and those values then integrate into the <i>header</i> fields — so the cone's radii, height and
/// texture spans carry on from where the previous stage left them rather than resetting. Process
/// loops over stages within one call if dt spans more than one, carrying the leftover.
/// </summary>
public sealed class CrazyConeSim
{
    /// <summary>Fields 0-20 inclusive.</summary>
    public const int HeaderFields = 21;

    /// <summary>A stage is 29 fields (<c>0x74</c> bytes in stock).</summary>
    public const int StageFields = 29;

    /// <summary><c>1016c5b8</c>: the same rounded full turn Spiral2 and the build use.</summary>
    public const float FullTurn = 6.28000020980835f;

    /// <summary><c>100d715e</c>: a dead locator does not end a control that has this bit.</summary>
    public const int FlagOutlivesLocator = 0x2000;

    /// <summary><c>100d74db</c>: the anchor's height is snapped to the ground under it.</summary>
    public const int FlagGroundSnap = 0x800;

    /// <summary><c>100d7442</c>: the build fans the cones round a turn. Process overwrites it anyway.</summary>
    public const int FlagFanAtBuild = 0x400;

    /// <summary>Visual ctor argument 4 (<c>100d7417</c>), bit 8: u and v change roles.</summary>
    public const int FlagSwapUv = 0x100;

    /// <summary>Visual ctor argument 6 (<c>100d73f6</c>), bit 9.</summary>
    public const int FlagAdditive = 0x200;

    readonly float[] _fields;
    readonly float[] _stages;       // stageCount * StageFields, the loader's +0x60 array

    /// <summary>Header slots stock mutates in place. Indices into the record's fields.</summary>
    float _uSpan;       // field 14, +0x44
    float _vSpan;       // field 15, +0x48
    float _topRadius;   // field 16, +0x4c
    float _botRadius;   // field 17, +0x50
    float _topHeight;   // field 18, +0x54
    float _lift;        // field 19, +0x58

    /// <summary>Stock <c>+0x94</c> / <c>+0x98</c>: the u and v scroll offsets.</summary>
    float _uScroll, _vScroll;

    /// <summary>Stock <c>+0x8c</c> and <c>+0x90</c>.</summary>
    public int StageIndex { get; private set; }
    public float StageElapsed { get; private set; }

    /// <summary>Stock <c>+0x84</c> / <c>+0x88</c>: the sub-cycle's counter and timer.</summary>
    public int SubCycleCount { get; private set; }
    public float SubCycleTime { get; private set; }

    public int Flags { get; }
    public int Material { get; }
    public int ConeCount { get; }
    public int Segments { get; }
    public int StageCount { get; }

    /// <summary>Field 12: added to the top radius for each cone after the first.</summary>
    public float TopRadiusStep { get; }

    /// <summary>Field 13: added to the bottom radius for each cone after the first.</summary>
    public float BottomRadiusStep { get; }

    /// <summary>Stock <c>+0x14</c> set to 1.</summary>
    public bool Dead { get; private set; }

    /// <summary>Stock <c>+0x64</c>..<c>+0x6c</c>, the point every cone sits on.</summary>
    public float AnchorX { get; private set; }
    public float AnchorY { get; private set; }
    public float AnchorZ { get; private set; }

    /// <summary>What the last <see cref="Step"/> handed the cones.</summary>
    public State Current;
    public State Previous;

    /// <summary>
    /// The ground height under a point (stock <c>100d33f3</c>). Null leaves the anchor where the
    /// locator put it.
    /// </summary>
    public Func<float, float, float> GroundHeight { get; set; }

    public bool SwapUv => (Flags & FlagSwapUv) != 0;
    public bool Additive => (Flags & FlagAdditive) != 0;
    public bool OutlivesLocator => (Flags & FlagOutlivesLocator) != 0;

    public CrazyConeSim(float[] fields)
    {
        _fields = fields ?? Array.Empty<float>();

        Flags = Int(0);
        Material = Int(9);
        ConeCount = Int(10);
        Segments = Int(11);
        TopRadiusStep = F(12);
        BottomRadiusStep = F(13);
        _uSpan = F(14);
        _vSpan = F(15);
        _topRadius = F(16);
        _botRadius = F(17);
        _topHeight = F(18);
        _lift = F(19);
        StageCount = Int(20);

        if (ConeCount < 0)
            ConeCount = 0;
        if (StageCount < 0)
            StageCount = 0;

        // 100d6b23: stageCount blocks of 29, read straight off the end of the header.
        _stages = new float[StageCount * StageFields];
        for (int s = 0; s < StageCount; s++)
            for (int k = 0; k < StageFields; k++)
            {
                // Slots 1 and 2 are the stage's own accumulators; the loader skips them.
                if (k == 1 || k == 2)
                    continue;
                _stages[s * StageFields + k] = F(HeaderFields + s * StageFields + k);
            }
    }

    /// <summary>A stage slot as stock loaded it — slots 17-21 and 27-28 are ints.</summary>
    public float Stage(int stage, int slot) => _stages[stage * StageFields + slot];

    public uint StageColour(int stage, int slot)
        => unchecked((uint)BitConverter.SingleToInt32Bits(_stages[stage * StageFields + slot]));

    public int StageInt(int stage, int slot)
        => BitConverter.SingleToInt32Bits(_stages[stage * StageFields + slot]);

    /// <summary>
    /// One Process call (<c>100d6dcc</c>). <paramref name="locatorValid"/> is stock's
    /// <c>10106591</c> and the three floats its <c>10106306</c> position.
    /// </summary>
    public void Step(float dt, bool locatorValid, float lx, float ly, float lz)
    {
        if (Dead)
            return;

        Previous = Current;

        // 100d6dea: the base timer runs first, then this.
        if (StageIndex >= StageCount)
        {
            Dead = true;
            return;
        }

        float remaining = dt;
        bool more;
        int guard = 0;
        int lastStage = StageIndex;

        do
        {
            more = false;
            int s = StageIndex;
            lastStage = s;
            float duration = Stage(s, 0);
            float t;
            float used;

            if (duration < StageElapsed + remaining)
            {
                // 100d6e21: this stage finishes inside the call. It is drawn at t = 1 and the
                // leftover of dt is carried into the next one.
                used = (float)(duration - (double)StageElapsed);
                StageIndex = s + 1;
                more = StageIndex < StageCount;
                remaining = (float)(remaining - (double)used);
                StageElapsed = 0f;
                t = 1f;
            }
            else
            {
                // 100d6e66 / 100d6e88: either the first call inside a freshly entered stage, or an
                // ordinary one. Both just add what is left and read the progress.
                used = remaining;
                StageElapsed = (float)(StageElapsed + (double)remaining);
                t = duration != 0f ? (float)(StageElapsed / (double)duration) : 1f;
                remaining = 0f;
                if (s != _enteredStage)
                {
                    // 100d6e7c: entering a stage resets the sub-cycle counter to -1.
                    SubCycleCount = -1;
                    _enteredStage = s;
                }
            }

            Advance(s, used, t);
        }
        while (more && StageIndex < StageCount && ++guard < 64);

        // 100d7152: an invalid locator ends it unless flag 0x2000 says otherwise.
        if (!locatorValid && !OutlivesLocator)
        {
            Dead = true;
            return;
        }

        if (locatorValid)
        {
            AnchorX = lx;
            AnchorY = ly;
            AnchorZ = lz;
            if ((Flags & FlagGroundSnap) != 0 && GroundHeight != null)
                AnchorY = GroundHeight(AnchorX, AnchorZ);
        }

        Current = new State
        {
            // 100d724c: the cone stands from the lifted anchor up to field 18.
            Height = (float)(_topHeight - (double)_lift),
            TopRadius = _topRadius,
            BottomRadius = _botRadius,
            USpan = _uSpan,
            VSpan = _vSpan,
            UScroll = _uScroll,
            VScroll = _vScroll,
            ColourA = _lastColourA,
            ColourB = _lastColourB,
            // 100d71fb: the spin is the stage's elapsed time times its slot 9, on the last stage the
            // call touched — which is the stage pointer stock still has in esi.
            Spin = (float)(StageElapsed * (double)Stage(lastStage, 9)),
            AnchorX = AnchorX,
            // 100d7194: the whole nest is lifted by field 19.
            AnchorY = (float)(AnchorY + (double)_lift),
            AnchorZ = AnchorZ,
        };
    }

    int _enteredStage = -1;

    uint _lastColourA, _lastColourB;

    /// <summary>
    /// The per-stage body (<c>100d6eae</c>): integrate the stage's own pairs, then let them drive the
    /// header fields, then read the two colour ramps.
    /// </summary>
    void Advance(int s, float dt, float t)
    {
        int b = s * StageFields;

        // 100d6eae: value += rate * dt, paired 3<-14, 4<-15, 5<-10, 6<-11, 7<-12, 8<-13, 9<-16.
        Integrate(b, 3, 14, dt);
        Integrate(b, 4, 15, dt);
        Integrate(b, 5, 10, dt);
        Integrate(b, 6, 11, dt);
        Integrate(b, 7, 12, dt);
        Integrate(b, 8, 13, dt);
        Integrate(b, 9, 16, dt);

        // 100d6f12: and now those values drive the header's own fields.
        _topRadius = (float)(_stages[b + 7] * (double)dt + _topRadius);
        _botRadius = (float)(_stages[b + 8] * (double)dt + _botRadius);
        _topHeight = (float)(_stages[b + 5] * (double)dt + _topHeight);
        _lift = (float)(_stages[b + 6] * (double)dt + _lift);
        _uSpan = (float)(_stages[b + 1] * (double)dt + _uSpan);
        _vSpan = (float)(_stages[b + 2] * (double)dt + _vSpan);
        _uScroll = (float)(_stages[b + 3] * (double)dt + _uScroll);
        _vScroll = (float)(_stages[b + 4] * (double)dt + _vScroll);

        // 100d6f8d: Color_t::Interpolate over the stage's own progress.
        _lastColourA = StockColorInterpolate(StageColour(s, 17), StageColour(s, 19), t);
        _lastColourB = StockColorInterpolate(StageColour(s, 18), StageColour(s, 20), t);

        AdvanceSubCycle(s, dt);
    }

    void Integrate(int b, int value, int rate, float dt)
        => _stages[b + value] = (float)(_stages[b + rate] * (double)dt + _stages[b + value]);

    /// <summary>
    /// <c>100d7007</c>: slot 22 drives a second timer, slot 25 divides it, and slot 21 is how many
    /// times it may run. One before the end stock adds slot 24 to slot 23; on the last it zeroes
    /// slot 21, which stops the sub-cycle for good.
    /// </summary>
    void AdvanceSubCycle(int s, float dt)
    {
        int repeats = StageInt(s, 21);
        if (repeats == 0)
            return;

        SubCycleTime = (float)(SubCycleTime + (double)dt);
        float period = Stage(s, 22);
        if (period <= 0f || SubCycleTime <= period)
            return;

        SubCycleTime = 0f;
        SubCycleCount++;
        int b = s * StageFields;
        if (SubCycleCount == repeats - 1)
            _stages[b + 23] = (float)(_stages[b + 24] + (double)_stages[b + 23]);
        else if (SubCycleCount == repeats)
            _stages[b + 21] = BitConverter.Int32BitsToSingle(0);
    }

    /// <summary>
    /// randy31's <c>Color_t::Interpolate</c> (<c>10155580</c>): a straight per-channel lerp of two
    /// packed ARGBs.
    /// </summary>
    public static uint StockColorInterpolate(uint from, uint to, float t)
    {
        uint result = 0;
        for (int shift = 0; shift < 32; shift += 8)
        {
            int a = (int)((from >> shift) & 0xff);
            int b = (int)((to >> shift) & 0xff);
            int v = (int)(a + (b - a) * (double)t + 0.5);
            if (v < 0)
                v = 0;
            if (v > 255)
                v = 255;
            result |= (uint)v << shift;
        }
        return result;
    }

    /// <summary>
    /// <c>100d71c6</c>: cone <paramref name="i"/>'s turn about the world y — its share of a full turn
    /// plus the stage's spin. Radians.
    /// </summary>
    public float ConeAngle(int i, float spin)
        => ConeCount == 0 ? spin : (float)(i * (double)FullTurn / ConeCount + spin);

    /// <summary><c>100d7265</c>: cone <paramref name="i"/>'s bottom radius.</summary>
    public float ConeBottomRadius(int i, float baseRadius)
        => (float)(BottomRadiusStep * (double)i + baseRadius);

    /// <summary><c>100d727b</c>: cone <paramref name="i"/>'s top radius.</summary>
    public float ConeTopRadius(int i, float baseRadius)
        => (float)(TopRadiusStep * (double)i + baseRadius);

    float F(int i) => i >= 0 && i < _fields.Length ? _fields[i] : 0f;

    int Int(int i)
        => i >= 0 && i < _fields.Length ? BitConverter.SingleToInt32Bits(_fields[i]) : 0;

    /// <summary>What one call hands every cone.</summary>
    public struct State
    {
        public float Height;
        public float TopRadius;
        public float BottomRadius;
        public float USpan, VSpan;
        public float UScroll, VScroll;
        public uint ColourA, ColourB;
        public float Spin;
        public float AnchorX, AnchorY, AnchorZ;
    }

    /// <summary>Port-only: the drawn state between two fixed steps.</summary>
    public static State Blend(State a, State b, float t) => new State
    {
        Height = a.Height + (b.Height - a.Height) * t,
        TopRadius = a.TopRadius + (b.TopRadius - a.TopRadius) * t,
        BottomRadius = a.BottomRadius + (b.BottomRadius - a.BottomRadius) * t,
        USpan = a.USpan + (b.USpan - a.USpan) * t,
        VSpan = a.VSpan + (b.VSpan - a.VSpan) * t,
        UScroll = a.UScroll + (b.UScroll - a.UScroll) * t,
        VScroll = a.VScroll + (b.VScroll - a.VScroll) * t,
        ColourA = StockColorInterpolate(a.ColourA, b.ColourA, t),
        ColourB = StockColorInterpolate(a.ColourB, b.ColourB, t),
        Spin = a.Spin + (b.Spin - a.Spin) * t,
        AnchorX = a.AnchorX + (b.AnchorX - a.AnchorX) * t,
        AnchorY = a.AnchorY + (b.AnchorY - a.AnchorY) * t,
        AnchorZ = a.AnchorZ + (b.AnchorZ - a.AnchorZ) * t,
    };
}
