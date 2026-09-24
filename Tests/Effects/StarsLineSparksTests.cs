using System;
using Xunit;

/// <summary>
/// Locks <see cref="StarsLineSparks"/> to stock _GfxControlStars_t cases 19 (<c>100fa9e4</c>) and 16
/// (<c>100fa20f</c>). Case 19 uses 45597 (nano 150501's tracer): life 300 ms, size 0.44, scatter 0.55,
/// a constant dark-red ramp. Case 16 uses 17100 (nano 43878's tracer): life 300 ms, size 0.2, scatter 0.4.
/// </summary>
public class StarsLineSparksTests
{
    const float Step = 1f / 30f;
    static readonly float[] Red = { 1f, 0.215f, 0.039f, 0.039f };

    // A ball point of (0, 0, 0) (16384 / 16384 - 1 on each axis), then a life jitter of 0.9.
    static StarsLineSparks Make45597(System.Func<int> rand) => new StarsLineSparks(19, 0.3f, 0.44f, 0.55f, Red, Red, rand);

    static StarsLineSparks Centred()
    {
        int[] seq = { 16384, 16384, 16384, 0 };
        int i = 0;
        return Make45597(() => seq[i++ % seq.Length]);
    }

    static void StepAlongX(StarsLineSparks s, float age, bool hasHitLocation = true)
        => s.Step(age, 1f, hasHitLocation, 0f, 1f, 0f, 10f, 1f, 0f);

    [Fact]
    public void FirstCall_OpensFifteenSlotsAndDrawsNothingYet()
    {
        StarsLineSparks s = Centred();
        StepAlongX(s, 0f);
        Assert.Equal(15, s.LastCount);
        foreach (StarsCase3.Sprite sprite in s.Sprites)
            Assert.False(sprite.Visible);
    }

    [Fact]
    public void Spark_SitsStillAtTheProgressPointOfTheHitLocation()
    {
        StarsLineSparks s = Centred();
        StepAlongX(s, 0.5f); // p = 0.5: halfway from (0, 1, 0) to (10, 1, 0)
        StepAlongX(s, 0.5f + Step);
        StarsCase3.Sprite a = s.Sprites[0];
        Assert.True(a.Visible);
        Assert.Equal(5f, a.X, 5);
        Assert.Equal(1f, a.Y, 5);
        Assert.Equal(0f, a.Z, 5);

        // Timer 0.77 (0.3 * 0.9 + 0.5); t = 0.2111; size = 0.44 * (1 - (1 - 2t)²); frame 15 - ftol(3.376).
        Assert.Equal(0.2931162f, a.Size, 6);
        Assert.Equal(12, a.Frame);
        Assert.Equal(0xFF360909u, a.Argb);

        StepAlongX(s, 0.5f + 2 * Step);
        Assert.Equal(5f, s.Sprites[0].X, 5); // no velocity: it never moves
    }

    [Fact]
    public void Spark_LivesLifeTimesTheJitter_ThenItsSlotRespawns()
    {
        StarsLineSparks s = Centred();
        float age = 0f;
        StepAlongX(s, age);
        int serial = 0;
        for (int n = 1; n <= 8; n++)
        {
            age = n * Step;
            StepAlongX(s, age);
            if (n == 1)
                serial = s.Sprites[0].Serial;
            Assert.True(s.Sprites[0].Visible, $"step {n}"); // alive until 0.27 s
        }

        // Step 9 (0.3 s) is past the timer: slot 0 respawns and its record is left as it was.
        StepAlongX(s, 9 * Step);
        StepAlongX(s, 10 * Step);
        Assert.NotEqual(serial, s.Sprites[0].Serial);
        Assert.Equal(3f, s.Sprites[0].X, 4); // p = 0.3 at the respawn
    }

    [Fact]
    public void WithoutAHitLocation_NothingSpawns()
    {
        StarsLineSparks s = Centred();
        StepAlongX(s, 0f, hasHitLocation: false);
        StepAlongX(s, Step, hasHitLocation: false);
        Assert.Equal(0, s.LastCount);
    }

    [Fact]
    public void Terminating_StopsSpawningAndDrainsWithinALife()
    {
        StarsLineSparks s = Centred();
        StepAlongX(s, 0f);
        StepAlongX(s, Step);
        s.Terminating = true;
        Assert.False(s.Drained);
        for (int n = 2; n < 12; n++)
            StepAlongX(s, n * Step);
        Assert.True(s.Drained);
    }

    [Fact]
    public void Blend_GrowsTheSizeBetweenSteps_WithoutMovingTheSpark()
    {
        StarsLineSparks s = Centred();
        StepAlongX(s, 0.5f);
        StepAlongX(s, 0.5f + Step);
        StarsCase3.Sprite before = s.Sprites[0];
        StepAlongX(s, 0.5f + 2 * Step);
        StarsCase3.Sprite after = s.Sprites[0];

        StarsCase3.Sprite half = s.Blend(0, 0.5f);
        Assert.Equal((before.Size + after.Size) * 0.5f, half.Size, 6);
        Assert.Equal(after.X, half.X, 6);
    }

    [Fact]
    public void LifeJitter_ReachesOnePointOneAtTheTopOfTheMask()
    {
        int[] seq = { 16384, 16384, 16384, 0x7ff };
        int i = 0;
        StarsLineSparks s = Make45597(() => seq[i++ % seq.Length]);
        StepAlongX(s, 0f);
        // 0.3 * (2047 * 0.0001 + 0.9) = 0.33141: still alive at 0.33, respawned at 0.3334.
        StepAlongX(s, 0.33f);
        Assert.True(s.Sprites[0].Visible);
        Assert.Equal(1, s.Sprites[0].Serial);
        StepAlongX(s, 0.3334f);
        StepAlongX(s, 0.34f);
        Assert.NotEqual(1, s.Sprites[0].Serial);
    }

    static readonly float[] Gold = { 1f, 0.961f, 0.753f, 0.227f };

    [Fact]
    public void Case18_OpensTenSlotsPerCall_AtTheHead_WithThreeDrawsEach()
    {
        int draws = 0;
        var s = new StarsLineSparks(18, 0.3f, 0.2f, 0.4f, Gold, Gold, () => { draws++; return 16384; });
        s.Step(0.5f, 1f, true, 0f, 1f, 0f, 10f, 1f, 0f);
        Assert.Equal(StarsLineSparks.Case18SpawnsPerStep, s.LastCount);
        Assert.Equal(30, draws); // the ball only: no along draw, no life jitter

        s.Step(0.5f + Step, 1f, true, 0f, 1f, 0f, 10f, 1f, 0f);
        StarsCase3.Sprite p = s.Sprites[0];
        Assert.True(p.Visible);
        Assert.Equal(5f, p.X, 5); // end * 0.5 + start * 0.5
        Assert.Equal(1f, p.Y, 5);
    }

    [Fact]
    public void Case18_LivesExactlyField30()
    {
        var s = new StarsLineSparks(18, 0.3f, 0.2f, 0.4f, Gold, Gold, () => 16384);
        s.Step(0f, 1f, true, 0f, 1f, 0f, 10f, 1f, 0f);
        s.Step(0.299f, 1f, true, 0f, 1f, 0f, 10f, 1f, 0f);
        Assert.True(s.Sprites[0].Visible);
        Assert.Equal(1, s.Sprites[0].Serial);
        s.Step(0.3f, 1f, true, 0f, 1f, 0f, 10f, 1f, 0f); // dead: respawned, record left as it was
        s.Step(0.31f, 1f, true, 0f, 1f, 0f, 10f, 1f, 0f);
        Assert.NotEqual(1, s.Sprites[0].Serial);
    }

    static StarsLineSparks Make17100(int alongRand)
    {
        // Ball point (0, 0, 0), then the case 16 position rand.
        int[] seq = { 16384, 16384, 16384, alongRand };
        int i = 0;
        return new StarsLineSparks(16, 0.3f, 0.2f, 0.4f, Red, Red, () => seq[i++ % seq.Length]);
    }

    [Fact]
    public void Case16_SparksLandAnywhereAlongTheLine_NotAtTheProgressPoint()
    {
        StarsLineSparks s = Make17100(8192); // q = 8192 / 32768 = 0.25
        StepAlongX(s, 0.5f);                 // case 19 would put it at p = 0.5
        StepAlongX(s, 0.5f + Step);
        Assert.True(s.Sprites[0].Visible);
        Assert.Equal(2.5f, s.Sprites[0].X, 5);
        Assert.Equal(1f, s.Sprites[0].Y, 5);
    }

    [Fact]
    public void Case16_LivesExactlyTheFieldLife()
    {
        StarsLineSparks s = Make17100(0);
        StepAlongX(s, 0f);
        int serial = 0;
        for (int n = 1; n <= 8; n++)
        {
            StepAlongX(s, n * Step);
            if (n == 1)
                serial = s.Sprites[0].Serial;
            Assert.Equal(serial, s.Sprites[0].Serial);
        }

        // Due at 0.3 (no jitter): respawned then, rewritten by the next call.
        StepAlongX(s, 9 * Step);
        StepAlongX(s, 10 * Step);
        Assert.NotEqual(serial, s.Sprites[0].Serial);
    }

    [Fact]
    public void Case16_OpensFifteenACall()
    {
        StarsLineSparks s = Make17100(0);
        StepAlongX(s, 0f);
        Assert.Equal(15, s.LastCount);
        StepAlongX(s, Step);
        Assert.Equal(30, s.LastCount);
    }

    [Fact]
    public void Case17_WindsAHelixRoundTheLine_ColouredByProgress()
    {
        float[] start = { 1f, 0f, 0f, 0f }, end = { 1f, 1f, 1f, 1f };
        var s = new StarsLineSparks(17, 0.3f, 0.2f, 2f, start, end, () => 16384); // q = 0.5, phase 2
        StepAlongX(s, 0.25f);
        StepAlongX(s, 0.25f + Step);
        StarsCase3.Sprite p = s.Sprites[0];
        Assert.True(p.Visible);
        Assert.Equal(5f, p.X, 4); // halfway along
        // Off the axis by (1 - (1 - 2q)^2) / 2 = 0.5.
        Assert.Equal(0.5f, (float)Math.Sqrt((p.Y - 1f) * (p.Y - 1f) + p.Z * p.Z), 4);
        float progress = (float)((0.25f + (double)Step) / 1f);
        Assert.Equal(StockColorRamp.Eval(start, end, (float)((double)(0.25f + Step) / 1f)), p.Argb);
    }

    // 45686 (nano 28597's tracer): life 250 ms, size 0.59, scatter 0.25, white to black.
    static readonly float[] White = { 1f, 1f, 1f, 1f };
    static readonly float[] Black = { 1f, 0f, 0f, 0f };

    static StarsLineSparks Make45686() => new StarsLineSparks(20, 0.25f, 0.59f, 0.25f, White, Black, () => 16384);

    [Fact]
    public void Case20_OpensFifteenAtTheHead_FullSizeThroughout_FramesCountingUp()
    {
        StarsLineSparks s = Make45686();
        StepAlongX(s, 0.5f);
        Assert.Equal(15, s.LastCount);
        Assert.False(s.Sprites[0].Visible);

        StepAlongX(s, 0.6f);
        Assert.Equal(30, s.LastCount);
        StarsCase3.Sprite a = s.Sprites[0];
        Assert.True(a.Visible);
        Assert.Equal(5f, a.X, 5);
        Assert.Equal(1f, a.Y, 5);
        float t = (float)((0.25 + 0.6f - 0.75f) / 0.25);
        Assert.Equal(0.59f, a.Size);
        Assert.Equal((int)(t * 63.9900016784668), a.Frame);
        Assert.Equal(25, a.Frame);
        Assert.Equal(StockColorRamp.Eval(White, Black, t), a.Argb);
    }

    [Fact]
    public void Case20_LivesExactlyField30_WithNoJitter()
    {
        StarsLineSparks s = Make45686();
        StepAlongX(s, 0f);
        StepAlongX(s, 0.249f);
        Assert.True(s.Sprites[0].Visible);
        Assert.Equal(63, s.Sprites[0].Frame);
        Assert.Equal(1, s.Sprites[0].Serial);
        StepAlongX(s, 0.25f); // dead: respawned, record left as it was
        StepAlongX(s, 0.26f);
        Assert.NotEqual(1, s.Sprites[0].Serial);
    }
}
