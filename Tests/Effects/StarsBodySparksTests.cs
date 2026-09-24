using System;
using System.Collections.Generic;
using Xunit;

/// <summary>
/// Locks <see cref="StarsBodySparks"/> to stock _GfxControlStars_t starType 15 (<c>100f9f5f</c>, lazy init
/// <c>100f811f</c>, vertex callback <c>100f740b</c>), with 43047 (the hit of 28604 Electrifying
/// Containment): life 350 ms, size 0.5, speed 0.5.
/// </summary>
public class StarsBodySparksTests
{
    static readonly float[] Start = { 1f, 0.2f, 0.2f, 1f };
    static readonly float[] End = { 1f, 0.2f, 1f, 0.2f };

    static Func<int> Rand(params int[] values)
    {
        var queue = new Queue<int>(values);
        return () => queue.Count > 0 ? queue.Dequeue() : 0;
    }

    static StarsBodySparks Make(float scale = 1f, Func<int> rand = null)
        => new StarsBodySparks(0.35f, 0.5f, 0.5f, scale, Start, End, rand ?? Rand());

    static StarsBodySparks.Vertex V(int i) => new StarsBodySparks.Vertex { X = i, Y = 0f, Z = 0f, NX = 0f, NY = 0f, NZ = 1f };

    [Fact]
    public void FirstCall_SpawnsTenFromTheDefaults_StraightUpFromTheDynel()
    {
        StarsBodySparks sim = Make();
        var frame = StarsBodySparks.Frame.At(1f, 2f, 3f);
        sim.Step(0f, frame);
        Assert.Equal(StarsBodySparks.SpawnsPerStep, sim.LastCount);
        Assert.Equal(10, sim.NextEntry);
        // The spawn call leaves the records as they were.
        Assert.False(sim.Sprites[0].Visible);

        float age = 1f / 30f;
        sim.Step(age, frame);
        Assert.Equal(20, sim.LastCount);
        StarsCase3.Sprite s = sim.Sprites[0];
        Assert.True(s.Visible);
        float v = 0.5f * StarsBodySparks.Drag;
        Assert.Equal(1f, s.X);
        Assert.Equal(2f + v * StarsBodySparks.Step01, s.Y, 6);
        Assert.Equal(3f, s.Z);
        float t = StarsCase3.LifeFraction(0.35f, age, 0.35f);
        Assert.Equal((float)((1.100000023841858 - t) * 0.5), s.Size, 6);
        Assert.Equal(StockColorRamp.Eval(Start, End, t), s.Argb);
        Assert.Equal(0, s.Frame);
        Assert.False(sim.Sprites[10].Visible);
    }

    [Fact]
    public void ReadGroup_WalksFromARandomStartInStepsOfTotalOver31()
    {
        StarsBodySparks sim = Make(rand: Rand(7));
        var read = new List<int>();
        sim.ReadGroup(3100, 0, 3100, i => { read.Add(i); return V(i); });
        // Stride 100 from vertex 7; vertex 3007 would be entry 30 and isn't copied.
        Assert.Equal(30, read.Count);
        Assert.Equal(507f, sim.Entry(5).X);
        Assert.Equal(2907f, sim.Entry(29).X);
        Assert.Equal(1f, sim.Entry(29).NZ);
    }

    [Fact]
    public void ReadGroup_PlacesEachGroupByItsBaseIndex()
    {
        StarsBodySparks sim = Make(rand: Rand(7, 25));
        sim.ReadGroup(1000, 0, 3100, V);
        sim.ReadGroup(2100, 1000, 3100, i => V(10000 + i));
        Assert.Equal(907f, sim.Entry(9).X);
        // The second group starts again at its own rand() % 30: vertex 25 is mesh vertex 1025, entry 10.
        Assert.Equal(10025f, sim.Entry(10).X);
        Assert.Equal(11925f, sim.Entry(29).X);
    }

    [Fact]
    public void ReadGroup_ASmallMeshStepsOneAtATime()
    {
        StarsBodySparks sim = Make(rand: Rand(33));
        sim.ReadGroup(20, 0, 20, V);
        // Stride 20 / 31 = 0 becomes 1; the walk starts at 33 % 30 = 3.
        Assert.Equal(3f, sim.Entry(3).X);
        Assert.Equal(19f, sim.Entry(19).X);
        Assert.Equal(0f, sim.Entry(2).X);
        Assert.Equal(1f, sim.Entry(2).NY);
    }

    [Fact]
    public void Spawn_TurnsTheSampleByTheDynel_ScalingOnlyThePosition()
    {
        StarsBodySparks sim = Make(scale: 2f, rand: Rand(0));
        sim.ReadGroup(31, 0, 31, i => new StarsBodySparks.Vertex { X = 1f, NX = 1f });
        // A quarter turn about y: x goes to -z, z to x.
        var frame = new StarsBodySparks.Frame
        {
            Px = 5f, Py = 0f, Pz = 0f,
            Xx = 0f, Xy = 0f, Xz = -1f,
            Yx = 0f, Yy = 1f, Yz = 0f,
            Zx = 1f, Zy = 0f, Zz = 0f,
        };
        sim.Step(0f, frame);
        sim.Step(1f / 30f, frame);
        StarsCase3.Sprite s = sim.Sprites[0];
        float v = 0.5f * StarsBodySparks.Drag;
        Assert.Equal(5f, s.X, 6);
        Assert.Equal(0f, s.Y, 6);
        Assert.Equal(-2f - v * StarsBodySparks.Step01, s.Z, 6);
    }

    [Fact]
    public void NextEntry_WrapsAfterThirty()
    {
        StarsBodySparks sim = Make();
        var frame = StarsBodySparks.Frame.At(0f, 0f, 0f);
        sim.Step(0f, frame);
        sim.Step(0.01f, frame);
        Assert.Equal(20, sim.NextEntry);
        sim.Step(0.02f, frame);
        Assert.Equal(0, sim.NextEntry);
    }

    [Fact]
    public void Terminating_StopsTheSpawns_ThenDrains()
    {
        StarsBodySparks sim = Make();
        var frame = StarsBodySparks.Frame.At(0f, 0f, 0f);
        sim.Step(0f, frame);
        sim.Terminating = true;
        sim.Step(0.1f, frame);
        Assert.Equal(10, sim.LastCount);
        Assert.False(sim.Drained);
        sim.Step(0.4f, frame);
        Assert.Equal(0, sim.LastCount);
        Assert.True(sim.Drained);
        Assert.False(sim.Sprites[0].Visible);
    }
}
