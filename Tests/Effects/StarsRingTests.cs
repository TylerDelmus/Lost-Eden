using System;
using Xunit;

/// <summary>Locks <see cref="StarsRing"/> to stock Stars starTypes 7 (45001) and 8 (43719).</summary>
public class StarsRingTests
{
    static float Bits(int v) => BitConverter.Int32BitsToSingle(v);

    /// <summary>45001, nano 28638's hit: an orange ring expanding to 20 m over 1.9 s.</summary>
    static float[] Fields45001() => new[]
    {
        Bits(5), 0f, 0f, 0f, 0f, 0f, 0f, Bits(1000), -1f, Bits(8),
        Bits(7), 0f, 0.5f, 4f, 3f, 1f, 3f, 3f, 1f, 0.99f,
        0.54f, 0f, 0.2f, 1f, 0.3f, 0.3f, 1.9f, 0f, 3.5f, 1.8f,
        Bits(2000), Bits(1),
    };

    /// <summary>43719, nano 28608's hit: starType 8.</summary>
    static float[] Fields43719() => new[]
    {
        Bits(5), 0f, 0f, 0f, 0f, 0f, 0f, Bits(1000), -1f, Bits(8),
        Bits(8), 0f, 0.5f, 4f, 3f, 1f, 3f, 3f, 0.5f, 0.99f,
        0.54f, 0f, 0.2f, 0.99f, 0.54f, 0f, 0.6f, 0f, 0.6f, 0.35f,
        Bits(160), Bits(1),
    };

    [Fact]
    public void Type7_IsAFlatRingOf128()
    {
        var r = new StarsRing(7, Fields45001());
        float[] d = r.Directions;
        Assert.Equal(0f, d[0], 5);  // j = 0: (sin 0, 0, cos 0)
        Assert.Equal(1f, d[2], 5);
        Assert.Equal(1f, d[32 * 3], 4); // a quarter of the way round: +x
        for (int j = 0; j < StarsRing.Count; j++)
        {
            Assert.Equal(0f, d[j * 3 + 1]);
            Assert.Equal(1f, d[j * 3] * d[j * 3] + d[j * 3 + 2] * d[j * 3 + 2], 4);
        }
    }

    [Fact]
    public void Type8_SpiralsOverASphere()
    {
        var r = new StarsRing(8, Fields43719());
        float[] d = r.Directions;
        Assert.Equal(new[] { 0f, 1f, 0f }, new[] { d[0], d[1], d[2] }); // t = 0: the pole
        for (int j = 0; j < StarsRing.Count; j++)
            Assert.Equal(1f, d[j * 3] * d[j * 3] + d[j * 3 + 1] * d[j * 3 + 1] + d[j * 3 + 2] * d[j * 3 + 2], 4);
    }

    [Fact]
    public void Pulse_GrowsWithTheSquareRootOfTheFraction()
    {
        var r = new StarsRing(7, Fields45001());

        r.Step(0f, 1.9f);
        Assert.Equal(0f, r.Radius);
        Assert.Equal(1.8f, r.Size, 5);
        Assert.Equal(0xFFFC8900u, r.Argb); // A 1, R .99 -> 252, G .54 -> 137, B 0

        r.Step(0.475f, 1.9f); // frac 0.25, sqrt 0.5
        Assert.Equal(0.25f, r.Frac, 5);
        Assert.Equal(10f, r.Radius, 4);          // 2000 * 0.01 * 0.5
        Assert.Equal(3.55f, r.Size, 4);          // 3.5 * 0.5 + 1.8
        Assert.Equal(0xCBu, r.Argb >> 24);       // alpha 1 -> 0.2 at a quarter: _ftol(0.79999 * 255) = 203
    }

    [Fact]
    public void Repeats_WrapTheFraction()
    {
        float[] f = Fields45001();
        f[31] = Bits(2);
        var r = new StarsRing(7, f);
        r.Step(1.425f, 1.9f); // u = 1.5
        Assert.Equal(0.5f, r.Frac, 4);
    }
}
