using System;
using Xunit;

/// <summary>
/// Locks <see cref="StarsCase28"/> to stock <c>_GfxControlStars_t</c> starType 28
/// (<c>100fc0d7</c>..<c>100fc2ef</c>), using its only record 12600 — the hit of 259902 Throw Snowball:
/// duration 1 s, swell size 0.2, life 300 ms, a flat white-ish ramp.
/// </summary>
public class StarsCase28Tests
{
    const float Size = 0.2f;
    const int LifeMs = 300;
    const float Duration = 1f;

    static readonly float[] Start = { 1f, 0.9f, 0.9f, 1f };
    static readonly float[] End = { 1f, 0.9f, 0.9f, 1f };

    static StarsCase28 Make() => new StarsCase28(Size, LifeMs, Start, End);

    /// <summary>Steps the case along a 10 m line running up the x axis.</summary>
    static void Step(StarsCase28 c, float age, bool hasLine = true) =>
        c.Step(age, Duration, hasLine, 0f, 0f, 0f, 10f, 0f, 0f);

    [Fact]
    public void TheLifeIsFieldThirtyInSeconds()
    {
        Assert.Equal(0.3f, Make().Life, 5);
    }

    [Fact]
    public void ACallStartsTenSprites()
    {
        // 100fc0df: the budget is the literal 10.
        StarsCase28 c = Make();
        Step(c, 0f);
        Assert.Equal(StarsCase28.SpawnsPerCall, c.LiveCount);
    }

    [Fact]
    public void WithoutAHitLocationNothingSpawns()
    {
        // 100fc28b: a null hit location closes the spawn branch.
        StarsCase28 c = Make();
        Step(c, 0f, hasLine: false);
        Assert.Equal(0, c.LiveCount);
        foreach (StarsCase3.Sprite s in c.Sprites)
            Assert.False(s.Visible);
    }

    [Fact]
    public void ThePointTravelsFromTheStartToTheEnd()
    {
        // 100fc122: head = end * p + start * (1 - p).
        StarsCase28 c = Make();
        Step(c, 0f);
        Assert.Equal(0f, c.HeadX, 4);
        Step(c, 0.5f);
        Assert.Equal(5f, c.HeadX, 4);
        Step(c, 1f);
        Assert.Equal(10f, c.HeadX, 4);
    }

    [Fact]
    public void EverySpriteSitsOnTheTravellingPoint()
    {
        // 100fc26a: stock overwrites the slot's stored position with the current point, so they all
        // share it rather than leaving a trail.
        StarsCase28 c = Make();
        Step(c, 0f);
        Step(c, 0.1f);
        Step(c, 0.2f);

        int seen = 0;
        foreach (StarsCase3.Sprite s in c.Sprites)
        {
            if (!s.Visible)
                continue;
            seen++;
            Assert.Equal(c.HeadX, s.X, 4);
            Assert.Equal(c.HeadY, s.Y, 4);
            Assert.Equal(c.HeadZ, s.Z, 4);
        }
        Assert.True(seen > 10, $"only {seen} sprites were up");
    }

    [Fact]
    public void TheSizeSwellsToFullAtHalfLifeAndBack()
    {
        // 100fc1eb: size = field 28 * (1 - (1 - 2t)^2).
        StarsCase28 c = Make();
        Step(c, 0f);                 // slot 0 born at 0, dies at 0.3

        Step(c, 0.001f);             // t about 0
        float young = c.Sprites[0].Size;
        Step(c, 0.15f);              // t = 0.5
        float peak = c.Sprites[0].Size;
        Step(c, 0.299f);             // t about 1
        float old = c.Sprites[0].Size;

        Assert.Equal(Size, peak, 3);
        Assert.True(young < peak * 0.1f, $"young {young} should be near nothing");
        Assert.True(old < peak * 0.1f, $"old {old} should be near nothing");
    }

    [Fact]
    public void TheFrameCountsFifteenDownToZero()
    {
        // 100fc244: frame = 15 - ftol(t * 15.99).
        StarsCase28 c = Make();
        Step(c, 0f);

        Step(c, 0.001f);
        Assert.Equal(15, c.Sprites[0].Frame);
        Step(c, 0.15f);
        Assert.Equal(15 - (int)(0.5f * 15.99f), c.Sprites[0].Frame);
        Step(c, 0.299f);
        Assert.Equal(0, c.Sprites[0].Frame);
    }

    [Fact]
    public void ASpriteLastsItsLife()
    {
        StarsCase28 c = Make();
        Step(c, 0f);                 // slot 0 dies at 0.3
        Step(c, 0.29f);
        Assert.True(c.Sprites[0].Visible);

        // Past its death, with spawning closed so it cannot recycle into the same slot.
        c.Terminating = true;
        Step(c, 0.3f);
        Assert.False(c.Sprites[0].Visible);
    }

    [Fact]
    public void TerminatingStopsTheSpawnsAndThenDrains()
    {
        StarsCase28 c = Make();
        Step(c, 0f);
        Assert.False(c.Drained);

        c.Terminating = true;
        Step(c, 0.1f);
        Assert.False(c.Drained);     // the first ten are still up
        Step(c, 0.5f);               // past the 0.3 s life
        Assert.True(c.Drained);
    }

    [Fact]
    public void ItRunsAboutNinetySpritesAtOnce()
    {
        StarsCase28 c = Make();
        for (int i = 0; i < 30; i++)
            Step(c, i / 30f);

        int visible = 0;
        foreach (StarsCase3.Sprite s in c.Sprites)
            if (s.Visible)
                visible++;
        // Ten a call at 30 Hz, each lasting 0.3 s: 90 of the 128 slots.
        Assert.InRange(visible, 80, StarsCase28.SlotCount);
    }

    [Fact]
    public void TheColourComesFromTheRamp()
    {
        StarsCase28 c = Make();
        Step(c, 0f);
        Step(c, 0.1f);
        // This record's ramp has the same colour at both ends, so every sprite matches it.
        uint expected = StockColorRamp.Eval(Start, End, 0.5f);
        foreach (StarsCase3.Sprite s in c.Sprites)
            if (s.Visible)
                Assert.Equal(expected, s.Argb);
    }
}
