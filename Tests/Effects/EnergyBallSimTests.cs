using System;
using Xunit;

/// <summary>
/// Locks <see cref="EnergyBallSim"/> to stock GfxControlEnergyBall_t (loader <c>1010de89</c>, build
/// <c>1010dd3f</c>, Process <c>1010e039</c>) and GfxVisualEnergyBall's render (<c>10012264</c>, one
/// quad <c>10011e31</c>), with 71060 — the ball both Orbital Strike nanos use.
/// </summary>
public class EnergyBallSimTests
{
    static float Bits(int i) => BitConverter.Int32BitsToSingle(i);

    /// <summary>71060: rise 1 s, fall 1 s, 4 blades a fan, 0 -> 65 -> 0 radius.</summary>
    static float[] Record71060()
    {
        var f = new float[27];
        f[0] = Bits(0x0f03);
        f[8] = -1f;
        f[9] = Bits(8);
        f[10] = 1f; f[11] = 1f;
        f[12] = Bits(4);
        f[13] = 1f; f[14] = 50f;
        f[15] = 0f; f[16] = 65f; f[17] = 0f;
        f[18] = Bits(0x00888888);
        f[19] = Bits(0x00ffffff);
        f[20] = Bits(unchecked((int)0x80ffffff));
        f[21] = Bits(unchecked((int)0x80ffffff));
        f[22] = Bits(0);
        f[23] = 0.2f;
        f[24] = 4f;
        return f;
    }

    [Fact]
    public void ReadsTheRecordTheWayTheLoaderDoes()
    {
        var sim = new EnergyBallSim(Record71060());
        Assert.Equal(8, sim.Material);
        Assert.Equal(4, sim.Blades);
        Assert.Equal(1f, sim.RiseSeconds, 4);
        Assert.Equal(1f, sim.FallSeconds, 4);
        Assert.Equal(2f, sim.Life, 4);
        Assert.Equal(65f, sim.PeakRadius, 4);
        Assert.Equal(0.2f, sim.Wander, 4);
        Assert.Equal(4f, sim.RadiusPulse, 4);
        // Every shipped record has flags 0x0f03, so every ball is additive and UV-swapped.
        Assert.True(sim.Additive);
        Assert.True(sim.SwapUv);
        Assert.False(sim.GroundSnap);
        // 10012264 walks three fans of N blades.
        Assert.Equal(12, sim.QuadCount);
        // The colour fields are packed ints, not floats — 0x00888888 is a signalling NaN pattern's
        // neighbour, and the whole set must survive as raw bits (see GfxBits).
        Assert.Equal(0x00888888u, sim.TopFrom);
        Assert.Equal(0x80ffffffu, sim.TopTo);
    }

    [Fact]
    public void TheLifeIsTheTwoHalves_NotFieldEight()
    {
        var sim = new EnergyBallSim(Record71060());
        Assert.Equal(-1f, sim.Duration, 4);   // field 8, which stock never uses here
        Assert.Equal(2f, sim.Life, 4);

        sim.Advance(1.99f);
        Assert.False(sim.Finished);
        sim.Advance(2f);
        Assert.False(sim.Finished);           // 1010e067 finishes only once past the end
        sim.Advance(2.01f);
        Assert.True(sim.Finished);
    }

    [Fact]
    public void TheRadiusClimbsToThePeakAtTheJoin_ThenBack()
    {
        var sim = new EnergyBallSim(Record71060());
        // Field 24 = 4 pulses the radius by cos(2*pi*u), so compare against that, not the bare ramp.
        sim.Advance(0f);
        Assert.Equal(0f + 4f, sim.Radius, 3);          // f = 0 -> start radius 0, pulse cos(0) = 1

        sim.Advance(1f);                                // the join: f = 1 either side
        Assert.Equal(65f + 4f * (float)Math.Cos(Math.PI), sim.Radius, 2);

        sim.Advance(2f);                                // f back to 0 -> end radius 0
        Assert.Equal(0f + 4f * (float)Math.Cos(2.0 * Math.PI), sim.Radius, 2);
    }

    [Fact]
    public void TheEaseModesAreStocksThree()
    {
        float[] rec = Record71060();

        var soft = new EnergyBallSim(rec);               // mode 0
        Assert.Equal(1f - (float)Math.Pow(1f - 0.5f, 6), soft.RiseEase(0.5f), 5);
        Assert.Equal(1f - (float)Math.Pow(0.5f, 6), soft.FallEase(0.5f), 5);

        rec[22] = Bits(1);
        var hard = new EnergyBallSim(rec);
        Assert.Equal((float)Math.Pow(0.5f, 6), hard.RiseEase(0.5f), 5);
        Assert.Equal((float)Math.Pow(0.5f, 6), hard.FallEase(0.5f), 5);

        rec[22] = Bits(2);
        var linear = new EnergyBallSim(rec);
        Assert.Equal(0.25f, linear.RiseEase(0.25f), 5);
        Assert.Equal(0.75f, linear.FallEase(0.25f), 5);

        // All three leave the two halves meeting at 1, which is what keeps the radius continuous.
        foreach (int mode in new[] { 0, 1, 2 })
        {
            rec[22] = Bits(mode);
            var s = new EnergyBallSim(rec);
            Assert.Equal(1f, s.RiseEase(1f), 5);
            Assert.Equal(1f, s.FallEase(0f), 5);
        }
    }

    [Fact]
    public void TheColourRampRunsBothWays_AndIsDoneOnBytes()
    {
        var sim = new EnergyBallSim(Record71060());
        sim.Advance(0f);
        Assert.Equal(0x00888888u, sim.TopColour);
        Assert.Equal(0x00ffffffu, sim.BottomColour);

        sim.Advance(1f);                                 // the join, f = 1
        Assert.Equal(0x80ffffffu, sim.TopColour);
        Assert.Equal(0x80ffffffu, sim.BottomColour);

        sim.Advance(2f);                                 // the fall ends back where it began
        Assert.Equal(0x00888888u, sim.TopColour);

        // 10019c93 rounds each byte by adding a half and truncating, then 10019bb1 clamps the sum.
        Assert.Equal(0x80808080u, EnergyBallSim.Lerp(0x00000000u, 0xffffffffu, 0.5f));
        Assert.Equal(0xffffffffu, EnergyBallSim.Lerp(0xffffffffu, 0xffffffffu, 1f));
        // A whole-channel sum over 255 is clipped, not wrapped.
        Assert.Equal(0x00ffffffu, EnergyBallSim.Lerp(0x00ffffffu, 0x00ffffffu, 1f));
    }

    [Fact]
    public void FieldTwentyFiveShapesBothTheClimbAndTheEnvelope()
    {
        float[] rec = Record71060();

        // 0: the ball stays put and the envelope is a flat 1 (this is 71060).
        var flat = new EnergyBallSim(rec);
        flat.Advance(1f);
        Assert.Equal(0f, flat.Climbed, 5);

        // Positive: it starts high and settles, with the envelope fading the whole ball in.
        rec[25] = 10f;
        var down = new EnergyBallSim(rec);
        down.Advance(0f);
        Assert.Equal(10f, down.Climbed, 4);
        Assert.Equal(0f, down.Radius, 4);                // envelope u = 0 kills it at the start
        down.Advance(2f);
        Assert.Equal(0f, down.Climbed, 3);

        // Negative (71065's -800): it climbs away, and the envelope fades the ball out instead.
        rec[25] = -800f;
        var up = new EnergyBallSim(rec);
        up.Advance(0f);
        Assert.Equal(0f, up.Climbed, 3);
        up.Advance(2f);
        Assert.Equal(800f, up.Climbed, 2);
        Assert.Equal(0f, up.Radius, 4);                  // envelope 1 - u = 0 at the end
    }

    [Fact]
    public void TheThreeFansAreOrthogonal_AndEachBladeIsAFlatSquare()
    {
        var sim = new EnergyBallSim(Record71060());

        // Blade 0 of each fan is unrotated, so the bases are the plain axes.
        sim.Blade(0, out float axX, out float axY, out float axZ, out float angle,
            out float ax, out float ay, out float az, out float bx, out float by, out float bz);
        Assert.Equal(1f, axX, 5); Assert.Equal(0f, angle, 5);
        Assert.Equal(1f, ax, 5); Assert.Equal(1f, by, 5);

        sim.Blade(1, out axX, out axY, out axZ, out _, out ax, out ay, out az, out bx, out by, out bz);
        Assert.Equal(1f, axY, 5);
        Assert.Equal(1f, ax, 5);                         // the y fan keeps A on x

        // 10011e55: only the z fan swings A onto z.
        sim.Blade(2, out axX, out axY, out axZ, out _, out ax, out ay, out az, out bx, out by, out bz);
        Assert.Equal(1f, axZ, 5);
        Assert.Equal(1f, az, 5);
        Assert.Equal(0f, ax, 5);

        // Blade 1 of the x fan is a quarter of the way round: pi / 4 for N = 4.
        sim.Blade(3, out _, out _, out _, out angle, out ax, out ay, out az, out bx, out by, out bz);
        Assert.Equal((float)(Math.PI / 4.0), angle, 5);
        Assert.Equal(1f, ax, 5);                         // A is the axis, so it does not move
        Assert.Equal((float)Math.Cos(Math.PI / 4.0), by, 4);
        Assert.Equal((float)Math.Sin(Math.PI / 4.0), bz, 4);
    }

    [Fact]
    public void TheCornersAreTheStripOrderStockFeedsD3D()
    {
        // A = x, B = y, radius 2 -> the square in the xy plane, in 0 1 2 3 strip order.
        EnergyBallSim.Corner(0, 2f, 1f, 0f, 0f, 0f, 1f, 0f, out float x, out float y, out _);
        Assert.Equal(2f, x, 5); Assert.Equal(2f, y, 5);
        EnergyBallSim.Corner(1, 2f, 1f, 0f, 0f, 0f, 1f, 0f, out x, out y, out _);
        Assert.Equal(-2f, x, 5); Assert.Equal(2f, y, 5);
        EnergyBallSim.Corner(2, 2f, 1f, 0f, 0f, 0f, 1f, 0f, out x, out y, out _);
        Assert.Equal(2f, x, 5); Assert.Equal(-2f, y, 5);
        EnergyBallSim.Corner(3, 2f, 1f, 0f, 0f, 0f, 1f, 0f, out x, out y, out _);
        Assert.Equal(-2f, x, 5); Assert.Equal(-2f, y, 5);
    }

    [Fact]
    public void TheUvLayoutIsAQuarterTurn_NotAMirror()
    {
        // 10011ec9, flag clear.
        EnergyBallSim.Uv(0, false, out float u, out float v); Assert.Equal(0f, u, 5); Assert.Equal(0f, v, 5);
        EnergyBallSim.Uv(1, false, out u, out v); Assert.Equal(0f, u, 5); Assert.Equal(1f, v, 5);
        EnergyBallSim.Uv(2, false, out u, out v); Assert.Equal(1f, u, 5); Assert.Equal(0f, v, 5);
        EnergyBallSim.Uv(3, false, out u, out v); Assert.Equal(1f, u, 5); Assert.Equal(1f, v, 5);

        // Flag set — every shipped record takes this one.
        EnergyBallSim.Uv(0, true, out u, out v); Assert.Equal(0f, u, 5); Assert.Equal(1f, v, 5);
        EnergyBallSim.Uv(1, true, out u, out v); Assert.Equal(1f, u, 5); Assert.Equal(1f, v, 5);
        EnergyBallSim.Uv(2, true, out u, out v); Assert.Equal(0f, u, 5); Assert.Equal(0f, v, 5);
        EnergyBallSim.Uv(3, true, out u, out v); Assert.Equal(1f, u, 5); Assert.Equal(0f, v, 5);
    }

    [Fact]
    public void TheBallWandersAndSpinsOnceOverItsLife()
    {
        var sim = new EnergyBallSim(Record71060());

        sim.Advance(0f);
        // w = (sin 0, cos 0, sin 0) = (0, 1, 0), set to length 0.2.
        Assert.Equal(0f, sim.WanderX, 4);
        Assert.Equal(0.2f, sim.WanderY, 4);
        Assert.Equal(0f, sim.SpinRadians, 5);

        sim.Advance(2f);
        Assert.Equal((float)(2.0 * Math.PI), sim.SpinRadians, 4);
        // The axis is always unit length, whatever the three cosines do.
        float m = sim.AxisX * sim.AxisX + sim.AxisY * sim.AxisY + sim.AxisZ * sim.AxisZ;
        Assert.Equal(1f, m, 4);

        // 1007030f is a plain scale, not a set-length, so each axis is its own sine times field 23
        // and the reach breathes instead of holding at 0.2.
        for (float t = 0f; t <= 2f; t += 0.1f)
        {
            float u = t / 2f;
            sim.Advance(t);
            Assert.Equal((float)Math.Sin(8.0 * Math.PI * u) * 0.2f, sim.WanderX, 4);
            Assert.Equal((float)Math.Cos(6.0 * Math.PI * u) * 0.2f, sim.WanderY, 4);
            Assert.Equal((float)Math.Sin(2.0 * Math.PI * u) * 0.2f, sim.WanderZ, 4);
            float d = (float)Math.Sqrt(
                sim.WanderX * sim.WanderX + sim.WanderY * sim.WanderY + sim.WanderZ * sim.WanderZ);
            Assert.True(d <= 0.2f * (float)Math.Sqrt(3.0) + 1e-4f);
        }
    }
}
