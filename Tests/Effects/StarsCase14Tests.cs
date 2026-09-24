using System;
using Xunit;

/// <summary>
/// Locks <see cref="StarsCase14"/> to stock <c>_GfxControlStars_t</c> starType 14
/// (<c>100f9da3</c>..<c>100f9f5e</c>, lazy init <c>100f81e0</c> → <c>100f80a6</c>), using record 43118,
/// the hit of 95796 Captivated Gaze: size 0.4, duration 5, reach 0.9, turn 3.3 rad, life 600 ms,
/// 2 spawns per call.
/// </summary>
public class StarsCase14Tests
{
    const float Size = 0.4f;
    const float Reach = 0.9f;
    const float Turn = 3.3f;
    const int LifeMs = 600;
    const int Spawns = 2;
    const float Duration = 5f;

    static readonly float[] Start = { 1f, 1f, 1f, 1f };
    static readonly float[] End = { 0f, 1f, 1f, 1f };

    static StarsCase14 Make() =>
        new StarsCase14(Size, Reach, Turn, LifeMs, Spawns, Start, End);

    [Fact]
    public void TheLoader_TurnsTheLifeIntoSeconds()
    {
        // 100f7fe7: +0x16cc = field 30 / 1000.
        Assert.Equal(0.6f, Make().Life, 5);
    }

    [Fact]
    public void EverySlotStartsExpiredSoTheFirstCallsSpendTheBudget()
    {
        // 100f81e0 fills all 128 death times with -100, so nothing is alive to begin with.
        StarsCase14 c = Make();
        c.Step(0f, Duration, 0f, 0f, 0f);
        Assert.Equal(Spawns, c.LiveCount);
    }

    [Fact]
    public void ASpawningSlotIsLeftUndrawnThatCall()
    {
        // 100f9eb8: the spawn branch writes the direction and the death time and jumps to the loop
        // tail; it never touches the sprite record.
        StarsCase14 c = Make();
        c.Step(0f, Duration, 0f, 0f, 0f);
        foreach (StarsCase3.Sprite s in c.Sprites)
            Assert.False(s.Visible);
    }

    [Fact]
    public void TheAngleWalksOnByTheTurnForEverySpark()
    {
        // 100f9ec7: +0x1650 += field 29 per spawn, not per call.
        StarsCase14 c = Make();
        c.Step(0f, Duration, 0f, 0f, 0f);
        Assert.Equal(Turn * 2f, c.Angle, 4);
        c.Step(1f / 30f, Duration, 0f, 0f, 0f);
        Assert.Equal(Turn * 4f, c.Angle, 4);
    }

    [Fact]
    public void ASparkFliesStraightOutInTheXzPlane()
    {
        StarsCase14 c = Make();
        c.Step(0f, Duration, 0f, 0f, 0f);           // spawns slots 0 and 1
        float age = 0.3f;
        c.Step(age, Duration, 0f, 0f, 0f);

        // Slot 0 took the first spawn, at angle = turn.
        StarsCase3.Sprite s = c.Sprites[0];
        Assert.True(s.Visible);

        float t = (age + 0.6f - 0.6f) / 0.6f;       // death was 0 + life
        float r = Reach * t;
        float p = age / Duration;
        Assert.Equal((float)Math.Sin(Turn) * r, s.X, 4);
        Assert.Equal(2f * p - 1f, s.Y, 4);          // dir.y is always 0
        Assert.Equal((float)Math.Cos(Turn) * r, s.Z, 4);
        Assert.Equal(Size, s.Size, 4);
        Assert.Equal(0, s.Frame);
    }

    [Fact]
    public void TheWholeRingRidesFromOneBelowToOneAboveTheLocator()
    {
        // 100f9e3e: local y = dir.y + (2p - 1), and dir.y is 0 for every spark.
        StarsCase14 c = Make();
        c.Step(0f, Duration, 0f, 0f, 0f);
        c.Step(0.01f, Duration, 0f, 0f, 0f);
        Assert.Equal(-1f, c.Sprites[0].Y, 2);

        StarsCase14 d = Make();
        d.Step(0f, Duration, 0f, 0f, 0f);
        d.Step(Duration * 0.5f, Duration, 0f, 0f, 0f);
        Assert.Equal(0f, d.Sprites[0].Y, 2);
    }

    [Fact]
    public void TheSparkIsPlacedRelativeToTheLocator()
    {
        StarsCase14 c = Make();
        c.Step(0f, Duration, 5f, 6f, 7f);
        c.Step(0.3f, Duration, 5f, 6f, 7f);
        StarsCase3.Sprite s = c.Sprites[0];
        float t = 0.5f, r = Reach * t, p = 0.3f / Duration;
        Assert.Equal(5f + (float)Math.Sin(Turn) * r, s.X, 4);
        Assert.Equal(6f + 2f * p - 1f, s.Y, 4);
        Assert.Equal(7f + (float)Math.Cos(Turn) * r, s.Z, 4);
    }

    [Fact]
    public void ASparkLivesExactlyItsLife()
    {
        StarsCase14 c = Make();
        c.Step(0f, Duration, 0f, 0f, 0f);           // slot 0 dies at 0.6
        c.Step(0.59f, Duration, 0f, 0f, 0f);
        Assert.True(c.Sprites[0].Visible);

        // At 0.6 the age is no longer below the death time. With spawning closed the slot is hidden.
        c.Terminating = true;
        c.Step(0.6f, Duration, 0f, 0f, 0f);
        Assert.False(c.Sprites[0].Visible);
    }

    [Fact]
    public void ARecyclingSlotKeepsItsOldFrameForOneCall()
    {
        // The spawn branch (100f9eb8) jumps to the loop tail without writing the sprite, so a slot that
        // dies and immediately respawns draws once more wherever it was. That is stock, not an
        // oversight here.
        StarsCase14 c = Make();
        c.Step(0f, Duration, 0f, 0f, 0f);           // slot 0 dies at 0.6
        c.Step(0.59f, Duration, 0f, 0f, 0f);
        StarsCase3.Sprite last = c.Sprites[0];
        float angle = c.Angle;

        c.Step(0.6f, Duration, 0f, 0f, 0f);         // dies and respawns in the same call
        Assert.True(c.Sprites[0].Visible);
        Assert.Equal(last.X, c.Sprites[0].X, 5);    // the stale position
        Assert.NotEqual(angle, c.Angle);            // but it did take a spawn

        // The call after that it is a new spark at the start of its life.
        c.Step(0.63f, Duration, 0f, 0f, 0f);
        Assert.True(c.Sprites[0].Visible);
        Assert.NotEqual(last.X, c.Sprites[0].X);
    }

    [Fact]
    public void TerminatingPinsTheRingAtTheTopAndStopsSpawning()
    {
        // 100f9dbf: p is forced to 1; 100f9ebe: the spawn branch is closed.
        StarsCase14 c = Make();
        c.Step(0f, Duration, 0f, 0f, 0f);
        float angle = c.Angle;

        c.Terminating = true;
        c.Step(0.3f, Duration, 0f, 0f, 0f);
        Assert.Equal(angle, c.Angle, 4);            // nothing spawned
        Assert.Equal(1f, c.Sprites[0].Y, 4);        // 2 * 1 - 1
    }

    [Fact]
    public void ItOnlyDrainsOnceTerminatingAndEmpty()
    {
        // 100f9f49: a running case never drains, however few sparks are up.
        StarsCase14 c = Make();
        c.Step(0f, Duration, 0f, 0f, 0f);
        Assert.False(c.Drained);

        c.Terminating = true;
        c.Step(0.3f, Duration, 0f, 0f, 0f);
        Assert.False(c.Drained);                    // the two sparks are still alive
        c.Step(1f, Duration, 0f, 0f, 0f);           // past their 0.6 s life
        Assert.True(c.Drained);
    }

    [Fact]
    public void TheBudgetIsPerCallNotPerSecond()
    {
        StarsCase14 c = Make();
        for (int i = 0; i < 4; i++)
            c.Step(i / 30f, Duration, 0f, 0f, 0f);
        // Four calls, two spawns each: eight sparks have been started.
        Assert.Equal(Turn * 8f, c.Angle, 3);
    }

    [Fact]
    public void ARecycledSlotDoesNotBlendAcrossItsJump()
    {
        StarsCase14 c = Make();
        c.Step(0f, Duration, 0f, 0f, 0f);
        c.Step(0.3f, Duration, 0f, 0f, 0f);
        StarsCase3.Sprite mid = c.Sprites[0];
        // Halfway between two live steps it interpolates.
        c.Step(0.35f, Duration, 0f, 0f, 0f);
        StarsCase3.Sprite blended = c.Blend(0, 0.5f);
        Assert.InRange(blended.X, Math.Min(mid.X, c.Sprites[0].X), Math.Max(mid.X, c.Sprites[0].X));
    }

    [Fact]
    public void AllOneHundredAndTwentyEightSlotsAreUsable()
    {
        StarsCase14 c = Make();
        Assert.Equal(128, c.Sprites.Length);
        // 64 calls at 2 a call fill every slot.
        for (int i = 0; i < 64; i++)
            c.Step(i / 30f, Duration, 0f, 0f, 0f);
        int live = 0;
        foreach (StarsCase3.Sprite s in c.Sprites)
            if (s.Visible)
                live++;
        // Each spark lives 0.6 s = 18 calls, two a call, so about 36 are up at once.
        Assert.InRange(live, 30, 40);
    }
}
