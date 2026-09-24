using System;
using Xunit;

/// <summary>
/// Locks <see cref="CrazyConeSim"/> and <see cref="ConeFrustum"/> to stock
/// <c>_GfxControlCrazyCone_t</c> (<c>Gamecode 100d6dcc</c>) and <c>GfxVisualCone</c>
/// (<c>DisplaySystem 1000bd0a</c>), using the shipped records 61021 (1 stage) and 60001 (2 stages).
/// </summary>
public class CrazyConeSimTests
{
    static float I(int v) => BitConverter.Int32BitsToSingle(v);
    static float U(uint v) => BitConverter.Int32BitsToSingle(unchecked((int)v));

    /// <summary>A record: the 21-field header followed by <paramref name="stages"/> blocks of 29.</summary>
    static float[] Record(float[] header, params float[][] stages)
    {
        var f = new float[CrazyConeSim.HeaderFields + stages.Length * CrazyConeSim.StageFields];
        Array.Copy(header, f, header.Length);
        for (int s = 0; s < stages.Length; s++)
            Array.Copy(stages[s], 0, f, CrazyConeSim.HeaderFields + s * CrazyConeSim.StageFields,
                stages[s].Length);
        return f;
    }

    /// <summary>
    /// Record 61021: flags 8707 = <b>0x2203</b> (the record dump prints flags in decimal), material
    /// 41, 1 cone, 32 segments, steps 0.3/0.3, u/v 15/-1, radii 0, lift 0.2.
    /// </summary>
    static float[] Header61021()
    {
        var h = new float[CrazyConeSim.HeaderFields];
        h[0] = I(0x2203);
        h[8] = 1f;
        h[9] = I(41);
        h[10] = I(1);       // one cone
        h[11] = I(32);      // 32 segments
        h[12] = 0.3f;       // top radius per cone
        h[13] = 0.3f;       // bottom radius per cone
        h[14] = 15f;        // u span
        h[15] = -1f;        // v span
        h[16] = 0f;         // top radius
        h[17] = 0f;         // bottom radius
        h[18] = 0f;         // top height
        h[19] = 0.2f;       // lift
        h[20] = I(1);       // overwritten by Make from the stages it is given
        return h;
    }

    /// <summary>A stage: duration 4, and whatever rates the test needs.</summary>
    static float[] Stage(
        float duration, float topRate = 0f, float botRate = 0f, float heightRate = 0f,
        float liftRate = 0f, float uRate = 0f, float vRate = 0f, float spin = 0f,
        uint c17 = 0xffffffff, uint c18 = 0xffffffff, uint c19 = 0xffffffff, uint c20 = 0xffffffff,
        int repeats = 0)
    {
        var s = new float[CrazyConeSim.StageFields];
        s[0] = duration;
        // Values 3-9, rates 10-16, paired 3<-14, 4<-15, 5<-10, 6<-11, 7<-12, 8<-13, 9<-16.
        s[3] = uRate;        // feeds the u scroll
        s[4] = vRate;        // feeds the v scroll
        s[5] = heightRate;   // feeds field 18
        s[6] = liftRate;     // feeds field 19
        s[7] = topRate;      // feeds field 16
        s[8] = botRate;      // feeds field 17
        s[9] = spin;
        s[17] = U(c17);
        s[18] = U(c18);
        s[19] = U(c19);
        s[20] = U(c20);
        s[21] = I(repeats);
        return s;
    }

    static CrazyConeSim Make(params float[][] stages) => MakeWith(Header61021(), stages);

    /// <summary>Field 20 always matches the number of stage blocks actually written.</summary>
    static CrazyConeSim MakeWith(float[] header, params float[][] stages)
    {
        header[20] = I(stages.Length);
        return new CrazyConeSim(Record(header, stages));
    }

    static void Step(CrazyConeSim c, float dt) => c.Step(dt, true, 0f, 0f, 0f);

    [Fact]
    public void TheRecordIsAHeaderPlusStagesOfTwentyNine()
    {
        // The shipped counts are 21 + 29n: 50, 79, 108, 137.
        Assert.Equal(50, CrazyConeSim.HeaderFields + 1 * CrazyConeSim.StageFields);
        Assert.Equal(79, CrazyConeSim.HeaderFields + 2 * CrazyConeSim.StageFields);
        Assert.Equal(108, CrazyConeSim.HeaderFields + 3 * CrazyConeSim.StageFields);
        Assert.Equal(137, CrazyConeSim.HeaderFields + 4 * CrazyConeSim.StageFields);
    }

    [Fact]
    public void FieldTenIsTheConesAndFieldTwentyTheStages()
    {
        CrazyConeSim c = Make(Stage(4f), Stage(2f));
        Assert.Equal(1, c.ConeCount);
        Assert.Equal(2, c.StageCount);
        Assert.Equal(32, c.Segments);
    }

    [Fact]
    public void StageSlotZeroIsItsDuration()
    {
        CrazyConeSim c = Make(Stage(4f));
        Assert.Equal(4f, c.Stage(0, 0), 4);
        Step(c, 1f);
        Assert.Equal(1f, c.StageElapsed, 4);
        Assert.Equal(0, c.StageIndex);
    }

    [Fact]
    public void RunningOutOfStagesEndsIt()
    {
        // 100d6dea: the control ends on the call AFTER the last stage is spent.
        CrazyConeSim c = Make(Stage(1f));
        Step(c, 0.5f);
        Assert.False(c.Dead);
        Step(c, 0.6f);          // the stage completes; index moves past the end
        Assert.False(c.Dead);
        Step(c, 0.1f);          // now it notices
        Assert.True(c.Dead);
    }

    [Fact]
    public void AStageThatFinishesCarriesTheLeftoverIntoTheNext()
    {
        // 100d6e21: dt -= leftover, elapsed = 0, and the next stage picks up the rest.
        CrazyConeSim c = Make(Stage(1f), Stage(5f));
        Step(c, 1.25f);
        Assert.Equal(1, c.StageIndex);
        Assert.Equal(0.25f, c.StageElapsed, 4);
    }

    [Fact]
    public void TheRatesIntegrateIntoTheHeaderFields()
    {
        // 100d6f12: the stage's values drive the record's own radii and height.
        CrazyConeSim c = Make(Stage(10f, topRate: 2f, botRate: 3f, heightRate: 4f));
        Step(c, 1f);
        // value += rate * dt happens first, so after one second of dt the value is rate * dt and the
        // header has taken that value * dt on the same call.
        Assert.Equal(2f, c.Current.TopRadius, 4);
        Assert.Equal(3f, c.Current.BottomRadius, 4);
        // Height is field 18 minus field 19; 18 has grown by 4 and 19 is still the record's 0.2.
        Assert.Equal(4f - 0.2f, c.Current.Height, 4);
    }

    [Fact]
    public void TheIntegrationKeepsAccelerating()
    {
        // The rate itself is a value that the next pair integrates, so it compounds.
        CrazyConeSim c = Make(Stage(10f, topRate: 1f));
        Step(c, 1f);
        float first = c.Current.TopRadius;
        Step(c, 1f);
        float second = c.Current.TopRadius;
        Assert.Equal(1f, first, 4);
        Assert.Equal(2f, second, 4);
    }

    [Fact]
    public void TheScrollOffsetsComeFromSlotsThreeAndFour()
    {
        CrazyConeSim c = Make(Stage(10f, uRate: 5f, vRate: -2f));
        Step(c, 1f);
        Assert.Equal(5f, c.Current.UScroll, 4);
        Assert.Equal(-2f, c.Current.VScroll, 4);
    }

    [Fact]
    public void TheSpinIsTheElapsedTimeTimesSlotNine()
    {
        // 100d71fb.
        CrazyConeSim c = Make(Stage(10f, spin: 0.5f));
        Step(c, 2f);
        Assert.Equal(1f, c.Current.Spin, 4);
    }

    [Fact]
    public void TheColoursRampOverTheStagesProgress()
    {
        // 100d6f8d: Interpolate(slot17, slot19, t) and Interpolate(slot18, slot20, t).
        CrazyConeSim c = Make(Stage(4f, c17: 0x00000000, c19: 0xffffffff,
                                        c18: 0xff000000, c20: 0xff0000ff));
        Step(c, 2f);        // halfway
        Assert.Equal(0x80808080u, c.Current.ColourA);
        Assert.Equal(0xff000080u, c.Current.ColourB);
    }

    [Fact]
    public void ConeRadiiStepPerCone()
    {
        // 100d7265 / 100d727b: field 13 steps the bottom and field 12 the top.
        CrazyConeSim c = Make(Stage(4f));
        Assert.Equal(0f, c.ConeBottomRadius(0, 0f), 4);
        Assert.Equal(0.3f, c.ConeBottomRadius(1, 0f), 4);
        Assert.Equal(0.9f, c.ConeTopRadius(3, 0f), 4);
        Assert.Equal(1.6f, c.ConeTopRadius(2, 1f), 4);
    }

    [Fact]
    public void ConesShareAFullTurnBetweenThem()
    {
        // 100d71c6: i * 6.28 / coneCount, plus the spin.
        var header = Header61021();
        header[10] = I(4);      // four cones
        CrazyConeSim c = MakeWith(header, Stage(4f));
        Assert.Equal(0f, c.ConeAngle(0, 0f), 4);
        Assert.Equal(CrazyConeSim.FullTurn / 4f, c.ConeAngle(1, 0f), 4);
        Assert.Equal(CrazyConeSim.FullTurn / 2f, c.ConeAngle(2, 0f), 4);
        // The spin is added on top of the share.
        Assert.Equal(CrazyConeSim.FullTurn / 4f + 1f, c.ConeAngle(1, 1f), 4);
    }

    [Fact]
    public void ADeadLocatorEndsItUnlessFlagTwoThousandIsSet()
    {
        // 100d715e. 61021's 0x2203 HAS the bit, so it survives its locator.
        CrazyConeSim tough = Make(Stage(10f));
        Assert.True(tough.OutlivesLocator);
        tough.Step(0.1f, false, 0f, 0f, 0f);
        Assert.False(tough.Dead);

        // 61081's 3587 = 0xe03 does not, so it ends.
        var header = Header61021();
        header[0] = I(0xe03);
        CrazyConeSim plain = MakeWith(header, Stage(10f));
        Assert.False(plain.OutlivesLocator);
        plain.Step(0.1f, false, 0f, 0f, 0f);
        Assert.True(plain.Dead);
    }

    [Fact]
    public void TheAnchorIsLiftedByFieldNineteen()
    {
        // 100d7194.
        CrazyConeSim c = Make(Stage(10f));
        c.Step(0.1f, true, 1f, 5f, 2f);
        Assert.Equal(1f, c.Current.AnchorX, 4);
        Assert.Equal(5.2f, c.Current.AnchorY, 4);   // the record's field 19 is 0.2
        Assert.Equal(2f, c.Current.AnchorZ, 4);
    }

    [Fact]
    public void TheGroundSnapReplacesTheAnchorHeight()
    {
        // 100d74db / 100d718a: flag 0x800, which 61081 (3587 = 0xe03) has and 61021 does not.
        var header = Header61021();
        header[0] = I(0xe03);
        CrazyConeSim c = MakeWith(header, Stage(10f));
        Assert.Equal(CrazyConeSim.FlagGroundSnap, c.Flags & CrazyConeSim.FlagGroundSnap);
        c.GroundHeight = (x, z) => -3f;
        c.Step(0.1f, true, 0f, 40f, 0f);
        Assert.Equal(-2.8f, c.Current.AnchorY, 4);  // -3 plus the 0.2 lift
    }

    [Fact]
    public void ColourInterpolateIsAPlainPerChannelLerp()
    {
        Assert.Equal(0x80808080u, CrazyConeSim.StockColorInterpolate(0, 0xffffffff, 0.5f));
        Assert.Equal(0xffffffffu, CrazyConeSim.StockColorInterpolate(0, 0xffffffff, 1f));
        Assert.Equal(0x00000000u, CrazyConeSim.StockColorInterpolate(0, 0xffffffff, 0f));
    }

    // ---- ConeFrustum ----

    [Fact]
    public void AConeIsTwoNPlusTwoVertices()
    {
        // 1000c1eb.
        Assert.Equal(66, new ConeFrustum(32, false).VertexCount);
        Assert.Equal(10, new ConeFrustum(4, false).VertexCount);
    }

    [Fact]
    public void ItRunsFromTheBottomRingToTheTopRing()
    {
        var cone = new ConeFrustum(4, false);
        cone.Build(2f, 3f, 5f, 0xffff0000, 0xff0000ff, 1f, 1f, 0f, 0f);

        // Pair 0 is at angle 0, so x is the radius and z is nothing (stock takes x from the COSINE
        // table and z from the sine, the opposite way round to Beam).
        Assert.Equal(2f, cone.X[0], 4);
        Assert.Equal(0f, cone.Y[0], 4);
        Assert.Equal(0f, cone.Z[0], 4);
        Assert.Equal(0xffff0000u, cone.Colors[0]);

        Assert.Equal(3f, cone.X[1], 4);
        Assert.Equal(5f, cone.Y[1], 4);
        Assert.Equal(0f, cone.Z[1], 4);
        Assert.Equal(0xff0000ffu, cone.Colors[1]);

        // A quarter of the way round, x is nothing and z is the radius.
        Assert.Equal(0f, cone.X[2], 3);
        Assert.Equal(2f, cone.Z[2], 3);
    }

    [Fact]
    public void TheLastPairClosesOntoTheFirst()
    {
        var cone = new ConeFrustum(4, false);
        cone.Build(2f, 3f, 5f, 0xffffffff, 0xffffffff, 1f, 1f, 0f, 0f);
        int last = cone.VertexCount - 2;
        Assert.Equal(cone.X[0], cone.X[last], 3);
        Assert.Equal(cone.Z[0], cone.Z[last], 3);
    }

    [Fact]
    public void URunsRoundTheRingAndVUpTheSide()
    {
        // 1000be08.
        var cone = new ConeFrustum(4, false);
        cone.Build(2f, 3f, 5f, 0xffffffff, 0xffffffff, uSpan: 8f, vSpan: 2f, uOffset: 0f, vOffset: 0f);
        Assert.Equal(0f, cone.U[0], 4);
        Assert.Equal(0f, cone.V[0], 4);
        Assert.Equal(0f, cone.U[1], 4);
        Assert.Equal(2f, cone.V[1], 4);     // the top of the first pair is a whole v span up
        Assert.Equal(2f, cone.U[2], 4);     // 8 / 4 segments per step
        Assert.Equal(8f, cone.U[cone.VertexCount - 2], 4);
    }

    [Fact]
    public void SwappingUvPutsVRoundTheRingInstead()
    {
        // 1000be96.
        var cone = new ConeFrustum(4, true);
        cone.Build(2f, 3f, 5f, 0xffffffff, 0xffffffff, uSpan: 8f, vSpan: 2f, uOffset: 0f, vOffset: 0f);
        Assert.Equal(0f, cone.U[0], 4);
        Assert.Equal(8f, cone.U[1], 4);     // the top vertex is a whole u span across
        Assert.Equal(0f, cone.V[0], 4);
        Assert.Equal(0.5f, cone.V[2], 4);   // 2 / 4 segments per step
    }

    [Fact]
    public void TheOffsetsScrollTheWholeTexture()
    {
        var plain = new ConeFrustum(4, false);
        plain.Build(2f, 3f, 5f, 0xffffffff, 0xffffffff, 8f, 2f, 0f, 0f);
        var moved = new ConeFrustum(4, false);
        moved.Build(2f, 3f, 5f, 0xffffffff, 0xffffffff, 8f, 2f, 1.5f, -0.25f);

        for (int i = 0; i < plain.VertexCount; i++)
        {
            Assert.Equal(plain.U[i] + 1.5f, moved.U[i], 4);
            Assert.Equal(plain.V[i] - 0.25f, moved.V[i], 4);
        }
    }
}
