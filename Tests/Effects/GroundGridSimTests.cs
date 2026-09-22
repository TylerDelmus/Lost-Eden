using System;
using Xunit;

/// <summary>
/// Locks <see cref="GroundGridSim"/> to stock GfxControlGroundGrid_t (Process <c>1010e704</c>) and
/// GfxVisualGroundGrid's Update (<c>100164b4</c>), with 71370: 200 x 200 at 1 m, fade in 0.5, out 2, 60 s.
/// </summary>
public class GroundGridSimTests
{
    static float Bits(int i) => BitConverter.Int32BitsToSingle(i);

    static float[] Record71370()
    {
        var f = new float[27];
        f[0] = Bits(0x30c03);
        f[8] = 60f;
        f[9] = Bits(95);
        f[10] = Bits(200);
        f[12] = 1f; f[13] = 1f;
        f[14] = 0.5f; f[15] = 2f;
        f[16] = Bits(-1);
        f[18] = 1f;
        f[19] = 0.25f;
        f[20] = 5f;
        return f;
    }

    [Fact]
    public void Alpha_FadesInThenHoldsThenFadesOut()
    {
        var sim = new GroundGridSim(Record71370());
        sim.Advance(0.25f, 0f);
        Assert.Equal(0.5f, sim.Alpha, 5);
        sim.Advance(10f, 0f);
        Assert.Equal(1f, sim.Alpha, 5);
        sim.Advance(59f, 0f);
        Assert.Equal(0.5f, sim.Alpha, 5);
    }

    [Fact]
    public void Grid_IsCentredOnTheEmitter_AndUvsSpanOnce()
    {
        var sim = new GroundGridSim(Record71370());
        Assert.Equal(99.5f, sim.HalfWidth, 4);
        sim.Point(0, 0, out float x0, out float z0);
        Assert.Equal(-99.5f, x0, 4);
        Assert.Equal(-99.5f, z0, 4);
        sim.Uv(199, 199, out float u, out float v);
        Assert.Equal(1f, u, 5);
        Assert.Equal(1f, v, 5);
    }

    [Fact]
    public void CentredUvFlag_ShiftsByHalfTheGrid()
    {
        float[] rec = Record71370();
        rec[0] = Bits(0x30c03 | GroundGridSim.FlagCentredUv);
        rec[10] = Bits(3);
        var sim = new GroundGridSim(rec);
        sim.Uv(0, 0, out float u, out _);
        Assert.Equal(-0.5f, u, 5);
    }
}
