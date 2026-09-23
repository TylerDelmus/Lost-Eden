using System;
using Xunit;

/// <summary>
/// Locks <see cref="BeamSim"/> and <see cref="BeamBlades"/> to stock <c>GfxControlBeam_t</c>
/// (<c>Gamecode 10109c0c</c>) and <c>GfxVisualBeam</c> (<c>DisplaySystem 10008056</c>), using the
/// shipped records: 71106 (a 6 s wobbling beam), 71500 (a 0.27 s flash with a random turn), 72060
/// (a 24-take stutter) and 71862 (the one record whose anchor falls).
/// </summary>
public class BeamSimTests
{
    /// <summary>A record's 42 fields, ints written as their bit pattern the way gfxtweak stores them.</summary>
    static float[] Fields(
        int flags, float duration, int material, int fallGate, float fadeIn, float fadeOut,
        int repeats, int blades, float uMax, float vSpan, float length,
        float footStart, float tipStart, float footHold, float tipHold, float footEnd, float tipEnd,
        uint c1Start, uint c2Start, uint c1Hold, uint c2Hold, uint c1End, uint c2End,
        float spin, float footJitter, float tipJitter, float footSwing, float tipSwing,
        float footRate, float tipRate, float footPhase, float tipPhase,
        int secondMaterial, float secondHeight, float gap)
    {
        var f = new float[42];
        f[0] = I(flags);
        f[8] = duration;
        f[9] = I(material);
        f[10] = I(fallGate);
        f[11] = fadeIn;
        f[12] = fadeOut;
        f[13] = I(repeats);
        f[14] = I(blades);
        f[15] = uMax;
        f[16] = vSpan;
        f[17] = length;
        f[18] = footStart; f[19] = tipStart;
        f[20] = footHold; f[21] = tipHold;
        f[22] = footEnd; f[23] = tipEnd;
        f[24] = U(c1Start); f[25] = U(c2Start);
        f[26] = U(c1Hold); f[27] = U(c2Hold);
        f[28] = U(c1End); f[29] = U(c2End);
        f[30] = spin;
        f[31] = footJitter; f[32] = tipJitter;
        f[33] = footSwing; f[34] = tipSwing;
        f[35] = footRate; f[36] = tipRate;
        f[37] = footPhase; f[38] = tipPhase;
        f[39] = I(secondMaterial);
        f[40] = secondHeight;
        f[41] = gap;
        return f;
    }

    static float I(int v) => BitConverter.Int32BitsToSingle(v);
    static float U(uint v) => BitConverter.Int32BitsToSingle(unchecked((int)v));

    /// <summary>Record 71106: flags 0x30e07, 6 s, 8 blades, one colour ramp 0x002050a0 - 0x602050a0.</summary>
    static float[] R71106() => Fields(
        0x30e07, 6f, 74, 1, 3f, 3f, 1, 8, 1f, 1f, 10f,
        2f, 2f, 2f, 2f, 2f, 2f,
        0x002050a0, 0x002050a0, 0x602050a0, 0x602050a0, 0x002050a0, 0x002050a0,
        0.2f, 0f, 0f, 0.25f, 0.25f, 0.1f, 0.1f, 0f, 45f,
        -1, 0.2f, 0f);

    /// <summary>Record 71500: duration -1, so it lasts fadeIn + fadeOut; field 30 is 999.</summary>
    static float[] R71500() => Fields(
        0x19b03, -1f, 59, 1, 0.02f, 0.25f, 1, 2, 1f, 1f, 10f,
        0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f,
        0x00ffffff, 0x00ffffff, 0xffffffff, 0xffffffff, 0x00ffffff, 0x00ffffff,
        999f, 1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f,
        -1, 0.2f, 0f);

    /// <summary>Record 72060: 24 takes of 0.02 s with a 0.04 s gap, and a second material.</summary>
    static float[] R72060() => Fields(
        0x10b07, 0.02f, 60, 1, 0f, 0f, 24, 4, 1f, 1f, 0.65f,
        0.12f, 0.12f, 0.12f, 0.12f, 0.12f, 0.12f,
        0x00ffffff, 0x00ffffff, 0xffffffff, 0xffffffff, 0x00ffffff, 0x00ffffff,
        0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f,
        61, 0.2f, 0.04f);

    /// <summary>Record 71862: no 0x800, so it never refills its anchor and the drop accumulates.</summary>
    static float[] R71862() => Fields(
        0x00303, -1f, 1, 0, 0.5f, 0f, 1, 8, 1f, 1f, 50f,
        0f, 0f, 4f, 0f, 0f, 0f,
        0x00ffffff, 0x05ffffff, 0x22ffffff, 0x88ffffff, 0x00ffffff, 0x05ffffff,
        3f, 1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f,
        -1, 0.2f, 0f);

    static BeamSim Make(float[] fields, Func<float> random = null)
        => new BeamSim(fields, random ?? (() => 0f));

    static void Step(BeamSim sim, float dt) => sim.Step(dt, true, 0f, 0f, 0f);

    [Fact]
    public void ADurationOfZeroOrLessIsTheTwoFades()
    {
        // 101099fa: 71500 is -1, 0.02 and 0.25.
        Assert.Equal(0.27f, Make(R71500()).Duration, 5);
        // 71862 is -1, 0.5 and 0.
        Assert.Equal(0.5f, Make(R71862()).Duration, 5);
        // A positive field 8 is taken as it stands.
        Assert.Equal(6f, Make(R71106()).Duration, 5);
    }

    [Fact]
    public void TheHoldValuesAreWhatItSettlesOn()
    {
        // 72060 has no fades at all, so its whole take is the hold.
        BeamSim sim = Make(R72060());
        Step(sim, 0.01f);
        Assert.Equal(0.12f, sim.Current.Foot, 4);
        Assert.Equal(0xffffffff, sim.Current.Colour1);
        Assert.Equal(0xffffffff, sim.Current.Colour2);
    }

    [Fact]
    public void MostRecordsHaveNoHoldAtAll()
    {
        // 71500's two fades add up to its whole duration (0.02 + 0.25 = 0.27), so it rises for a
        // fiftieth of a second and falls for the rest — there is no steady part to reach. Same for
        // 71106 (3 + 3 = 6) and 71064. At 0.1 s it is already a third of the way down.
        BeamSim sim = Make(R71500());
        Step(sim, 0.1f);
        Assert.Equal(0xadu, sim.Current.Colour1 >> 24);
    }

    [Fact]
    public void TheFadeInRunsTheFirstRampOverFieldEleven()
    {
        // 10109e06: u = t / f11, colour = hold·u + start·(1-u). 71106's alpha goes 0x00 to 0x60.
        BeamSim sim = Make(R71106());
        Step(sim, 1.5f);        // half of the 3 s fade-in
        uint alpha = sim.Current.Colour1 >> 24;
        Assert.InRange(alpha, 0x2fu, 0x31u);
        Assert.Equal(0x2050a0u, sim.Current.Colour1 & 0xffffffu);
    }

    [Fact]
    public void TheFadeOutRunsTheSecondRampOverFieldTwelve()
    {
        // 10109efb: it starts at duration - f12 and reaches the end colour at the duration.
        BeamSim sim = Make(R71106());
        Step(sim, 3f);
        Assert.Equal(0x60u, sim.Current.Colour1 >> 24);
        Step(sim, 1.5f);        // 4.5 s: halfway down the 3 s fade-out
        uint alpha = sim.Current.Colour1 >> 24;
        Assert.InRange(alpha, 0x2fu, 0x31u);
        Step(sim, 1.49f);       // 5.99 s
        Assert.InRange(sim.Current.Colour1 >> 24, 0u, 1u);
    }

    [Fact]
    public void TheRadiiFollowTheSameThreeStops()
    {
        // 71862: the foot goes 0 -> 4 over the fade-in while the tip stays at 0, so it is a cone.
        BeamSim sim = Make(R71862());
        Step(sim, 0.25f);       // halfway through the 0.5 s fade-in
        Assert.Equal(2f, sim.Current.Foot, 4);
        Assert.Equal(0f, sim.Current.Tip, 4);
    }

    [Fact]
    public void OneTakeEndsTheControl()
    {
        // 10109ca5: field 13 is 1, so the first expiry spends it and there is nothing left.
        BeamSim sim = Make(R71106());
        Step(sim, 5.9f);
        Assert.False(sim.Dead);
        Step(sim, 0.2f);
        Assert.True(sim.Dead);
    }

    [Fact]
    public void TheGapBlanksBothColoursAndKeepsItAlive()
    {
        // 10109c74: past the duration but inside field 41 the colours go to nothing.
        BeamSim sim = Make(R72060());
        Step(sim, 0.03f);       // past the 0.02 s take, inside the 0.04 s gap
        Assert.True(sim.Blanked);
        Assert.False(sim.Dead);
        Assert.Equal(0u, sim.Current.Colour1);
        Assert.Equal(0u, sim.Current.Colour2);
    }

    [Fact]
    public void ATakeRestartsTheAgeAndSpendsARepeat()
    {
        BeamSim sim = Make(R72060());
        Assert.Equal(24, sim.RepeatsLeft);
        Step(sim, 0.03f);       // in the gap
        Assert.Equal(24, sim.RepeatsLeft);
        Step(sim, 0.04f);       // 0.07 s, past duration + gap
        Assert.Equal(23, sim.RepeatsLeft);
        Assert.Equal(0f, sim.CycleAge, 5);
        Assert.False(sim.Blanked);
    }

    [Fact]
    public void ItDiesWhenTheLastRepeatIsSpent()
    {
        BeamSim sim = Make(R72060());
        for (int i = 0; i < 200 && !sim.Dead; i++)
            Step(sim, 1f / 30f);
        Assert.True(sim.Dead);
        Assert.Equal(0, sim.RepeatsLeft);
    }

    [Fact]
    public void AFieldThirtyOfNineNineNineHoldsOneRandomAngleForTheTake()
    {
        // 10109cbe: the angle is re-picked per take and does not turn within one.
        float[] r = R71500();
        var sim = new BeamSim(r, () => 0.25f);
        Assert.Equal(0.25f * BeamSim.RandomAngleScale, sim.RandomAngle, 4);
        Step(sim, 0.05f);
        float a = sim.Current.Angle;
        Step(sim, 0.05f);
        Assert.Equal(a, sim.Current.Angle, 5);
    }

    [Fact]
    public void OtherwiseTheAngleIsFieldThirtyTimesTheAge()
    {
        BeamSim sim = Make(R71106());
        Step(sim, 2f);
        Assert.Equal(0.4f, sim.Current.Angle, 4);   // 0.2 rad/s
    }

    [Fact]
    public void TheWobbleAddsATravellingSineToEachRadius()
    {
        // 10109fe2: foot += sin(2·pi·f35·t + f37) · f33. 71106: rate 0.1, swing 0.25, phase 0.
        BeamSim sim = Make(R71106());
        Step(sim, 2.5f);
        double expected = 2.0 + Math.Sin(2.0 * Math.PI * 0.1 * 2.5) * 0.25;
        Assert.Equal((float)expected, sim.Current.Foot, 3);
    }

    [Fact]
    public void FieldsThirtySevenAndThirtyEightAreDegrees()
    {
        // 10109844: 45 degrees becomes a quarter of pi, so the tip's sine starts at its peak.
        BeamSim sim = Make(R71106());
        Step(sim, 0f);
        double expected = 2.0 + Math.Sin(45.0 * BeamSim.DegreesToRadians) * 0.25;
        Assert.Equal((float)expected, sim.Current.Tip, 3);
    }

    [Fact]
    public void TheWobbleJitterUsesAFreshRandomEveryCall()
    {
        // 71862 has flag 0x400 off, so take 71106's shape with a jitter switched on instead.
        float[] r = R71106();
        r[31] = 1f;             // field 31: a full unit of jitter on the foot
        int calls = 0;
        var sim = new BeamSim(r, () => { calls++; return 0.5f; });
        Step(sim, 1f);
        Assert.True(calls >= 2, $"only {calls} randoms drawn");
        Assert.True(sim.Current.Foot > 1.4f);
    }

    [Fact]
    public void TheAnchorFallsOnlyWhenFieldTenIsZeroAndNothingRefillsIt()
    {
        // 10109db4: 71862 has field 10 at 0 and no 0x800, so the drop accumulates.
        // 14 steps, because the 15th reaches the 0.5 s duration and ends the control before the body.
        BeamSim falling = Make(R71862());
        for (int i = 0; i < 14; i++)
            falling.Step(1f / 30f, true, 0f, 0f, 0f);
        Assert.True(falling.AnchorY < -80f, $"only fell to {falling.AnchorY}");
        Assert.Equal(BeamSim.FallAcceleration * 14f / 30f, falling.FallVelocity, 1);

        // 71106 has field 10 at 1, so it never moves.
        BeamSim still = Make(R71106());
        for (int i = 0; i < 15; i++)
            still.Step(1f / 30f, true, 0f, 7f, 0f);
        Assert.Equal(7f, still.AnchorY, 4);
    }

    [Fact]
    public void FollowingTheLocatorTakesItsPositionEveryCall()
    {
        BeamSim sim = Make(R71106());
        sim.Step(0.1f, true, 1f, 2f, 3f);
        Assert.Equal(1f, sim.Current.AnchorX, 4);
        Assert.Equal(2f, sim.Current.AnchorY, 4);
        Assert.Equal(3f, sim.Current.AnchorZ, 4);
    }

    [Fact]
    public void ALocatorThatStopsResolvingEndsIt()
    {
        // 10109d21.
        BeamSim sim = Make(R71106());
        sim.Step(0.1f, false, 0f, 0f, 0f);
        Assert.True(sim.Dead);
    }

    [Fact]
    public void ARecordWithoutTheFollowBitIgnoresTheLocator()
    {
        // 71862 has no 0x800, so an unresolvable locator does not stop it.
        BeamSim sim = Make(R71862());
        sim.Step(0.1f, false, 5f, 5f, 5f);
        Assert.False(sim.Dead);
        Assert.Equal(0f, sim.Current.AnchorX, 4);
    }

    [Fact]
    public void TerminatingGivesItThreeSecondsOfFadeOut()
    {
        // 101096c2: the duration comes in to age + 3 and field 12 becomes 3.
        BeamSim sim = Make(R71106());
        Step(sim, 1f);
        sim.TerminateGracefully(sim.CycleAge);
        Assert.Equal(4f, sim.Duration, 4);

        // The fade-IN is still three seconds and stock tests it first, so between the new fade-out's
        // start (1 s) and the fade-in's end (3 s) the beam keeps RISING, then snaps onto the fade-out
        // ramp at 3 s already two thirds of the way down it. That jump is stock's, not the port's.
        Step(sim, 1.5f);        // 2.5 s, still fading in: 0x60 * 2.5/3
        Assert.Equal(0x50u, sim.Current.Colour1 >> 24);
        Step(sim, 0.5f);        // 3 s, the first frame on the fade-out: 0x60 * (1 - 2/3)
        Assert.Equal(0x20u, sim.Current.Colour1 >> 24);
        Step(sim, 0.99f);       // 3.99 s, all but gone
        Assert.InRange(sim.Current.Colour1 >> 24, 0u, 1u);
    }

    [Fact]
    public void TerminatingLateDoesNothing()
    {
        BeamSim sim = Make(R71106());
        Step(sim, 4f);          // only 2 s left, less than the 3 s stop
        sim.TerminateGracefully(sim.CycleAge);
        Assert.Equal(6f, sim.Duration, 4);
    }

    [Fact]
    public void TheColourMixIsPerChannelWithAHalfAdded()
    {
        // randy31 10019c93 then 10019bb1.
        Assert.Equal(0xffffffffu, BeamSim.MixColour(0xffffffff, 0xffffffff, 0.5f));
        Assert.Equal(0x80808080u, BeamSim.MixColour(0x00000000, 0xffffffff, 0.5f));
        Assert.Equal(0x00000000u, BeamSim.MixColour(0x00000000, 0xffffffff, 0f));
    }

    [Fact]
    public void TheBladesSpanHalfATurnFromFortyFive()
    {
        // 10008080: 45 degrees, stepping by 180 / count while it stays under 225.
        var blades = new BeamBlades(0, 8, 1f, 1f, 0.2f);
        blades.Build(10f, 2f, 3f, 0xffffffff, 0xff000000);
        Assert.Equal(32, blades.VertexCount);

        // The first blade sits at 45 degrees: x and z are both r / sqrt(2).
        float r = 2f / (float)Math.Sqrt(2.0);
        Assert.Equal(r, blades.X[0], 4);
        Assert.Equal(r, blades.Z[0], 4);
        // Its opposite corner is 180 degrees round.
        Assert.Equal(-r, blades.X[1], 4);
        Assert.Equal(-r, blades.Z[1], 4);
    }

    [Fact]
    public void ABladeRunsFromTheFootToTheTip()
    {
        var blades = new BeamBlades(0, 4, 1f, 1f, 0.2f);
        blades.Build(10f, 2f, 3f, 0xffff0000, 0xff0000ff);

        Assert.Equal(0f, blades.Y[0], 4);
        Assert.Equal(0f, blades.Y[1], 4);
        Assert.Equal(10f, blades.Y[2], 4);
        Assert.Equal(10f, blades.Y[3], 4);

        // The foot pair is in colour 1 and the tip pair in colour 2.
        Assert.Equal(0xffff0000u, blades.Colors[0]);
        Assert.Equal(0xffff0000u, blades.Colors[1]);
        Assert.Equal(0xff0000ffu, blades.Colors[2]);
        Assert.Equal(0xff0000ffu, blades.Colors[3]);

        // The tip vertices carry the tip radius.
        float r = 3f / (float)Math.Sqrt(2.0);
        Assert.Equal(r, blades.X[2], 4);
    }

    [Fact]
    public void URunsAcrossTheBladeAndVAlongIt()
    {
        var blades = new BeamBlades(0, 4, 1f, 1f, 0.2f);
        blades.Build(10f, 2f, 3f, 0xffffffff, 0xffffffff);
        Assert.Equal(1f, blades.U[0], 4);
        Assert.Equal(0f, blades.U[1], 4);
        Assert.Equal(1f, blades.V[0], 4);   // the foot, at vOffset + vSpan
        Assert.Equal(0f, blades.V[2], 4);   // the tip, at vOffset
    }

    [Fact]
    public void FlagOneThousandFlipsVAlongTheBeam()
    {
        // 100082fb.
        var blades = new BeamBlades(BeamBlades.FlagFlipV, 4, 1f, 1f, 0.2f);
        blades.Build(10f, 2f, 3f, 0xffffffff, 0xffffffff);
        Assert.Equal(0f, blades.V[0], 4);
        Assert.Equal(1f, blades.V[2], 4);
    }

    [Fact]
    public void FlagOneHundredSwapsUAndV()
    {
        // 10008362.
        var plain = new BeamBlades(0, 4, 1f, 1f, 0.2f);
        plain.Build(10f, 2f, 3f, 0xffffffff, 0xffffffff);
        var swapped = new BeamBlades(BeamBlades.FlagSwapUv, 4, 1f, 1f, 0.2f);
        swapped.Build(10f, 2f, 3f, 0xffffffff, 0xffffffff);

        for (int i = 0; i < plain.VertexCount; i++)
        {
            Assert.Equal(plain.V[i], swapped.U[i], 5);
            Assert.Equal(plain.U[i], swapped.V[i], 5);
        }
    }

    [Fact]
    public void FlagEightThousandStartsSomewhereRandomInTheTexture()
    {
        // 10007ff2: picked once, at construction.
        var blades = new BeamBlades(BeamBlades.FlagRandomV, 4, 1f, 1f, 0.2f, () => 0.4f);
        Assert.Equal(0.4f, blades.VOffset, 5);
        blades.Build(10f, 2f, 3f, 0xffffffff, 0xffffffff);
        Assert.Equal(1.4f, blades.V[0], 4);
        Assert.Equal(0.4f, blades.V[2], 4);
    }

    [Fact]
    public void TheSecondMaterialsQuadLiesFlatAtFieldForty()
    {
        // 1000855f: half-size foot, at field 40 of the length, all four corners in colour 1.
        var blades = new BeamBlades(0, 4, 1f, 1f, 0.35f);
        blades.Build(4f, 0.75f, 0.75f, 0xff112233, 0xff445566);

        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(1.4f, blades.CapY[i], 4);          // 0.35 × 4
            Assert.Equal(0.75f, Math.Abs(blades.CapX[i]), 4);
            Assert.Equal(0.75f, Math.Abs(blades.CapZ[i]), 4);
            Assert.Equal(0xff112233u, blades.CapColors[i]);
        }
    }

    [Fact]
    public void TheCameraFadeScalesAlphaAndTruncates()
    {
        // 1000850f: alpha × the dot, cast to an int.
        Assert.Equal(0xff112233u, BeamBlades.ScaleAlpha(0xff112233, 1f));
        Assert.Equal(0x7f112233u, BeamBlades.ScaleAlpha(0xff112233, 0.5f));
        Assert.Equal(0x00112233u, BeamBlades.ScaleAlpha(0xff112233, 0f));
    }
}
