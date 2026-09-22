using System.Collections.Generic;
using Xunit;

/// <summary>
/// Locks <see cref="Spell1Trail"/> to stock Spell1 window 2 (FUN_100f3d22 / FUN_100f3205), using
/// cast 46116's window [4, 5].
/// </summary>
public class Spell1TrailTests
{
    [Fact]
    public void FirstHalf_BothPassesStartAtTheHands()
    {
        var p = new float[8];
        Assert.Equal(2, Spell1Trail.WindowPasses(4.25f, 4f, 5f, p));
        // u = 0.25: (0, .5, .25, .25) and (1, .5, .75, .25). The earlier port started the second at
        // 1-u, which is a zero-length pass.
        Assert.Equal(new[] { 0f, 0.5f, 0.25f, 0.25f }, new[] { p[0], p[1], p[2], p[3] });
        Assert.Equal(new[] { 1f, 0.5f, 0.75f, 0.25f }, new[] { p[4], p[5], p[6], p[7] });
    }

    [Fact]
    public void SecondHalf_BothPassesEndAtTheMiddle()
    {
        var p = new float[8];
        Assert.Equal(2, Spell1Trail.WindowPasses(4.75f, 4f, 5f, p));
        Assert.Equal(new[] { 0.25f, 0.25f, 0.5f, 0.625f }, new[] { p[0], p[1], p[2], p[3] });
        Assert.Equal(new[] { 0.75f, 0.25f, 0.5f, 0.625f }, new[] { p[4], p[5], p[6], p[7] });
    }

    [Fact]
    public void AtTheWindowEnd_NothingIsDrawn()
    {
        Assert.Equal(0, Spell1Trail.WindowPasses(5f, 4f, 5f, new float[8]));
    }

    [Fact]
    public void PassCount_TruncatesWithNoMinimumAndCapsAtFifty()
    {
        Assert.Equal(7, Spell1Trail.PassCount(0.6f, 0f, 0.25f));  // 0.15 * 50 = 7.5
        Assert.Equal(0, Spell1Trail.PassCount(0.01f, 0f, 0.25f)); // 0.125: nothing
        Assert.Equal(1, Spell1Trail.PassCount(0.05f, 1f, 0.5f));  // |0.05 * -0.5| * 50 = 1.25
        Assert.Equal(50, Spell1Trail.PassCount(10f, 0f, 1f));
    }

    [Fact]
    public void Pass_SingleSprite_SitsAtT0WithSize0()
    {
        var emitted = new List<(float T, float Size)>();
        // |0.05 * -0.5| * 50 = 1.25 -> 1 sprite.
        Spell1Trail.Pass(0.05f, 1f, 0.5f, 0.5f, 0.25f, () => 0.5f, (t, s) => emitted.Add((t, s)));
        Assert.Single(emitted);
        Assert.Equal(1f, emitted[0].T, 5);
        Assert.Equal(0.5f, emitted[0].Size, 5);
    }

    [Fact]
    public void Pass_SpreadsEvenlyAndInterpolatesSize()
    {
        var emitted = new List<(float T, float Size)>();
        // 0.6 * 0.25 * 50 = 7 sprites from t 0 to 0.25, size 0.5 to 0.25; no jitter at rand 0.5.
        Spell1Trail.Pass(0.6f, 0f, 0.5f, 0.25f, 0.25f, () => 0.5f, (t, s) => emitted.Add((t, s)));
        Assert.Equal(7, emitted.Count);
        Assert.Equal(0f, emitted[0].T, 5);
        Assert.Equal(0.25f, emitted[6].T, 5);
        Assert.Equal(0.5f, emitted[0].Size, 5);
        Assert.Equal(0.25f, emitted[6].Size, 5);
    }

    [Fact]
    public void Colour_IsFieldsTenToThirteenTruncated()
    {
        Assert.Equal(0xFF72667Fu, Spell1Trail.PackColor(1f, 0.45f, 0.4f, 0.5f));
    }
}
