using System;
using Xunit;

/// <summary>
/// Locks <see cref="Trail2Sim"/> to stock GfxControlTrail2_t (Process <c>10115605</c>) and GfxVisualTrail2
/// (Sample <c>1002c7bd</c>, build <c>1002c918</c>, frame update <c>1002d169</c>) with 72301 (attractor 2006)
/// in the Yalmaha buff Meta 72308.
/// </summary>
public class Trail2SimTests
{
    static float I(uint v) => BitConverter.Int32BitsToSingle(unchecked((int)v));

    static float[] Engine() => new[]
    {
        I(0x1c03), 0f, 0f, 0f, 0f, 0f, 0f, I(2006), -1f, 0f, I(45), I(10), 0f, 0.5f, 4f, 1f,
        I(4), 0f, I(0xff7070ff), 0.45f, I(0xffaaaaff), 0.55f, I(0xffaaaaff), 1f, I(0xff7070ff),
        I(3), 0f, 0.2f, 0.5f, 0.22f, 1f, 0.2f,
        I(3), 0f, 0.2f, 0.5f, 0.22f, 1f, 0.2f,
    };

    static Trail2Sim.Sample At(float x, float y, float z, uint colour = 0u) => new Trail2Sim.Sample
    {
        AxisX = new Trail2Sim.V3(1f, 0f, 0f),
        AxisY = new Trail2Sim.V3(0f, 1f, 0f),
        AxisZ = new Trail2Sim.V3(0f, 0f, 1f),
        Place = new Trail2Sim.V3(x, y, z),
        Colour = colour,
    };

    [Fact]
    public void Loads_72301()
    {
        var sim = new Trail2Sim(Engine());
        Assert.Equal(0x1c03, sim.Flags);
        Assert.Equal(45, sim.Material);
        Assert.Equal(10, sim.Length);
        Assert.Equal(0f, sim.Interval);
        Assert.Equal(0.5f, sim.Rate);
        Assert.Equal(4f, sim.Thrust);
        Assert.Equal(1f, sim.Fluctuate);
    }

    [Fact]
    public void Sample_RewritesTheFirstSlots_WithTheNewest()
    {
        var sim = new Trail2Sim(Engine());
        sim.Reset(At(0f, 0f, 0f));
        sim.Push(At(1f, 0f, 0f));
        Assert.Equal(1, sim.Index);
        Assert.Equal(1, sim.Count);
        for (int i = 0; i < 9; i++)
            Assert.Equal(1f, sim[i].Place.X);
        Assert.Equal(0f, sim[9].Place.X);

        sim.Push(At(2f, 0f, 0f)); // written at 1, then 0 .. 7 rewritten
        for (int i = 0; i < 8; i++)
            Assert.Equal(2f, sim[i].Place.X);
        Assert.Equal(1f, sim[8].Place.X);
        Assert.Equal(0f, sim[9].Place.X);
    }

    [Fact]
    public void TheFill_FoldsTheHistory_WhenTheRingFirstFills()
    {
        // Each fill rewrites slots 0 .. N - count - 1, not the unused ones, so the first full ring runs
        // 9 8 7 6 5 6 7 8 9 10.
        var sim = new Trail2Sim(Engine());
        sim.Reset(At(0f, 0f, 0f));
        for (int k = 1; k <= 10; k++)
            sim.Push(At(k, 0f, 0f));
        float[] expected = { 9, 8, 7, 6, 5, 6, 7, 8, 9, 10 };
        for (int i = 0; i < 10; i++)
            Assert.Equal(expected[i], sim[i].Place.X);
        Assert.Equal(0, sim.Index);
        Assert.Equal(10, sim.Count);
    }

    [Fact]
    public void AFullRing_DrawsOldestToOrigin()
    {
        var sim = new Trail2Sim(Engine());
        sim.Reset(At(0f, 0f, 0f));
        for (int k = 1; k <= 22; k++)
            sim.Push(At(k, 0f, 0f));
        sim.SetOrigin(At(99f, 0f, 0f));
        Assert.Equal(10, sim.Count);
        Assert.Equal(2, sim.Index);
        Assert.Equal(11, sim.PointCount);
        // Samples 13 .. 21 in order (the newest, 22, isn't drawn), then the origin twice.
        for (int i = 0; i < 9; i++)
            Assert.Equal(13f + i, sim.Point(i).Place.X);
        Assert.Equal(99f, sim.Point(9).Place.X);
        Assert.Equal(99f, sim.Point(10).Place.X);
    }

    [Fact]
    public void Head_ScalesTheAxes_AndColoursFromTheCurves()
    {
        var sim = new Trail2Sim(Engine());
        sim.Reset(At(0f, 0f, 0f));
        // Moving forward: d along +z, the dynel's z.
        Assert.True(sim.Head(1f, At(0f, 0f, 1f), new Trail2Sim.V3(0f, 0f, 1f), out Trail2Sim.Sample head));
        // t = fmod(0.5 · 1, 1) = 0.5: sizes 0.22, the colour between the 0.45 and 0.55 keys.
        Assert.Equal(0.22f, head.AxisX.X, 5);
        Assert.Equal(0.22f, head.AxisY.Y, 5);
        Assert.Equal(0xffaaaaffu, head.Colour);
    }

    [Fact]
    public void Reversing_BlacksTheSample()
    {
        var sim = new Trail2Sim(Engine());
        sim.Reset(At(0f, 0f, 0f));
        sim.Head(1f, At(0f, 0f, -1f), new Trail2Sim.V3(0f, 0f, 1f), out Trail2Sim.Sample back);
        Assert.Equal(0u, back.Colour);

        // Sideways (dot 0) and standing still keep the curve's colour.
        sim.Head(1f, At(1f, 0f, 0f), new Trail2Sim.V3(0f, 0f, 1f), out Trail2Sim.Sample side);
        Assert.Equal(0xffaaaaffu, side.Colour);
        sim.Head(1f, At(0f, 0f, 0f), new Trail2Sim.V3(0f, 0f, 1f), out Trail2Sim.Sample still);
        Assert.Equal(0xffaaaaffu, still.Colour);
    }

    [Fact]
    public void Build_FourCrossedStrips_AlongV()
    {
        var sim = new Trail2Sim(Engine());
        sim.Reset(At(0f, 0f, 0f));
        for (int k = 1; k <= 20; k++)
            sim.Push(At(0f, 0f, k, 0xff000000u + (uint)k));
        sim.SetOrigin(At(0f, 0f, 20f, 0xffffffffu));
        Trail2Sim.Strip[] strips = sim.NewStrips();
        sim.Build(strips);
        Assert.Equal(22, strips[0].Count);

        // Point 0 is sample 11 (the oldest, at index 0): strip 0 is (P + Y, P - Y), uv (0, 0) and (1, 0).
        Assert.Equal(1f, strips[0].Positions[1]);
        Assert.Equal(-1f, strips[0].Positions[4]);
        Assert.Equal(11f, strips[0].Positions[2]);
        Assert.Equal(0f, strips[0].Uvs[0]);
        Assert.Equal(0f, strips[0].Uvs[1]);
        Assert.Equal(1f, strips[0].Uvs[2]);
        Assert.Equal(0xff00000bu, strips[0].Colours[0]);
        // Strip 2 is (P - X + Y, P + X - Y).
        Assert.Equal(-1f, strips[2].Positions[0]);
        Assert.Equal(1f, strips[2].Positions[1]);
        Assert.Equal(1f, strips[2].Positions[3]);
        Assert.Equal(-1f, strips[2].Positions[4]);
        // The last point is the origin, at v = 10 / 10.
        Assert.Equal(20f, strips[1].Positions[21 * 3 + 2]);
        Assert.Equal(1f, strips[1].Uvs[21 * 2 + 1]);
    }

    [Fact]
    public void Drift_MovesTheCountedSamples_AlongTheOriginsZ()
    {
        var sim = new Trail2Sim(Engine());
        sim.Reset(At(0f, 0f, 0f));
        sim.Push(At(0f, 0f, 0f));
        sim.Push(At(0f, 0f, 0f));
        sim.SetOrigin(At(0f, 0f, 0f));
        sim.Drift(0.1f, 0.5f); // (4 + 1 · 0.5) · 0.1 = 0.45
        Assert.Equal(0.45f, sim[0].Place.Z, 5);
        Assert.Equal(0.45f, sim[1].Place.Z, 5);
        Assert.Equal(0f, sim[2].Place.Z);
    }

    [Fact]
    public void AnInterval_TakesOneSamplePerInterval()
    {
        float[] f = Engine();
        f[12] = 0.1f;
        var sim = new Trail2Sim(f);
        sim.Reset(At(0f, 0f, 0f));
        sim.Take(At(1f, 0f, 0f), 0.05f);
        Assert.Equal(0, sim.Count);
        sim.Take(At(1f, 0f, 0f), 0.26f);
        Assert.Equal(3, sim.Count);
    }
}
