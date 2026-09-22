using Xunit;

/// <summary>
/// Locks <see cref="StarsCase19"/> to stock _GfxControlStars_t case 19 (<c>100fa9e4</c>), with 45597
/// (nano 150501's tracer): life 300 ms, size 0.44, scatter 0.55, a constant dark-red ramp.
/// </summary>
public class StarsCase19Tests
{
    const float Step = 1f / 30f;
    static readonly float[] Red = { 1f, 0.215f, 0.039f, 0.039f };

    // A ball point of (0, 0, 0) (16384 / 16384 - 1 on each axis), then a life jitter of 0.9.
    static StarsCase19 Make45597(System.Func<int> rand) => new StarsCase19(0.3f, 0.44f, 0.55f, Red, Red, rand);

    static StarsCase19 Centred()
    {
        int[] seq = { 16384, 16384, 16384, 0 };
        int i = 0;
        return Make45597(() => seq[i++ % seq.Length]);
    }

    static void StepAlongX(StarsCase19 s, float age, bool hasHitLocation = true)
        => s.Step(age, 1f, hasHitLocation, 0f, 1f, 0f, 10f, 1f, 0f);

    [Fact]
    public void FirstCall_OpensFifteenSlotsAndDrawsNothingYet()
    {
        StarsCase19 s = Centred();
        StepAlongX(s, 0f);
        Assert.Equal(15, s.LastCount);
        foreach (StarsCase3.Sprite sprite in s.Sprites)
            Assert.False(sprite.Visible);
    }

    [Fact]
    public void Spark_SitsStillAtTheProgressPointOfTheHitLocation()
    {
        StarsCase19 s = Centred();
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
        StarsCase19 s = Centred();
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
        StarsCase19 s = Centred();
        StepAlongX(s, 0f, hasHitLocation: false);
        StepAlongX(s, Step, hasHitLocation: false);
        Assert.Equal(0, s.LastCount);
    }

    [Fact]
    public void Terminating_StopsSpawningAndDrainsWithinALife()
    {
        StarsCase19 s = Centred();
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
        StarsCase19 s = Centred();
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
        StarsCase19 s = Make45597(() => seq[i++ % seq.Length]);
        StepAlongX(s, 0f);
        // 0.3 * (2047 * 0.0001 + 0.9) = 0.33141: still alive at 0.33, respawned at 0.3334.
        StepAlongX(s, 0.33f);
        Assert.True(s.Sprites[0].Visible);
        Assert.Equal(1, s.Sprites[0].Serial);
        StepAlongX(s, 0.3334f);
        StepAlongX(s, 0.34f);
        Assert.NotEqual(1, s.Sprites[0].Serial);
    }
}
