using System;
using Xunit;

/// <summary>
/// Locks <see cref="StarsCase12"/> to stock <c>_GfxControlStars_t</c> starType 12
/// (<c>100f9a7e</c>..<c>100f9c36</c>), using its only record 14100 — the hit of 257089 Alien Cocoon
/// and its kin: end size 0.3, duration 5, life 0.5 s, reach 0.8, gain 100 hundredths.
/// </summary>
public class StarsCase12Tests
{
    const float SizeEnd = 0.3f;
    const float Life = 0.5f;
    const float Reach = 0.8f;
    const int Gain = 100;
    const float Duration = 5f;

    /// <summary>
    /// A repeatable draw. It must vary: stock's <c>100d3005</c> (<see cref="ElectraSim.RandomUnitVector"/>)
    /// rejects candidates until one lands inside the unit sphere, so a constant source that happens to
    /// give the zero vector never returns.
    /// </summary>
    static Func<int> Rand()
    {
        int state = 12345;
        return () =>
        {
            state = unchecked(state * 1103515245 + 12345);
            return (state >> 8) & 0x7fff;
        };
    }

    static StarsCase12 Make(Func<int> rand = null) =>
        new StarsCase12(SizeEnd, Life, Reach, Gain, rand ?? Rand());

    [Fact]
    public void TheLifeIsFieldTwentyEightInSeconds()
    {
        // 100f9a8a: +0x16d4 is copied over +0x16cc every call, so field 30's milliseconds never apply.
        Assert.Equal(0.5f, Make().Life, 5);
    }

    [Fact]
    public void ACallStartsTwoStreaksOfFour()
    {
        // 100f9ac2: the budget is the literal 2; 100f9c0e: four slots each.
        StarsCase12 c = Make();
        c.Step(0f, Duration, 0f, 0f, 0f);

        // Slots 0 and 4 triggered the spawns and sit this call out; their three followers light up.
        Assert.False(c.Sprites[0].Visible);
        Assert.True(c.Sprites[1].Visible);
        Assert.True(c.Sprites[2].Visible);
        Assert.True(c.Sprites[3].Visible);
        Assert.False(c.Sprites[4].Visible);
        Assert.True(c.Sprites[5].Visible);
        // Nothing past the two streaks.
        Assert.False(c.Sprites[8].Visible);
    }

    [Fact]
    public void OnlyEveryFourthSlotCanStartAStreak()
    {
        // 100f9b9d: a slot with (i & 3) != 0 is hidden and skipped, never spawned on its own.
        StarsCase12 c = Make();
        for (int i = 0; i < 40; i++)
            c.Step(i / 30f, Duration, 0f, 0f, 0f);
        // Assert the stronger thing: every spark of a streak flies the same way. Only the visible ones
        // can be compared — a streak's four sparks die a tenth of a life apart, and the slot that
        // triggered the spawn sits its first call out, so a group is often part-shown.
        int compared = 0;
        for (int g = 0; g < StarsCase12.SlotCount / StarsCase12.GroupSize; g++)
        {
            int b = g * StarsCase12.GroupSize;
            string dir = null;
            for (int k = 0; k < StarsCase12.GroupSize; k++)
            {
                if (!c.Sprites[b + k].Visible)
                    continue;
                string d = DirOf(c, b + k);
                if (dir == null)
                    dir = d;
                else
                {
                    Assert.Equal(dir, d);
                    compared++;
                }
            }
        }
        Assert.True(compared > 20, $"only {compared} sparks shared a streak; the test proved nothing");
    }

    [Fact]
    public void TheFourSparksOfAStreakTrailByATenthOfALife()
    {
        // 100f9be6: death = age + life - k * 0.1 * life.
        StarsCase12 c = Make();
        c.Step(0f, Duration, 0f, 0f, 0f);
        c.Step(0.01f, Duration, 0f, 0f, 0f);

        // Slot 0 is the leader, slots 1-3 are progressively further through their lives.
        float u0 = UOf(c, 0), u1 = UOf(c, 1), u2 = UOf(c, 2), u3 = UOf(c, 3);
        Assert.True(u0 > u1 && u1 > u2 && u2 > u3);
        Assert.Equal(0.1f, u0 - u1, 3);
        Assert.Equal(0.1f, u1 - u2, 3);
    }

    [Fact]
    public void ASparkFliesOutToTheReachAsItDies()
    {
        StarsCase12 c = Make();
        c.Step(0f, Duration, 0f, 0f, 0f);
        c.Step(0.25f, Duration, 0f, 0f, 0f);

        // Slot 0 was born at 0 and dies at 0.5, so it is halfway: t = 0.5.
        StarsCase3.Sprite s = c.Sprites[0];
        Assert.True(s.Visible);
        float r = (float)Math.Sqrt(s.X * s.X + s.Y * s.Y + s.Z * s.Z);
        Assert.Equal(Reach * 0.5f, r, 3);
    }

    [Fact]
    public void ASparkShrinksFromTheGainDownToFieldEleven()
    {
        // 100f9b16: size = field 11 + u * field 31 / 100, and u falls 1 to 0.
        StarsCase12 c = Make();
        c.Step(0f, Duration, 0f, 0f, 0f);

        c.Step(0.01f, Duration, 0f, 0f, 0f);
        float young = c.Sprites[0].Size;
        c.Step(0.49f, Duration, 0f, 0f, 0f);
        float old = c.Sprites[0].Size;

        Assert.True(young > old);
        // gain 100 hundredths = 1.0, and at age 0.01 of a 0.5 s life u is 0.98.
        Assert.Equal(SizeEnd + 0.98f, young, 2);
        Assert.Equal(SizeEnd + 0.02f, old, 2);
    }

    [Fact]
    public void EachStreakTakesOneFlatColourFromThePalette()
    {
        // 100f9af1: (i >> 2) & 7 — per streak, not a ramp.
        StarsCase12 c = Make();
        for (int i = 0; i < 20; i++)
            c.Step(i / 30f, Duration, 0f, 0f, 0f);

        for (int i = 0; i < StarsCase12.SlotCount; i++)
            if (c.Sprites[i].Visible)
                Assert.Equal(StarsCase12.Palette[(i >> 2) & 7], c.Sprites[i].Argb);

        Assert.Equal(8, StarsCase12.Palette.Length);
        Assert.Equal(0xffffc0c0u, StarsCase12.Palette[0]);
        Assert.Equal(0xffc0ffc0u, StarsCase12.Palette[7]);
    }

    [Fact]
    public void ThePositionIsRelativeToTheLocator()
    {
        StarsCase12 c = Make();
        c.Step(0f, Duration, 3f, 4f, 5f);
        c.Step(0.25f, Duration, 3f, 4f, 5f);
        StarsCase3.Sprite s = c.Sprites[0];
        float r = (float)Math.Sqrt(
            (s.X - 3f) * (s.X - 3f) + (s.Y - 4f) * (s.Y - 4f) + (s.Z - 5f) * (s.Z - 5f));
        Assert.Equal(Reach * 0.5f, r, 3);
    }

    [Fact]
    public void TerminatingStopsTheStreaksAndThenDrains()
    {
        StarsCase12 c = Make();
        c.Step(0f, Duration, 0f, 0f, 0f);
        Assert.False(c.Drained);

        c.Terminating = true;
        c.Step(0.1f, Duration, 0f, 0f, 0f);
        Assert.False(c.Drained);                   // the first streaks are still out
        // Nothing new is started while terminating.
        int visible = 0;
        foreach (StarsCase3.Sprite s in c.Sprites)
            if (s.Visible)
                visible++;
        Assert.InRange(visible, 1, 8);

        c.Step(1f, Duration, 0f, 0f, 0f);          // well past the 0.5 s life
        Assert.True(c.Drained);
    }

    [Fact]
    public void ItFillsAllThirtyTwoStreaksAndSettles()
    {
        StarsCase12 c = Make();
        for (int i = 0; i < 60; i++)
            c.Step(i / 30f, Duration, 0f, 0f, 0f);

        int visible = 0;
        foreach (StarsCase3.Sprite s in c.Sprites)
            if (s.Visible)
                visible++;
        // Two streaks a call at 30 Hz is 60 a second, each lasting 0.5 s, so 30 streaks want to be up
        // at once out of the 32 the 128 slots allow: this type runs its pool nearly full.
        Assert.InRange(visible, 90, StarsCase12.SlotCount);
    }

    static string DirOf(StarsCase12 c, int i)
    {
        StarsCase3.Sprite s = c.Sprites[i];
        if (!s.Visible)
            return "hidden";
        float r = (float)Math.Sqrt(s.X * s.X + s.Y * s.Y + s.Z * s.Z);
        if (r <= 0f)
            return "zero";
        return $"{s.X / r:F3},{s.Y / r:F3},{s.Z / r:F3}";
    }

    /// <summary>What is left of slot i's life, recovered from how far it has flown.</summary>
    static float UOf(StarsCase12 c, int i)
    {
        StarsCase3.Sprite s = c.Sprites[i];
        float r = (float)Math.Sqrt(s.X * s.X + s.Y * s.Y + s.Z * s.Z);
        return 1f - r / Reach;
    }
}
