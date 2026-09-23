using System;
using Xunit;

/// <summary>
/// Locks <see cref="StarsCase9"/> to stock _GfxControlStars_t case 9 (<c>100f946f</c>) with 43070 (a hit of
/// 28636 Slime Cascade): life 0.5 s, size 0.35, fall 4, spread 0.7, flash 400.
/// </summary>
public class StarsCase9Tests
{
    const float Step = 1f / 30f;
    static readonly float[] Start = { 1f, 0f, 0.5f, 0.1f };
    static readonly float[] End = { 0.8f, 0f, 0.5f, 0.1f };

    /// <summary>A rand() giving the disc point (0.5, -0.5): 24576 → 0.5, 8192 → -0.5.</summary>
    static Func<int> Disc() { int i = 0; int[] seq = { 24576, 8192 }; return () => seq[i++ % 2]; }

    static StarsCase9 Make() => new StarsCase9(0.5f, 0.35f, 4f, 0.7f, 400, Start, End, Disc());

    [Fact]
    public void Spawns_TwoACall_OnEveryOtherRecord_LeavingTheRecord()
    {
        var sim = Make();
        sim.Step(0f, 1f, 2f, 3f);
        Assert.Equal(2, sim.LastCount);
        Assert.False(sim.Sprites[0].Visible);
        Assert.False(sim.Sprites[1].Visible);
    }

    [Fact]
    public void ASpark_FlashesTwoMetresUp_AndFalls()
    {
        var sim = Make();
        sim.Step(0f, 1f, 2f, 3f);
        sim.Step(Step, 1f, 2f, 3f); // x = 1/30 s
        StarsCase3.Sprite s = sim.Sprites[0];
        Assert.True(s.Visible);
        Assert.False(sim.Sprites[1].Visible); // odd records stay unused

        double x = Step;
        double g = 0.5 - x * 4;
        Assert.Equal(1f + 0.5f * 0.7f, s.X, 5);
        Assert.Equal(3f - 0.5f * 0.7f, s.Z, 5);
        Assert.Equal(2 + 1.5 + g, s.Y, 4);
        Assert.Equal(0.35 + g * 4.0, s.Size, 4);
        Assert.Equal(0, s.Frame);
    }

    [Fact]
    public void AfterTheFlash_TheSizeIsField11_AndItKeepsFalling()
    {
        var sim = Make();
        sim.Step(0f, 0f, 0f, 0f);
        sim.Step(0.3f, 0f, 0f, 0f); // x = 0.3: g = 0.5 - 1.2 = -0.7
        StarsCase3.Sprite s = sim.Sprites[0];
        Assert.Equal(0.35f, s.Size);
        Assert.Equal(1.5 - 0.7, s.Y, 4);
        Assert.Equal(StockColorRamp.Eval(Start, End, 0.6f), s.Argb);
    }

    [Fact]
    public void Terminating_StopsSpawns_AndDrainsWhenNothingIsLeft()
    {
        var sim = Make();
        sim.Step(0f, 0f, 0f, 0f);
        sim.Terminating = true;
        sim.Step(0.1f, 0f, 0f, 0f);
        Assert.False(sim.Drained);
        sim.Step(0.6f, 0f, 0f, 0f);
        Assert.True(sim.Drained);
    }

    [Fact]
    public void DiscPoints_AreInsideAndNotTheCentre()
    {
        int[] seq = { 32767, 32767, 16384, 16384, 24576, 8192 }; // outside, then the centre, then (0.5, -0.5)
        int i = 0;
        StarsCase9.RandomInUnitDisc(() => seq[i++], out float x, out float z);
        Assert.Equal(0.5f, x);
        Assert.Equal(-0.5f, z);
    }
}
