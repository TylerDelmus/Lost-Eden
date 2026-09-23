using System;
using Xunit;

/// <summary>
/// Locks <see cref="GroundShakeSim"/> to stock GfxControlGroundShake_t (loader <c>1010f015</c>,
/// Process <c>1010f12a</c>), using records 71216 and 71046.
/// </summary>
public class GroundShakeSimTests
{
    static float Bits(int i) => BitConverter.Int32BitsToSingle(i);

    /// <summary>Record 71216: 2 s, range 50, a curve falling 0.5 to 0.</summary>
    static float[] Simple()
    {
        var f = new float[18];
        f[0] = Bits(3);
        f[8] = 2f;
        f[12] = 50f;
        f[13] = Bits(2);
        f[14] = 0f; f[15] = 0.5f;
        f[16] = 1f; f[17] = 0f;
        return f;
    }

    [Fact]
    public void TheLoader_ReadsDurationRangeAndTheCurve()
    {
        var sim = new GroundShakeSim(Simple(), () => 0.5f);
        Assert.Equal(3, sim.Flags);
        Assert.Equal(2f, sim.Duration, 4);
        Assert.Equal(50f, sim.Range, 4);
        Assert.Equal(2, sim.Curve.Count);
    }

    [Fact]
    public void ItExpiresAtTheDuration()
    {
        var sim = new GroundShakeSim(Simple(), () => 0.5f);
        Assert.False(sim.Expired(0f));
        Assert.False(sim.Expired(1.99f));
        Assert.True(sim.Expired(2f));
        Assert.True(sim.Expired(3f));
    }

    [Fact]
    public void TheAmount_IsTheCurveAtTheEpicentre()
    {
        var sim = new GroundShakeSim(Simple(), () => 0.5f);
        // t = 0 -> curve 0.5, distance 0 -> full strength.
        Assert.Equal(0.5f, sim.Amount(0f, 0f), 4);
        // t = 0.5 -> curve 0.25.
        Assert.Equal(0.25f, sim.Amount(1f, 0f), 4);
    }

    [Fact]
    public void TheAmount_FallsOffLinearlyToTheRange()
    {
        var sim = new GroundShakeSim(Simple(), () => 0.5f);
        // At t = 0 the curve is 0.5; halfway out that is halved again.
        Assert.Equal(0.5f, sim.Amount(0f, 0f), 4);
        Assert.Equal(0.25f, sim.Amount(0f, 25f), 4);
        Assert.Equal(0f, sim.Amount(0f, 50f), 4);
    }

    [Fact]
    public void ACameraPastTheRange_IsPinnedNotNegative()
    {
        var sim = new GroundShakeSim(Simple(), () => 0.5f);
        Assert.Equal(0f, sim.Amount(0f, 500f), 4);
        Assert.Equal(0f, sim.Amount(0f, 5000f), 4);
    }

    [Fact]
    public void AZeroRange_ShakesNothing()
    {
        float[] f = Simple();
        f[12] = 0f;
        var sim = new GroundShakeSim(f, () => 1f);
        Assert.Equal(0f, sim.Amount(0f, 0f), 4);
    }

    [Fact]
    public void TheOffset_IsThreeDrawsOfTwoRMinusOne()
    {
        var draws = new[] { 1f, 0f, 0.5f };
        int i = 0;
        var sim = new GroundShakeSim(Simple(), () => draws[i++]);
        sim.Offset(4f, out float x, out float y, out float z);
        Assert.Equal(4f, x, 4);    // (2*1 - 1) * 4
        Assert.Equal(-4f, y, 4);   // (2*0 - 1) * 4
        Assert.Equal(0f, z, 4);    // (2*0.5 - 1) * 4
    }

    [Fact]
    public void ARealRecord_RangeTwoHundred()
    {
        // 71046: 8 s, range 200, four curve keys.
        var f = new float[22];
        f[0] = Bits(3);
        f[8] = 8f;
        f[12] = 200f;
        f[13] = Bits(4);
        f[14] = 0f; f[15] = 0f;
        f[16] = 0.6f; f[17] = 0.3f;
        f[18] = 0.6f; f[19] = 1f;
        f[20] = 1f; f[21] = 0f;
        var sim = new GroundShakeSim(f, () => 0.5f);
        Assert.Equal(200f, sim.Range, 4);
        Assert.Equal(4, sim.Curve.Count);
        Assert.Equal(0f, sim.Amount(0f, 0f), 4);      // the curve starts at 0
        Assert.Equal(0f, sim.Amount(4f, 200f), 4);    // and the range still kills it
    }
}
