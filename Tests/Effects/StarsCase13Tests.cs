using System;
using Xunit;

/// <summary>
/// Locks <see cref="StarsCase13"/> to stock _GfxControlStars_t case 13 (<c>100f9c3b</c>) and its heading
/// step (<c>100f7ec9</c>), with 43040 (a hit of 204594 Weeping Flesh): size 0.2, reach 0.9, turn 0.06.
/// </summary>
public class StarsCase13Tests
{
    static readonly float[] Start = { 1f, 1f, 0.2f, 0.2f };
    static readonly float[] End = { 0.6f, 0.99f, 0.54f, 0f };

    /// <summary>A rand() whose unit vector is always (0, 0, 1): x and y draws at 16384 (0), z at 32767.</summary>
    static Func<int> AlongZ()
    {
        int i = 0;
        int[] seq = { 16384, 16384, 32767 };
        return () => seq[i++ % 3];
    }

    static StarsCase13 Make(Func<int> rand = null) => new StarsCase13(0.2f, 0.9f, 0.06f, Start, End, rand ?? AlongZ());

    [Fact]
    public void Heading_TurnsByMinusField29_AboutTheAxis()
    {
        // With the wander along z the axis stays (0, 0, 1); each step turns the heading by -0.06 rad about z.
        var sim = Make();
        sim.Step(0f, 10f, 0f, 0f, 0f);
        int steps = StarsCase13.SlotCount + StarsCase13.StepsPerCall;
        sim.Heading(out float x, out float y, out float z);
        double angle = -0.06 * steps;
        // (0, 1, 0) turned by angle about z: (-sin a, cos a, 0).
        Assert.Equal(-Math.Sin(angle), x, 3);
        Assert.Equal(Math.Cos(angle), y, 3);
        Assert.Equal(0f, z, 5);
    }

    [Fact]
    public void Envelope_HidesEverything_AtTheStartAndEnd()
    {
        var sim = Make();
        sim.Step(0f, 10f, 0f, 0f, 0f);
        foreach (var s in sim.Sprites)
            Assert.False(s.Visible);
    }

    [Fact]
    public void AtHalfTime_TheWholeTrailShows_OutToField28()
    {
        var sim = Make();
        sim.Step(0f, 10f, 1f, 2f, 3f);
        sim.Step(5f, 10f, 1f, 2f, 3f); // env = 1
        int head = sim.Head;
        Assert.Equal((StarsCase13.SlotCount + 2 * StarsCase13.StepsPerCall) % StarsCase13.SlotCount, head);

        var newest = sim.Sprites[head];
        Assert.True(newest.Visible);
        Assert.Equal(1f, newest.X, 5); // f = 0: on the locator
        Assert.Equal(2f, newest.Y, 5);
        Assert.Equal(0.2f, newest.Size);

        int oldest = (head + 1) % StarsCase13.SlotCount; // f = 127 / 128
        var s = sim.Sprites[oldest];
        Assert.True(s.Visible);
        float distance = (float)Math.Sqrt((s.X - 1f) * (s.X - 1f) + (s.Y - 2f) * (s.Y - 2f) + (s.Z - 3f) * (s.Z - 3f));
        Assert.Equal(0.9f * 127f / 128f, distance, 4);
        Assert.Equal(StockColorRamp.Eval(Start, End, 127f / 128f), s.Argb);
    }

    [Fact]
    public void Envelope_TrimsTheOldEnd()
    {
        var sim = Make();
        sim.Step(0f, 10f, 0f, 0f, 0f);
        sim.Step(1f, 10f, 0f, 0f, 0f); // p 0.1: env = 1 - 0.64 = 0.36
        int head = sim.Head;
        int shown = 0;
        for (int i = 0; i < StarsCase13.SlotCount; i++)
        {
            float f = ((head - i + 128) % 128) / 128f;
            Assert.Equal(f < 0.36f, sim.Sprites[i].Visible);
            if (sim.Sprites[i].Visible)
                shown++;
        }
        Assert.Equal(47, shown); // f = 0 .. 46/128
    }
}
