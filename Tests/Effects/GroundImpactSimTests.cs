using System;
using System.Collections.Generic;
using Xunit;

/// <summary>
/// Locks <see cref="GroundImpactSim"/> to stock's GfxVisualForceSword_t (InitMesh <c>10015730</c>,
/// Render <c>10015f21</c>), which is everything type 0x1388 draws — its control discards its record.
/// One record, 90000, and one nano, 259452 Recovering from Teleport.
/// </summary>
public class GroundImpactSimTests
{
    /// <summary>A stream that repeats, so a test can pin the column count and the jitter.</summary>
    static Func<double> Fixed(params double[] values)
    {
        var q = new Queue<double>(values);
        return () =>
        {
            double v = q.Dequeue();
            q.Enqueue(v);
            return v;
        };
    }

    /// <summary>rand() % 10 + 5 with the first draw forced, so Columns is known.</summary>
    static GroundImpactSim WithColumns(int columns, params double[] rest)
    {
        // Columns = (int)(r * 32768) % 10 + 5, so pick r to land on the wanted remainder.
        double first = (columns - 5) / 32768.0;
        var values = new List<double> { first };
        values.AddRange(rest.Length > 0 ? rest : new double[] { 0.5 });
        var q = new Queue<double>(values);
        bool firstDraw = true;
        Func<double> stream = () =>
        {
            if (firstDraw)
            {
                firstDraw = false;
                q.Dequeue();
                return first;
            }
            double v = q.Dequeue();
            q.Enqueue(v);
            return v;
        };
        return new GroundImpactSim(stream);
    }

    [Fact]
    public void TheLatticeIsNineRowsByAColumnCountOfFiveToFourteen()
    {
        // 1001579d: rand() % 10 + 5.
        for (int c = 5; c <= 14; c++)
        {
            var sim = WithColumns(c);
            Assert.Equal(c, sim.Columns);
            Assert.Equal(c * 9, sim.VertexCount);
        }
        Assert.Equal(4, GroundImpactSim.BladeRows);
        Assert.Equal(5, GroundImpactSim.TrailRows);
        Assert.Equal(9, GroundImpactSim.Rows);

        // The stream is only consulted once for the count, however many columns come out.
        var lo = new GroundImpactSim(Fixed(0.0));
        Assert.Equal(5, lo.Columns);
    }

    [Fact]
    public void TheTwoHalvesOfTheBufferAreIndexedDifferently()
    {
        var sim = WithColumns(8);
        // 10016311: the blade's corners run col * 4 + corner...
        Assert.Equal(0, sim.Blade(0, 0));
        Assert.Equal(3, sim.Blade(0, 3));
        Assert.Equal(4, sim.Blade(1, 0));
        Assert.Equal(31, sim.Blade(7, 3));
        // ...while 100162f3's trail rows run row * columns + col. Misreading one for the other is the
        // easy mistake here.
        Assert.Equal(32, sim.Trail(4, 0));
        Assert.Equal(39, sim.Trail(4, 7));
        Assert.Equal(40, sim.Trail(5, 0));
        Assert.Equal(71, sim.Trail(8, 7));
        Assert.Equal(sim.VertexCount - 1, sim.Trail(8, 7));
    }

    [Fact]
    public void TheIndexListIsThirtySixPerColumnPair()
    {
        // 10015a6d: (5 + 1) * (columns - 1) * 6.
        var sim = WithColumns(9);
        Assert.Equal(36 * 8, sim.Indices.Length);
        foreach (int i in sim.Indices)
        {
            Assert.True(i >= 0);
            Assert.True(i < sim.VertexCount);
        }

        // The first pair is the blade's two crossed quads (corners 0-1 and 2-3).
        Assert.Equal(new[] { 0, 1, 4, 1, 5, 4, 2, 3, 6, 3, 7, 6 }, sim.Indices[..12]);
    }

    [Fact]
    public void TheUvsAreSetOnceAndNeverRun()
    {
        var sim = WithColumns(6);
        // 1001582e: u alternates 0, 1, 0, 1 and v is a flat 0.65 on every column — there really is
        // no run along the blade.
        for (int col = 0; col < sim.Columns; col++)
        {
            for (int c = 0; c < GroundImpactSim.BladeRows; c++)
            {
                int v = sim.Blade(col, c);
                Assert.Equal((c & 1) == 0 ? 0f : 1f, sim.Uvs[v * 2], 5);
                Assert.Equal(0.65f, sim.Uvs[v * 2 + 1], 5);
            }
        }
        // 100158b4: the trail sits on one texel.
        int t = sim.Trail(6, 2);
        Assert.Equal(0.5f, sim.Uvs[t * 2], 5);
        Assert.Equal(0.5f, sim.Uvs[t * 2 + 1], 5);
    }

    [Fact]
    public void EveryVertexIsTheSameBlue_AndOnlyTheTipStartsClear()
    {
        var sim = WithColumns(7);
        // 10015922: B 0xff, G 0x9b, R 0x32.
        Assert.Equal(0x3299ffu, GroundImpactSim.Rgb);
        for (int v = 0; v < sim.VertexCount; v++)
            Assert.Equal(GroundImpactSim.Rgb, sim.Colours[v] & 0x00ffffffu);

        // 10015922 again: the last column's four corners start at alpha 0 so the point fades out.
        for (int c = 0; c < GroundImpactSim.BladeRows; c++)
            Assert.Equal(0u, sim.Colours[sim.Blade(sim.Columns - 1, c)] >> 24);
        for (int c = 0; c < GroundImpactSim.BladeRows; c++)
            Assert.Equal(0xffu, sim.Colours[sim.Blade(0, c)] >> 24);
    }

    [Fact]
    public void TheBladeSweepsDownTheConnectorsOwnZ()
    {
        var sim = WithColumns(6, 0.5);   // 0.5 -> no jitter either way
        sim.Size = 3f;
        sim.Step();

        // 10015f86: the blade runs from the connector to -size over columns - 1 steps.
        Assert.Equal(0f, sim.Positions[sim.Blade(0, 0) * 3 + 2], 4);
        Assert.Equal(-3f, sim.Positions[sim.Blade(5, 0) * 3 + 2], 4);
        Assert.Equal(-3f / 5f, sim.Positions[sim.Blade(1, 0) * 3 + 2], 4);

        // 10015ff5: a square cross-section of +-0.05, corner 0 at (+,+) and 1 at (-,-), which is what
        // makes the two index quads cross rather than overlap.
        Assert.Equal(GroundImpactSim.Corner, sim.Positions[sim.Blade(0, 0) * 3], 4);
        Assert.Equal(GroundImpactSim.Corner, sim.Positions[sim.Blade(0, 0) * 3 + 1], 4);
        Assert.Equal(-GroundImpactSim.Corner, sim.Positions[sim.Blade(0, 1) * 3], 4);
        Assert.Equal(-GroundImpactSim.Corner, sim.Positions[sim.Blade(0, 1) * 3 + 1], 4);
        Assert.Equal(GroundImpactSim.Corner, sim.Positions[sim.Blade(0, 2) * 3], 4);
        Assert.Equal(-GroundImpactSim.Corner, sim.Positions[sim.Blade(0, 2) * 3 + 1], 4);
    }

    [Fact]
    public void TheJitterIsCappedAndNeverTouchesTheEnds()
    {
        var sim = WithColumns(10, 1.0);   // the largest draw -> the largest nudge
        sim.Step();
        // 10016016: +-0.005, and both tapers are 0 at one end or the other, so the ends stay put.
        for (int col = 0; col < sim.Columns; col++)
        {
            for (int c = 0; c < GroundImpactSim.BladeRows; c++)
            {
                int v = sim.Blade(col, c) * 3;
                float dx = Math.Abs(sim.Positions[v]) - GroundImpactSim.Corner;
                Assert.True(Math.Abs(dx) <= GroundImpactSim.Jitter * 1.01f);
            }
        }
    }

    [Fact]
    public void TheFlickerStaysInStocksRange_AndTheTrailHalvesPerLink()
    {
        var sim = WithColumns(9, 0.9, 0.2, 0.7, 0.4);
        sim.Step();

        for (int col = 1; col < sim.Columns - 1; col++)
        {
            int max = 0;
            for (int c = 0; c < GroundImpactSim.BladeRows; c++)
            {
                int a = (int)(sim.Colours[sim.Blade(col, c)] >> 24);
                // 100161cc: rand() % 200 + 0x37.
                Assert.InRange(a, GroundImpactSim.FlickerBase, GroundImpactSim.FlickerBase + 199);
                if (a > max) max = a;
            }
            // 1001626f: link t takes the brightest corner shifted right by t + 1.
            for (int t = 0; t < GroundImpactSim.TrailRows; t++)
                Assert.Equal((uint)(max >> (t + 1)), sim.Colours[sim.Trail(4 + t, col)] >> 24);
        }

        // The two end columns keep what InitMesh gave them.
        Assert.Equal(0xffu, sim.Colours[sim.Blade(0, 0)] >> 24);
        Assert.Equal(0u, sim.Colours[sim.Blade(sim.Columns - 1, 0)] >> 24);
    }

    [Fact]
    public void TheTrailLagsOneLinkPerCall()
    {
        var sim = WithColumns(5, 0.5);
        sim.Size = 1f;

        // First call: nothing has been written yet, so every link falls back to the centre
        // (10007cc2's "still zero" test).
        sim.Step();
        int centre = sim.Trail(4, 0) * 3;
        for (int t = 1; t < GroundImpactSim.TrailRows; t++)
        {
            int v = sim.Trail(4 + t, 0) * 3;
            Assert.Equal(sim.Positions[centre + 2], sim.Positions[v + 2], 5);
        }

        // Move the blade and step again: link 1 now holds what link 0 held, not the new centre.
        sim.Size = 5f;
        float wasLink0 = sim.Positions[sim.Trail(4, 1) * 3 + 2];
        sim.Step();
        Assert.Equal(wasLink0, sim.Positions[sim.Trail(5, 1) * 3 + 2], 5);
        Assert.NotEqual(wasLink0, sim.Positions[sim.Trail(4, 1) * 3 + 2]);
    }

    [Fact]
    public void SetColourKeepsTheAlphas()
    {
        var sim = WithColumns(6);
        sim.Step();
        uint beforeTip = sim.Colours[sim.Blade(sim.Columns - 1, 0)] >> 24;
        sim.SetColour(1f, 0f, 0f);
        // 1001564b writes the three colour bytes and leaves +0xf alone.
        Assert.Equal(0xff0000u, sim.Colours[0] & 0x00ffffffu);
        Assert.Equal(beforeTip, sim.Colours[sim.Blade(sim.Columns - 1, 0)] >> 24);
    }
}
