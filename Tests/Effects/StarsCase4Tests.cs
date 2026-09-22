using Xunit;

/// <summary>
/// Locks <see cref="StarsCase4"/> to stock _GfxControlStars_t case 4 (<c>100f8c39</c>), with 72391
/// (nano 269470's hand flame): life 285 ms, size 0.3, launch speed 0.015.
/// </summary>
public class StarsCase4Tests
{
    const float Step = 1f / 30f;
    static readonly float[] Start = { 0.7f, 0f, 0.34f, 0.99f };
    static readonly float[] End = { 0.2f, 0f, 0.54f, 0.99f };

    /// <summary>rand() = 16384 puts the ball point at the centre, so a spark launches straight up.</summary>
    static StarsCase4 Make() => new StarsCase4(0.285f, 0.3f, 0.015f, Start, End, () => 16384);

    [Fact]
    public void TwoSpawnsACall_AndTheSpawnCallLeavesTheRecord()
    {
        StarsCase4 s = Make();
        s.Step(0f, 1f, 2f, 3f);
        Assert.Equal(2, s.LastCount);
        Assert.False(s.Sprites[0].Visible);
        Assert.False(s.Sprites[1].Visible);

        s.Step(Step, 1f, 2f, 3f);
        Assert.Equal(4, s.LastCount);
        Assert.True(s.Sprites[0].Visible);
        Assert.False(s.Sprites[2].Visible);
    }

    [Fact]
    public void Velocity_IsDraggedAndLifted_ThenMovesATenthOfItself()
    {
        StarsCase4 s = Make();
        s.Step(0f, 1f, 2f, 3f);
        s.Step(Step, 1f, 2f, 3f);

        // v0 = 0.015 * (0, 3, 0); v1 = v0 * 0.95 + (0, 0.1, 0); p1 = p0 + v1 * 0.1.
        float vy = 0.045f * 0.95f + 0.1f;
        Assert.Equal(1f, s.Sprites[0].X, 6);
        Assert.Equal(2f + vy * 0.1f, s.Sprites[0].Y, 5);
        Assert.Equal(3f, s.Sprites[0].Z, 6);

        s.Step(2 * Step, 1f, 2f, 3f);
        float vy2 = vy * 0.95f + 0.1f;
        Assert.Equal(2f + (vy + vy2) * 0.1f, s.Sprites[0].Y, 5);
    }

    [Fact]
    public void SizeFrameAndColour_FollowTheLifeFraction()
    {
        StarsCase4 s = Make();
        s.Step(0f, 0f, 0f, 0f);
        s.Step(Step, 0f, 0f, 0f);

        float t = (0.285f + Step - 0.285f) / 0.285f;
        Assert.Equal((2f - t) * 0.3f, s.Sprites[0].Size, 4);
        Assert.Equal((int)((0.285 - Step) * 64.0 / 0.285), s.Sprites[0].Frame);
        Assert.Equal(StockColorRamp.Eval(Start, End, t), s.Sprites[0].Argb);
    }

    [Fact]
    public void FrameIndex_ClampsTo0And63()
    {
        Assert.Equal(63, StarsCase4.FrameIndex(1f, 0f, 0.5f));
        Assert.Equal(0, StarsCase4.FrameIndex(0f, 0.1f, 0.5f));
    }

    [Fact]
    public void Terminating_StopsSpawns_AndDrains()
    {
        StarsCase4 s = Make();
        s.Step(0f, 0f, 0f, 0f);
        s.Terminating = true;
        float age = 0f;
        for (int i = 0; i < 20 && !s.Drained; i++)
        {
            age += Step;
            s.Step(age, 0f, 0f, 0f);
        }
        Assert.True(s.Drained);
        Assert.True(age > 0.285f);
    }
}
