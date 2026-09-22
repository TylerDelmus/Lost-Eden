using Xunit;

/// <summary>
/// Locks <see cref="StarsSwirl"/> and <see cref="StarsCase10"/> to stock _GfxControlStars_t cases 5 (<c>100f8e71</c>), 6
/// (<c>100f90d7</c>), 11 (<c>100f9800</c>) and 10 (<c>100f9615</c>), with the records of nano 223386's hit
/// 47175: 43372 (type 6), 43318 (type 11) and 43258 (type 10).
/// </summary>
public class StarsSwirlTests
{
    const float Step = 1f / 30f;
    static readonly float[] Start = { 1f, 0.18f, 0f, 0.53f };
    static readonly float[] End = { 0.4f, 0.3f, 0.3f, 1f };

    /// <summary>A rand() whose draws give the XZ direction (1, 0, 0) and the unit vector (1, 0, 0).</summary>
    static System.Func<int> Rand(params int[] seq)
    {
        int i = 0;
        return () => seq[i++ % seq.Length];
    }

    static System.Func<int> Xz => Rand(32767, 16384);
    static System.Func<int> Unit => Rand(32767, 16384, 16384);

    [Fact]
    public void Spawn_OnARingAMetreBelow_LaunchedSidewaysAndUp()
    {
        var s = new StarsSwirl(11, 1.8f, 1.9f, 0.8f, 2, Start, End, Xz);
        s.Step(0f, 0f, 1f, 0f);
        Assert.Equal(2, s.LastCount);
        Assert.False(s.Sprites[0].Visible);

        // p0 = (0.8, 0, 0), v0 = (0, 0.5, -0.8); then v += 0.02 * (o - p), p += 0.1 * v.
        s.Step(Step, 0f, 1f, 0f);
        float vx = 0.02f * -0.8f, vy = 0.5f + 0.02f * 1f, vz = -0.8f;
        Assert.Equal(0.8f + vx * 0.1f, s.Sprites[0].X, 5);
        Assert.Equal(vy * 0.1f, s.Sprites[0].Y, 5);
        Assert.Equal(vz * 0.1f, s.Sprites[0].Z, 5);
    }

    [Fact]
    public void Case5_ThrowsSidewaysFrom1Point2Below_AndPullsInThreeDimensions()
    {
        // 43000 (hit of 152418 Imprisoned): life 1.3 s, size 3.5, radius 0.3, spring 1 (k = 0.01).
        var s = new StarsSwirl(5, 1.3f, 3.5f, 0.3f, 1, Start, End, Xz);
        s.Step(0f, 0f, 1f, 0f);
        Assert.Equal(2, s.LastCount);
        Assert.False(s.Sprites[0].Visible);

        // p0 = (0, 1 - 1.2, 0), v0 = (0.3, 0, 0); then v += 0.01 * (o - p), p += 0.2 * v.
        s.Step(Step, 0f, 1f, 0f);
        float vx = 0.3f + 0.01f * -0f, vy = 0.01f * 1.2f;
        Assert.Equal(vx * 0.2f, s.Sprites[0].X, 5);
        Assert.Equal(-0.2f + vy * 0.2f, s.Sprites[0].Y, 5);
        Assert.Equal(0f, s.Sprites[0].Z, 5);
        Assert.Equal(0f, s.Linger(3));
    }

    [Fact]
    public void Case5_FrameAndSize_AreCase11s()
    {
        var five = new StarsSwirl(5, 1.3f, 3.5f, 0.3f, 1, Start, End, Xz);
        var eleven = new StarsSwirl(11, 1.3f, 3.5f, 0.3f, 1, Start, End, Xz);
        five.Step(0f, 0f, 1f, 0f);
        eleven.Step(0f, 0f, 1f, 0f);
        five.Step(0.65f, 0f, 1f, 0f);
        eleven.Step(0.65f, 0f, 1f, 0f);
        Assert.Equal(eleven.Sprites[0].Frame, five.Sprites[0].Frame);
        Assert.Equal(eleven.Sprites[0].Size, five.Sprites[0].Size);
        Assert.Equal(eleven.Sprites[0].Argb, five.Sprites[0].Argb);
    }

    [Fact]
    public void Case6_PullsOnlyHorizontally_WithTheLongerStep()
    {
        var s = new StarsSwirl(6, 1.3f, 3.3f, 0.9f, 30, Start, End, Xz);
        s.Step(0f, 0f, 1f, 0f);
        s.Step(Step, 0f, 1f, 0f);

        // v = (0, 0.5, -0.9) + 0.3 * (-0.9, 0, 0) (no y pull), p += 0.15 * v.
        Assert.Equal(0.9f + 0.3f * -0.9f * 0.15f, s.Sprites[0].X, 5);
        Assert.Equal(0.5f * 0.15f, s.Sprites[0].Y, 5);
        Assert.Equal(-0.9f * 0.15f, s.Sprites[0].Z, 5);
    }

    [Fact]
    public void Case6_SlotsLingerBy_SlotMod8_Over9Point5()
    {
        var s = new StarsSwirl(6, 1.3f, 3.3f, 0.9f, 30, Start, End, Xz);
        Assert.Equal(0f, s.Linger(0));
        Assert.Equal((float)(7 / 9.5), s.Linger(7));
        Assert.Equal(0f, s.Linger(8));
        Assert.Equal(0f, new StarsSwirl(11, 1.8f, 1.9f, 0.8f, 2, Start, End, Xz).Linger(7));
    }

    [Fact]
    public void SizeAndColour_FollowTheLifeFraction()
    {
        var s = new StarsSwirl(11, 1.8f, 1.9f, 0.8f, 2, Start, End, Xz);
        s.Step(0f, 0f, 1f, 0f);
        s.Step(Step, 0f, 1f, 0f);
        float t = (Step + 1.8f - 1.8f) / 1.8f;
        Assert.Equal(StarsCase3.Size(t, 1.9f), s.Sprites[0].Size, 5);
        Assert.Equal(StockColorRamp.Eval(Start, End, t), s.Sprites[0].Argb);
        Assert.Equal(15 - (int)((1.8 - Step) * 16.0 / 1.8), s.Sprites[0].Frame);
    }

    [Fact]
    public void Case10_SparksSitOnTheShell_FiveACall()
    {
        var s = new StarsCase10(0.45f, 0.4f, 0.8f, 0, 70, Start, End, Unit);
        s.Step(0f, 6f, 1f, 2f, 3f);
        Assert.Equal(5, s.LastCount);
        s.Step(Step, 6f, 1f, 2f, 3f);
        Assert.Equal(10, s.LastCount);

        Assert.Equal(1.8f, s.Sprites[0].X, 4);
        Assert.Equal(2f, s.Sprites[0].Y, 4);
        Assert.Equal(3f, s.Sprites[0].Z, 4);
        Assert.Equal(0, s.Sprites[0].Frame);

        // q = (0.45 - Step) / 0.45, u = 2q - 1; size = (1 - u²) * 0.7 + 0.4.
        double q = (0.45 - Step) / 0.45, u = 2 * q - 1;
        Assert.Equal((float)((1 - u * u) * 0.7 + 0.4), s.Sprites[0].Size, 4);
    }

    [Fact]
    public void Case10_AlphaIsTheDurationEnvelope()
    {
        var s = new StarsCase10(0.45f, 0.4f, 0.8f, 0, 70, Start, End, Unit);
        uint start = s.SharedColour(0f, out _);
        uint middle = s.SharedColour(0.5f, out _);
        Assert.Equal(0u, start >> 24);
        Assert.Equal(255u, middle >> 24);
        Assert.Equal(StockColorRamp.Eval(Start, End, 0.5f) & 0xffffffu, middle & 0xffffffu);
    }

    [Fact]
    public void Case10_Field30PicksThePaletteBySlot()
    {
        var s = new StarsCase10(0.45f, 0.4f, 0.8f, 1, 70, Start, End, Unit);
        s.Step(0f, 6f, 0f, 0f, 0f);
        s.Step(3f, 6f, 0f, 0f, 0f); // age 3 of 6: envelope 1
        s.Step(3f + Step, 6f, 0f, 0f, 0f);
        s.SharedColour((3f + Step) / 6f, out uint mask);
        Assert.Equal(StarsCase10.Palette[0] & mask, s.Sprites[0].Argb);
    }
}
