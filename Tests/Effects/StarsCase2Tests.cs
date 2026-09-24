using System;
using Xunit;

/// <summary>
/// Locks <see cref="StarsCase2"/> to stock _GfxControlStars_t case 2 (<c>100f8864</c>, lazy init
/// <c>100f80ba</c>), with 43220 (nano 246033's hit): size 2.1, start alpha 1, end (0.9, 0.9, 0.9, 0.9).
/// </summary>
public class StarsCase2Tests
{
    static readonly float[] Start = { 1f, 0f, 0f, 0f };
    static readonly float[] End = { 0.9f, 0.9f, 0.9f, 0.9f };

    /// <summary>rand() giving the ball point (0.25, 0.5, -0.5) for every slot: v / 16384 - 1.</summary>
    static Func<int> Rand()
    {
        int[] seq = { 20480, 24576, 8192 };
        int i = 0;
        return () => seq[i++ % seq.Length];
    }

    static StarsCase2 Make() => new StarsCase2(2.1f, Start, End, Rand());

    [Fact]
    public void FirstCall_SpreadsEverySlotAtRadiusPointSix()
    {
        StarsCase2 c = Make();
        c.Step(0f, StarsCase2.LazyDuration, 1f, 2f, 3f);
        Assert.True(c.Initialised);
        StarsCase3.Sprite s = c.Sprites[5];
        Assert.True(s.Visible);
        // k = sqrt(0) * 0.7 + 0.6.
        Assert.Equal(1f + 0.25f * 0.6f, s.X, 5);
        Assert.Equal(2f + 0.5f * 0.6f, s.Y, 5);
        Assert.Equal(3f - 0.5f * 0.6f, s.Z, 5);
    }

    [Fact]
    public void RadiusGrowsWithTheRootOfProgress()
    {
        StarsCase2 c = Make();
        c.Step(0f, 1.5f, 0f, 0f, 0f);
        c.Step(0.375f, 1.5f, 0f, 0f, 0f); // p = 0.25, k = 0.5 * 0.7 + 0.6
        Assert.Equal(0.5f * 0.95f, c.Sprites[0].Y, 5);
    }

    [Fact]
    public void SizeAndFrame_FollowTheSpanTable()
    {
        StarsCase2 c = Make();
        c.Step(0f, 1.5f, 0f, 0f, 0f);
        // p = 0: n = 15, size = 2.1 * 20 / 29; odd slots show one frame less.
        Assert.Equal(2.1f * 20f / 29f, c.Sprites[0].Size, 5);
        Assert.Equal(15, c.Sprites[0].Frame);
        Assert.Equal(14, c.Sprites[1].Frame);

        c.Step(0.75f, 1.5f, 0f, 0f, 0f); // p = 0.5: n = _ftol(7.5) = 7
        Assert.Equal(2.1f * 0.5f * 20f / 19f, c.Sprites[0].Size, 5);
        Assert.Equal(7, c.Sprites[0].Frame);

        c.Step(1.47f, 1.5f, 0f, 0f, 0f); // p = 0.98: n = 0, clamped to 1
        Assert.Equal(1, c.Sprites[0].Frame);
        Assert.Equal(0, c.Sprites[1].Frame);
    }

    [Fact]
    public void Colour_IsTheRampWithThePaletteAsItsStart_BySlot()
    {
        StarsCase2 c = Make();
        c.Step(0f, 1.5f, 0f, 0f, 0f);
        // p = 0: the start colour, alpha 1, RGB from palette (slot & 7).
        Assert.Equal(StockColorRamp.Eval(new[] { 1f, 1f, 0.7f, 0.7f }, End, 0f), c.Sprites[0].Argb);
        Assert.Equal(0xffffff00u, c.Sprites[1].Argb);
        Assert.Equal(0xffff0000u, c.Sprites[6].Argb);
        Assert.Equal(c.Sprites[1].Argb, c.Sprites[9].Argb);

        c.Step(0.75f, 1.5f, 0f, 0f, 0f);
        Assert.Equal(StockColorRamp.Eval(new[] { 1f, 0f, 0f, 1f }, End, 0.5f), c.Sprites[4].Argb);
    }

    [Fact]
    public void Terminating_IsDrainedAtOnce()
    {
        StarsCase2 c = Make();
        c.Step(0f, 1.5f, 0f, 0f, 0f);
        Assert.False(c.Drained);
        c.Terminating = true;
        Assert.True(c.Drained);
    }
}
