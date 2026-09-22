using System;
using Xunit;

/// <summary>Locks <see cref="PlasmaSim"/> to stock _GfxControlPlasma_t + GfxVisualPlasma (17600).</summary>
public class PlasmaSimTests
{
    static float Bits(int v) => BitConverter.Int32BitsToSingle(v);

    /// <summary>17600, nano 28638's tracer, as stored in gfxtweak.bin.</summary>
    static float[] Fields17600() => new[]
    {
        Bits(5), 0f, 0.25f, 0f, 0f, 0f, 0f, Bits(0), -1f, Bits(8),
        1f, 0.3f, 0.3f, 1f, 1f, 0.3f, 0.3f, 1f, 1f,
    };

    [Fact]
    public void DurationIsField18_AndColourPacksWithTruncation()
    {
        var s = new PlasmaSim(Fields17600());
        Assert.Equal(1f, s.Duration);
        // A 1 -> 255, R/G .3 -> _ftol(76.5) = 76, B 1 -> 255.
        Assert.Equal(0xFF4C4CFFu, s.ColorAt(0f));
    }

    [Fact]
    public void Colour_RampsFromStartToEndOverTheDuration()
    {
        float[] f = Fields17600();
        f[14] = 0f; // end alpha 0
        var s = new PlasmaSim(f);
        Assert.Equal(0x7Fu, s.ColorAt(0.5f) >> 24); // _ftol(0.5 * 255)
    }

    static (float[] P, float[] Uv) Build(PlasmaSim s, float time, int rand, float ex = 7.5f)
    {
        var p = new float[PlasmaSim.VertexCount * 3];
        var uv = new float[PlasmaSim.VertexCount * 2];
        // Camera point behind the strip on -z: the strip faces it, side along y.
        s.BuildStrip(time, ex, 0f, 0f, 0f, 0f, -5f, () => rand, p, uv);
        return (p, uv);
    }

    [Fact]
    public void Strip_Runs76PairsFromStartToEnd_0p2Wide()
    {
        var (p, uv) = Build(new PlasmaSim(Fields17600()), 0f, 0x40);

        // First pair at the start, time 0: every sine is 0.
        Assert.Equal(new[] { 0f, -0.1f, 0f }, new[] { p[0], p[1], p[2] });
        Assert.Equal(new[] { 0f, 0.1f, 0f }, new[] { p[3], p[4], p[5] });
        Assert.Equal(new[] { 0f, 0f, 1f, 1f }, new[] { uv[0], uv[1], uv[2], uv[3] });

        int last = (PlasmaSim.VertexCount - 2) * 3;
        Assert.Equal(7.5f, p[last], 3);
        for (int i = 0; i < PlasmaSim.VertexCount; i += 2)
            Assert.Equal(0.2f, p[(i + 1) * 3 + 1] - p[i * 3 + 1], 4);
    }

    [Fact]
    public void Offset_IsTheSumOfFourCubedSines()
    {
        var (p, uv) = Build(new PlasmaSim(Fields17600()), 0.3f, 0x40);

        // Pair 10: along = 10 * 0.1 = 1.
        double[] a = { 0.15, 0.3, 0.2, 0.1 }, w = { 0.2, 0.3, 0.4, 0.5 }, v = { -0.8, 1, 1.2, 0.7 };
        double acc = 0;
        for (int k = 0; k < 4; k++)
            acc += Math.Pow(Math.Sin((v[k] * 0.3 + 1.0) * (6.28000020980835 / w[k])), 3) * a[k];

        float centreY = (p[20 * 3 + 1] + p[21 * 3 + 1]) * 0.5f;
        Assert.Equal(-0.1 * acc, centreY, 4); // side is (0, -0.1, 0)
        Assert.Equal(acc * 0.1, uv[20 * 2], 4);
        Assert.Equal(1 - acc * 0.1, uv[21 * 2 + 1], 4);
    }

    [Theory]
    [InlineData(0x0004, 0, 0.5f)]
    [InlineData(0x0006, 2, 0.5f)]
    [InlineData(0x0003, 0, 0f)]   // (r & ~3) == 0 adds nothing
    [InlineData(0x0044, 0, 0f)]   // a bit of 0x7c0 set: no nudge
    public void Rand_NudgesAPhase(int rand, int wave, float expected)
    {
        var s = new PlasmaSim(Fields17600());
        Build(s, 0f, rand);
        Assert.Equal(expected, s.Phase(wave));
    }

    [Fact]
    public void ZeroExtent_StepsAlongX()
    {
        var (p, _) = Build(new PlasmaSim(Fields17600()), 0f, 0x40, ex: 0f);
        int last = (PlasmaSim.VertexCount - 2) * 3;
        Assert.Equal(0.75f, p[last], 3); // 75 steps of 0.01
    }
}
