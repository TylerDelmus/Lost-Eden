using System;

/// <summary>
/// Stock <c>_GfxControlGroundRing_t</c> (typeCode 3012, 0xbc4; vftable <c>Gamecode 1016d094</c>,
/// Process <c>100e1c7e</c>, field loader <c>100e20f6</c>, build <c>100e1f21</c>): a flat annulus laid
/// over the ground, its inner and outer rims following the terrain height under each point. Nothing
/// here depends on Unity.
///
/// Fields: 0 flags, 1-6 the locator offset and turn, 7 attach, 8 duration, 9 material, 10 the
/// segments round the ring, 11 and 12 two floats the visual takes, 13 the inner radius, 14 the outer
/// radius, 15 the fade-in, 16 the fade-out, 17 and 18 two packed ARGB colours, 19 the height the ring
/// is lifted above the ground.
///
/// <b>The ring is a strip of <c>2 * segments + 2</c> points</b>, an (inner, outer) pair per step with
/// i running to the segment count <i>inclusive</i>, so the last pair closes onto the first. Each
/// point's height is <c>ground(x, z) + field 19</c>, taken relative to the ring's centre because the
/// visual is placed at the centre.
///
/// <b>Flag 0x800 decides whether it moves.</b> With the bit, the build bakes the ring to the terrain
/// once and Process never touches the points again (<c>100e1dee</c> jumps straight to the visual
/// update) — the ring stays where it was spawned. Without it, Process re-reads the locator and
/// re-samples the ground under every point every call, so the ring follows its owner over the
/// terrain. Eight of the nine shipped records are baked; only 60005 follows.
///
/// <b>The uv walks the ring</b> (<c>10016eac</c>): u runs from 0 to field 11 around the
/// circumference, a step of <c>field 11 / segments</c> per pair, and v goes 0 on the inner rim to
/// field 12 on the outer — so the texture tiles along the band rather than being projected flat.
/// The other mode at <c>10016f73</c>, which multiplies the point's world x and z by the two fields,
/// is only taken when flags bit 8 is CLEAR, and no shipped record clears it.
///
/// <b>The colours do not cross-fade</b> the way Beam's or CrazyCone's do. Each of the two is its own
/// colour with only its <i>alpha</i> ramped: up from nothing over field 15, held exactly as the record
/// wrote it through the middle, and back down to nothing over field 16 (<c>100e1cda</c> /
/// <c>100e1d50</c> / <c>100e1d6b</c>). The middle stretch skips the interpolation entirely.
/// </summary>
public sealed class GroundRingSim
{
    /// <summary><c>100e1dee</c>: the ring is baked to the ground once, at spawn.</summary>
    public const int FlagBakeOnce = 0x800;

    /// <summary><c>100e1c98</c>: a dead locator does not end a control that has this bit.</summary>
    public const int FlagOutlivesLocator = 0x1000;

    /// <summary>Visual ctor argument 6 (<c>100e1f75</c>), bit 9 — the bit Spiral2 draws additive on.</summary>
    public const int FlagAdditive = 0x200;

    /// <summary>
    /// Visual ctor argument 4 (<c>100e1f96</c>), bit 8, stored at the visual's <c>+0x1b4</c>.
    /// <c>10016ea0</c> is <c>cmp [+0x1b4], 0</c> then <c>je</c>, so the jump is the ZERO case: with
    /// the bit SET the uv is walked round the ring (<c>10016eac</c>), and only a record without it
    /// would get the world-projected uv at <c>10016f73</c>. Every shipped record sets it.
    /// </summary>
    public const int FlagRingWalkUv = 0x100;

    /// <summary>Visual ctor argument 9 (<c>100e1f6b</c>), bit 10.</summary>
    public const int FlagVisualBit10 = 0x400;

    readonly float[] _fields;

    public int Flags { get; }
    public int Material { get; }
    public int Segments { get; }
    public float Duration { get; }

    /// <summary>Field 13: the rim nearer the centre.</summary>
    public float InnerRadius { get; }

    /// <summary>Field 14: the outer rim.</summary>
    public float OuterRadius { get; }

    /// <summary>Field 15: how long the alpha takes to come up.</summary>
    public float FadeIn { get; }

    /// <summary>Field 16: how long it takes to go.</summary>
    public float FadeOut { get; }

    /// <summary>Stock <c>+0x74</c>, set by the loader: <c>duration - field 16</c>.</summary>
    public float FadeOutStart { get; }

    /// <summary>Field 19: how far above the ground the ring floats.</summary>
    public float HeightOffset { get; }

    /// <summary>Field 11, the visual's <c>+0x1ac</c>: how often the texture repeats across u.</summary>
    public float USpan { get; }

    /// <summary>Field 12, the visual's <c>+0x1b0</c>: the same for v.</summary>
    public float VSpan { get; }

    public uint ColourA { get; }
    public uint ColourB { get; }

    /// <summary>Stock <c>+0x5c</c>..<c>+0x64</c>, the point the ring is centred on.</summary>
    public float CentreX { get; private set; }
    public float CentreY { get; private set; }
    public float CentreZ { get; private set; }

    /// <summary>The <c>2 * segments + 2</c> ring points, laid out x, y, z per point.</summary>
    public readonly float[] Points;

    public bool Dead { get; private set; }
    public bool Baked => (Flags & FlagBakeOnce) != 0;
    public bool OutlivesLocator => (Flags & FlagOutlivesLocator) != 0;
    public bool Additive => (Flags & FlagAdditive) != 0;

    /// <summary><c>10016ea0</c>: u walks round the ring and v crosses the band.</summary>
    public bool RingWalkUv => (Flags & FlagRingWalkUv) != 0;

    public int PointCount => Segments < 0 ? 0 : Segments * 2 + 2;

    /// <summary>What the last <see cref="Step"/> handed the visual.</summary>
    public uint CurrentA { get; private set; }
    public uint CurrentB { get; private set; }

    /// <summary>
    /// The ground height under a world point — stock's <c>100d33f3</c>, which asks n3Playfield.
    /// Null gives a flat ring at the centre's own height.
    /// </summary>
    public Func<float, float, float> GroundHeight { get; set; }

    public GroundRingSim(float[] fields)
    {
        _fields = fields ?? Array.Empty<float>();

        Flags = Int(0);
        Duration = F(8);
        Material = Int(9);
        Segments = Int(10);
        USpan = F(11);
        VSpan = F(12);
        InnerRadius = F(13);
        OuterRadius = F(14);
        FadeIn = F(15);
        FadeOut = F(16);
        ColourA = UInt(17);
        ColourB = UInt(18);
        HeightOffset = F(19);

        if (Segments < 0)
            Segments = 0;

        // 100e21df: the loader works out where the fade-out starts once.
        FadeOutStart = (float)(Duration - (double)FadeOut);

        Points = new float[PointCount * 3];
        CurrentA = ColourA;
        CurrentB = ColourB;
    }

    /// <summary>
    /// The build (<c>100e1f21</c>): place the ring on the locator and, when flag 0x800 is set, bake
    /// its points to the ground there and then.
    /// </summary>
    public void Build(float lx, float ly, float lz)
    {
        CentreX = lx;
        CentreY = ly;
        CentreZ = lz;
        if (Baked)
            SamplePoints();
    }

    /// <summary>
    /// One Process call (<c>100e1c7e</c>). <paramref name="locatorValid"/> is stock's
    /// <c>10106591</c> and the three floats its <c>10106306</c> position.
    /// </summary>
    public void Step(float age, bool locatorValid, float lx, float ly, float lz)
    {
        if (Dead)
            return;

        // 100e1c8f: an invalid locator ends it unless flag 0x1000 says otherwise.
        if (!locatorValid && !OutlivesLocator)
        {
            Dead = true;
            return;
        }

        // 100e1cc2: alpha up, then held exactly, then down.
        if (age < FadeIn)
        {
            float t = FadeIn != 0f ? (float)(age / (double)FadeIn) : 1f;
            CurrentA = Interpolate(ColourA & 0x00ffffffu, ColourA, t);
            CurrentB = Interpolate(ColourB & 0x00ffffffu, ColourB, t);
        }
        else if (age < FadeOutStart)
        {
            // 100e1d50: the middle is the record's own colours, with no interpolation at all.
            CurrentA = ColourA;
            CurrentB = ColourB;
        }
        else
        {
            float t = FadeOut != 0f ? (float)((age - (double)FadeOutStart) / FadeOut) : 1f;
            CurrentA = Interpolate(ColourA, ColourA & 0x00ffffffu, t);
            CurrentB = Interpolate(ColourB, ColourB & 0x00ffffffu, t);
        }

        // 100e1dee: a baked ring never moves again; the others follow and re-sample every call.
        if (Baked)
            return;

        if (locatorValid)
        {
            CentreX = lx;
            CentreY = ly;
            CentreZ = lz;
        }
        SamplePoints();
    }

    /// <summary>
    /// <c>100e1e46</c> (and the same loop in the build): an (inner, outer) pair per step, i running to
    /// the segment count inclusive so the ring closes, each point's height taken from the ground under
    /// it and stored relative to the centre.
    /// </summary>
    void SamplePoints()
    {
        if (Segments <= 0)
            return;

        float step = (float)(FullTurn / (double)Segments);
        float angle = 0f;

        for (int i = 0; i <= Segments; i++)
        {
            // x from the cosine and z from the sine, as stock's two calls order them.
            float c = (float)Math.Cos(angle);
            float s = (float)Math.Sin(angle);

            Place(i * 6, (float)(c * (double)InnerRadius), (float)(s * (double)InnerRadius));
            Place(i * 6 + 3, (float)(c * (double)OuterRadius), (float)(s * (double)OuterRadius));

            angle = (float)(angle + (double)step);
        }
    }

    void Place(int at, float dx, float dz)
    {
        float ground = GroundHeight != null
            ? GroundHeight((float)(CentreX + (double)dx), (float)(CentreZ + (double)dz))
            : CentreY;
        Points[at] = dx;
        // 100e1ea4: ground + field 19, then made relative to the centre the visual sits on.
        Points[at + 1] = (float)(ground + (double)HeightOffset - CentreY);
        Points[at + 2] = dz;
    }

    /// <summary>Stock's rounded full turn for the ring (<c>1015f810</c>), an exact two pi.</summary>
    public const double FullTurn = 6.2831854820251465;

    /// <summary>randy31's <c>Color_t::Interpolate</c> (<c>10155580</c>), a per-channel lerp.</summary>
    public static uint Interpolate(uint from, uint to, float t)
        => CrazyConeSim.StockColorInterpolate(from, to, t);

    float F(int i) => i >= 0 && i < _fields.Length ? _fields[i] : 0f;

    int Int(int i)
        => i >= 0 && i < _fields.Length ? BitConverter.SingleToInt32Bits(_fields[i]) : 0;

    uint UInt(int i) => unchecked((uint)Int(i));
}
