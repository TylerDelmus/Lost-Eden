using System;
using Xunit;

/// <summary>
/// Locks <see cref="VulcanRocksSim"/> to stock _GfxControlVulcanRocks_t (Process <c>10103bc1</c>) with
/// 45060, the hit of 157988 Fiery Breath (through Meta 47400).
/// </summary>
public class VulcanRocksSimTests
{
    static float Bits(uint i) => BitConverter.Int32BitsToSingle(unchecked((int)i));

    static float[] Record45060()
    {
        var f = new float[30];
        f[0] = Bits(1);
        f[8] = -1f;
        f[9] = Bits(15);
        f[10] = 18f;
        f[11] = Bits(0x3fa0d97c); // 1.2566371
        f[12] = Bits(0x3fc90fdb); // pi / 2
        f[13] = f[14] = f[15] = f[16] = 1f;
        f[17] = Bits(7);
        f[20] = 23f;
        f[21] = 256f;
        f[22] = Bits(7);
        for (int i = 0; i < 7; i++)
            f[23 + i] = Bits((uint)(4 + i));
        return f;
    }

    /// <summary>Hands out <paramref name="values"/> in turn, then 0.5.</summary>
    static Func<float> Sequence(params float[] values)
    {
        int next = 0;
        return () => next < values.Length ? values[next++] : 0.5f;
    }

    static readonly VulcanRocksSim.Frame Identity = new VulcanRocksSim.Frame { XX = 1f, YY = 1f, ZZ = 1f };

    static bool FlatGround(float x, float y, float z, out float height, out float nx, out float ny, out float nz)
    {
        height = 0f;
        nx = 0f; ny = 1f; nz = 0f;
        return true;
    }

    static VulcanRocksSim Make(Func<float> rand, Func<int, bool> claim = null)
        => new VulcanRocksSim(Record45060(), rand, claim ?? (slot => slot == 4), FlatGround);

    [Fact]
    public void Loader_ReadsTheModelsAndTheListSize()
    {
        VulcanRocksSim sim = Make(Sequence());
        Assert.Equal(new[] { 4, 5, 6, 7, 8, 9, 10 }, sim.Models);
        Assert.Equal(23, sim.Capacity);
        Assert.False(sim.DeleteRocks); // field 30 is past the record
        Assert.Equal(0xffffffffu, sim.Argb);
        Assert.Equal(-1f, sim.Duration);
    }

    [Fact]
    public void FirstCall_ThrowsOnce_AndOnlyALoadedSlotGivesARock()
    {
        // r 0.2: _ftol(0.2 * 6.9999) = 1, slot 5, a texture: no rock and no further draws.
        VulcanRocksSim sim = Make(Sequence(0.2f));
        sim.Step(0f, 0f, Identity);
        Assert.Equal(1f, sim.Thrown);
        Assert.Empty(sim.Rocks);

        // r 0.1: pick 0, slot 4.
        sim = Make(Sequence(0.1f));
        sim.Step(0f, 0f, Identity);
        Assert.Single(sim.Rocks);
        Assert.Equal(4, sim.Rocks[0].Model);
    }

    [Fact]
    public void Throw_GoesAlongTheLocatorsAxes()
    {
        // pick 0, t = r 2pi = pi / 2 (sin 1), p = field 11 (r 0).
        VulcanRocksSim sim = Make(Sequence(0f, 0.25f, 0f));
        var frame = new VulcanRocksSim.Frame { X = 1f, Y = 2f, Z = 3f, XX = 1f, YY = 1f, ZZ = 1f };
        sim.Step(0f, 0f, frame);
        VulcanRocksSim.Rock rock = sim.Rocks[0];
        Assert.Equal(1f, rock.X);
        Assert.Equal(3f, rock.Z);
        double p = Bits(0x3fa0d97c);
        Assert.Equal(18 * Math.Sin(p), rock.VY, 4);
        Assert.Equal(18 * Math.Cos(p), rock.VZ, 4);
        Assert.Equal(0f, rock.VX, 4);
    }

    [Fact]
    public void Throws_Follow256PerSecond_UpToTheListSize()
    {
        VulcanRocksSim sim = Make(() => 0f); // every pick is slot 4
        sim.Step(0f, 0f, Identity);
        sim.Step(0.1f, 0.1f, Identity); // throws while the counter <= 25.6
        Assert.Equal(26f, sim.Thrown);
        Assert.Equal(23, sim.Rocks.Count);
    }

    [Fact]
    public void UnderTheGround_ARockIsMirroredAndHalved()
    {
        VulcanRocksSim sim = Make(Sequence(0f));
        sim.Step(0f, 0f, Identity);
        VulcanRocksSim.Rock rock = sim.Rocks[0];
        rock.X = 0f; rock.Y = 0.05f; rock.Z = 0f;
        rock.VX = 1f; rock.VY = -2f; rock.VZ = 0f;

        sim.Step(0.1f, 0.1f, Identity);
        // v.y = -2 - 0.98, y = 0.05 - 0.298 < 0: back on the ground, v mirrored and halved.
        Assert.Equal(0f, rock.Y);
        Assert.Equal(0.1f, rock.X, 5);
        Assert.Equal(0.5f, rock.VX, 5);
        Assert.Equal(1.49f, rock.VY, 4);
        Assert.Equal(1, rock.Bounces);
    }

    [Fact]
    public void AfterFourBounces_ARockStops_AndCountsEveryCallItRests()
    {
        VulcanRocksSim sim = Make(Sequence(0f));
        sim.Step(0f, 0f, Identity);
        VulcanRocksSim.Rock rock = sim.Rocks[0];
        rock.Y = 0f;
        rock.VX = 3f; rock.VY = -3f; rock.VZ = 0f;
        rock.Bounces = 4;

        sim.Step(1f / 30f, 1f / 30f, Identity);
        Assert.Equal(0f, rock.VX);
        Assert.Equal(0f, rock.VY);
        Assert.Equal(0f, rock.Spin);
        Assert.Equal(1, sim.Settled);

        // Resting, it sinks under gravity and stops again.
        sim.Step(2f / 30f, 1f / 30f, Identity);
        Assert.Equal(2, sim.Settled);
    }

    [Fact]
    public void ASlowBounce_Stops()
    {
        VulcanRocksSim sim = Make(Sequence(0f));
        sim.Step(0f, 0f, Identity);
        VulcanRocksSim.Rock rock = sim.Rocks[0];
        rock.Y = 0f;
        rock.VX = rock.VY = rock.VZ = 0f;
        // 1/60 s: v.y = -0.163, halved back up to 0.082 < 0.1.
        sim.Step(1f / 60f, 1f / 60f, Identity);
        Assert.Equal(0, rock.Bounces);
        Assert.Equal(1, sim.Settled);
    }

    [Fact]
    public void ReadyOnceTheSettleCounterReachesField20()
    {
        VulcanRocksSim sim = Make(Sequence(0f));
        sim.Step(0f, 0f, Identity);
        VulcanRocksSim.Rock rock = sim.Rocks[0];
        rock.VX = rock.VY = rock.VZ = 0f;
        rock.Bounces = 4;

        // One resting rock, one settle a call; age 0 keeps the throws off.
        int calls = 0;
        while (sim.Step(0f, 1f / 30f, Identity))
            calls++;
        Assert.Equal(22, calls);
        Assert.Equal(23, sim.Settled);
    }

    [Fact]
    public void TheFieldAfterTheModels_StopsSettledRocksCounting()
    {
        float[] f = Record45060();
        Array.Resize(ref f, 31);
        f[30] = Bits(1);
        var sim = new VulcanRocksSim(f, Sequence(0f), slot => true, FlatGround);
        Assert.True(sim.DeleteRocks);
        sim.Step(0f, 0f, Identity);
        sim.Rocks[0].Bounces = 4;
        sim.Step(0f, 1f / 30f, Identity);
        Assert.Equal(0, sim.Settled);
    }
}
