using System;
using Xunit;

/// <summary>
/// Locks <see cref="Spiral2Sim"/> and <see cref="Spiral2Ribbon"/> to stock GfxControlSpiral2_t
/// (loader <c>10111ebd</c>, Process <c>10111c40</c>, slot 6 <c>10111d75</c>) and its DisplaySystem
/// visual (ctor <c>1002270b</c>, geometry <c>10022407</c>), using records 71108 and 71125.
/// </summary>
public class Spiral2SimTests
{
    static float Bits(int i) => BitConverter.Int32BitsToSingle(i);

    /// <summary>
    /// Record 71108: flags 0x201 (additive, no wobble), 10 s, material 96, 5 ribbons of 25 segments,
    /// half width 0.5, height 3, start 0, sweep 3.5, u 10, spin -5, no acceleration, fades 0.9 / 0.1.
    /// Its radius curve is flat at 1 and its colour curve runs 0x004070c0 -> 0xff4070c0 -> 0xff4070c0.
    /// </summary>
    static float[] Record71108()
    {
        var f = new float[38];
        f[0] = Bits(0x201);
        f[8] = 10f;
        f[9] = Bits(96);
        f[10] = Bits(5);
        f[11] = Bits(25);
        f[12] = 4f; f[13] = 0.5f; f[14] = 3f; f[15] = 0f;
        f[16] = 3.5f; f[17] = 10f; f[18] = -5f; f[19] = 0f;
        f[20] = 0.9f; f[21] = 0.1f;
        f[22] = Bits(4);
        f[23] = 0f;    f[24] = Bits(0x004070c0);
        f[25] = 0.5f;  f[26] = Bits(unchecked((int)0xff4070c0));
        f[27] = 0.9f;  f[28] = Bits(unchecked((int)0xff4070c0));
        f[29] = 1f;    f[30] = Bits(0);
        f[31] = Bits(3);
        f[32] = 0f;    f[33] = 1f;
        f[34] = 0.9f;  f[35] = 1f;
        f[36] = 1f;    f[37] = 1f;
        return f;
    }

    [Fact]
    public void TheLoader_ReadsTheCountsTheAnglesAndBothCurves()
    {
        var sim = new Spiral2Sim(Record71108());
        Assert.Equal(0x201, sim.Flags);
        Assert.Equal(10f, sim.Duration, 4);
        Assert.Equal(96, sim.Material);
        Assert.Equal(25, sim.Segments);
        Assert.Equal(5, sim.Ribbons.Length);
        Assert.Equal(-5f, sim.InitialSpin, 4);
        Assert.Equal(0f, sim.AngularAcceleration, 4);
        Assert.Equal(4, sim.ColourCurve.Count);
        Assert.Equal(3, sim.RadiusCurve.Count);
        Assert.True(sim.Additive);
    }

    [Fact]
    public void TheRibbons_AreSpacedEvenlyRoundTheTurn()
    {
        var sim = new Spiral2Sim(Record71108());
        for (int i = 0; i < 5; i++)
            Assert.Equal(i * Spiral2Sim.FullTurn / 5f, sim.Ribbons[i].StartAngle, 4);
    }

    [Fact]
    public void AStartAngleOf999_IsRandomisedOverAFullTurn()
    {
        // 10112022: field 15 == 999 exactly, else the field is taken as written.
        float[] f = Record71108();
        f[15] = 999f;
        var sim = new Spiral2Sim(f, () => 0.25f);
        Assert.Equal(0.25f * Spiral2Sim.FullTurn, sim.BaseStartAngle, 4);
        Assert.Equal(sim.BaseStartAngle, sim.Ribbons[0].StartAngle, 4);

        f[15] = 998f;
        Assert.Equal(998f, new Spiral2Sim(f, () => 0.25f).BaseStartAngle, 4);
    }

    [Fact]
    public void TheAngleIntegratesBeforeTheSpinPicksUpTheAcceleration()
    {
        // 10111cb2: angle += spin * dt first, and only then spin += acceleration * dt.
        float[] f = Record71108();
        f[18] = 2f;   // spin
        f[19] = 10f;  // acceleration
        var sim = new Spiral2Sim(f);

        sim.Step(0f, 0.5f);
        Assert.Equal(1f, sim.Angle, 4);   // 2 * 0.5, the acceleration has not landed yet
        Assert.Equal(7f, sim.Spin, 4);    // 2 + 10 * 0.5

        sim.Step(0.5f, 0.5f);
        Assert.Equal(4.5f, sim.Angle, 4); // 1 + 7 * 0.5
        Assert.Equal(12f, sim.Spin, 4);
    }

    [Fact]
    public void ThePhaseIsAgeOverDurationAndIsNotClamped()
    {
        var sim = new Spiral2Sim(Record71108());
        Assert.Equal(0f, sim.Phase(0f), 4);
        Assert.Equal(0.5f, sim.Phase(5f), 4);
        Assert.Equal(1.2f, sim.Phase(12f), 4);
    }

    [Fact]
    public void TheRadiusAndColourComeFromTheCurvesAtThePhase()
    {
        var sim = new Spiral2Sim(Record71108());
        Assert.Equal(1f, sim.RadiusAt(0f), 4);
        Assert.Equal(1f, sim.RadiusAt(9f), 4);
        // The first colour key is transparent, the second is opaque at half way.
        Assert.Equal(0x00u, sim.ColourAt(0f) >> 24);
        Assert.Equal(0xffu, sim.ColourAt(5f) >> 24);
    }

    [Fact]
    public void TheGeometry_IsAHelixOfTwoVerticesPerSegment()
    {
        var sim = new Spiral2Sim(Record71108());
        Spiral2Ribbon ribbon = sim.Ribbons[0];
        Assert.Equal(50, ribbon.VertexCount);
        Assert.Equal(3.5f / 25f, ribbon.AngleStep, 5);

        ribbon.Build(0.5f, 2f, 0xffffffffu);

        // Vertex 0 is at the start angle 0: x = sin 0 = 0, z = cos 0 = radius.
        Assert.Equal(0f, ribbon.X[0], 4);
        Assert.Equal(2f, ribbon.Z[0], 4);
        // The pair straddles the centre by the half width and shares x, z and u.
        Assert.Equal(0.5f, ribbon.Y[0], 4);
        Assert.Equal(-0.5f, ribbon.Y[1], 4);
        Assert.Equal(ribbon.X[0], ribbon.X[1], 4);
        Assert.Equal(ribbon.U[0], ribbon.U[1], 4);
        // v is 0 on the upper vertex and 1 on the lower (10022591 / 100225fd).
        Assert.Equal(0f, ribbon.V[0], 4);
        Assert.Equal(1f, ribbon.V[1], 4);

        // The second pair has climbed height / segments and turned by one step.
        Assert.Equal(3f / 25f + 0.5f, ribbon.Y[2], 4);
        Assert.Equal((float)(Math.Sin(3.5 / 25) * 2), ribbon.X[2], 4);
        // u spans 0 .. uScale over the ribbon, one segment short of the end.
        Assert.Equal(0f, ribbon.U[0], 4);
        Assert.Equal(10f * 24f / 25f, ribbon.U[48], 3);
    }

    [Fact]
    public void TheFades_TaperEachEndAndTheFadeOutWins()
    {
        var sim = new Spiral2Sim(Record71108());
        Spiral2Ribbon ribbon = sim.Ribbons[0];  // fade in 0.1, fade out 0.9

        Assert.Equal(0f, ribbon.Fade(0f), 4);
        Assert.Equal(0.5f, ribbon.Fade(0.05f), 4);
        // Past 1 - 0.9 = 0.1 the fade-out takes over and overwrites the fade-in.
        Assert.Equal(1f, ribbon.Fade(0.1f), 4);
        Assert.Equal(0.5f, ribbon.Fade(0.55f), 4);
        Assert.Equal(0.01111f, ribbon.Fade(0.99f), 4);
    }

    [Fact]
    public void AZeroFadeWidth_LeavesThatEndAlone()
    {
        float[] f = Record71108();
        f[20] = 0f; f[21] = 0f;
        var ribbon = new Spiral2Sim(f).Ribbons[0];
        Assert.Equal(1f, ribbon.Fade(0f), 4);
        Assert.Equal(1f, ribbon.Fade(0.5f), 4);
        Assert.Equal(1f, ribbon.Fade(0.99f), 4);
    }

    [Fact]
    public void WithoutFlag400_EveryVertexTakesTheColoursOwnAlpha()
    {
        float[] f = Record71108();
        f[20] = 0f; f[21] = 0f;   // take the fades out of the way
        var ribbon = new Spiral2Sim(f).Ribbons[0];
        ribbon.Build(0.5f, 1f, 0x80112233u);

        for (int i = 0; i < ribbon.VertexCount; i++)
        {
            Assert.Equal(0x80u, ribbon.Colors[i] >> 24);
            Assert.Equal(0x112233u, ribbon.Colors[i] & 0xffffffu);
        }
    }

    [Fact]
    public void Flag400_WobblesTheAlphaAlongTheRibbonAndOverTime()
    {
        // 10022479: wobble = (sin(rate * t + theta * 4) + 1) / 2.
        float[] f = Record71108();
        f[0] = Bits(0x601);
        f[20] = 0f; f[21] = 0f;
        f[12] = 4f;
        var ribbon = new Spiral2Sim(f).Ribbons[0];

        ribbon.Build(0f, 1f, 0xffffffffu);
        // At t = 0 and theta = 0 the sine is 0, so the wobble is exactly a half.
        Assert.Equal((uint)(int)(255.0 * 0.5), ribbon.Colors[0] >> 24);

        double theta = 3.5 / 25.0;
        Assert.Equal((uint)(int)(255.0 * (Math.Sin(theta * 4.0) + 1.0) * 0.5), ribbon.Colors[2] >> 24);

        // The same vertex moves on with the phase.
        ribbon.Build(0.25f, 1f, 0xffffffffu);
        Assert.Equal((uint)(int)(255.0 * (Math.Sin(4.0 * 0.25) + 1.0) * 0.5), ribbon.Colors[0] >> 24);
    }

    [Fact]
    public void TerminateGracefully_HoldsBothCurvesThenFadesOverOneSecond()
    {
        var sim = new Spiral2Sim(Record71108());
        sim.TerminateGracefully(4f);

        // 10111d96: the duration comes in to age + 1.
        Assert.Equal(5f, sim.Duration, 4);
        Assert.Equal(3, sim.ColourCurve.Count);
        Assert.Equal(3, sim.RadiusCurve.Count);

        // Both replacements hold the curve's value at t = 0 (10111da2 / 10111e21 push zero) until
        // age / the new duration, which is 0.8 here.
        Assert.Equal(1f, sim.RadiusAt(0f), 4);
        Assert.Equal(1f, sim.RadiusAt(4f), 4);
        Assert.Equal(2f, sim.RadiusAt(5f), 4);       // one unit wider at the end

        Assert.Equal(0x004070c0u, sim.ColourAt(0f));
        Assert.Equal(0x004070c0u, sim.ColourAt(4f));
        Assert.Equal(0u, sim.ColourAt(5f));
    }

    [Fact]
    public void TerminateGracefully_DoesNothingInsideTheLastSecond()
    {
        // 10111d8d: stock skips the whole body once age > duration - 1 and lets the timer run out.
        var sim = new Spiral2Sim(Record71108());
        sim.TerminateGracefully(9.5f);
        Assert.Equal(10f, sim.Duration, 4);
        Assert.Equal(4, sim.ColourCurve.Count);

        // Exactly on the boundary stock still rewrites (the compare is not strict).
        sim.TerminateGracefully(9f);
        Assert.Equal(10f, sim.Duration, 4);
        Assert.Equal(3, sim.ColourCurve.Count);
    }

    [Fact]
    public void ARealRecord_SevenRibbonsWithARandomStart()
    {
        // 71125: 20 s, 7 ribbons of 50 segments, start 999, spin 2 accelerating by 1, radius flat at 3.
        var f = new float[36];
        f[0] = Bits(0x601);
        f[8] = 20f;
        f[9] = Bits(96);
        f[10] = Bits(7);
        f[11] = Bits(50);
        f[12] = 80f; f[13] = 1f; f[14] = 4f; f[15] = 999f;
        f[16] = 6f; f[17] = 10f; f[18] = 2f; f[19] = 1f;
        f[20] = 0.3f; f[21] = 0.3f;
        f[22] = Bits(4);
        f[23] = 0f;    f[24] = Bits(0x0070a0ff);
        f[25] = 0.4f;  f[26] = Bits(unchecked((int)0xff70a0ff));
        f[27] = 0.8f;  f[28] = Bits(unchecked((int)0xff70a0ff));
        f[29] = 1f;    f[30] = Bits(0);
        f[31] = Bits(2);
        f[32] = 0f;    f[33] = 3f;
        f[34] = 1f;    f[35] = 3f;

        var sim = new Spiral2Sim(f, () => 0.5f);
        Assert.Equal(7, sim.Ribbons.Length);
        Assert.Equal(50, sim.Segments);
        Assert.Equal(100, sim.Ribbons[0].VertexCount);
        Assert.Equal(0.5f * Spiral2Sim.FullTurn, sim.BaseStartAngle, 4);
        Assert.Equal(3f, sim.RadiusAt(10f), 4);
        Assert.Equal(6f / 50f, sim.Ribbons[3].AngleStep, 5);
        // Field 14 is the climb over the whole ribbon, not per segment.
        Assert.Equal(4f, sim.Ribbons[0].Height, 4);
    }
}
