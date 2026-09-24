using System;
using Xunit;

/// <summary>
/// Locks <see cref="VolGridSim"/> to stock GfxControlVolGrid_t (Process <c>10116292</c>) and
/// GfxVisualVolGrid (build <c>1002f86d</c>) with 72360, the light pillar of 269540 Blessing of the Eternal
/// Craftsman.
/// </summary>
public class VolGridSimTests
{
    static float Bits(uint i) => BitConverter.Int32BitsToSingle(unchecked((int)i));

    static float[] Record72360()
    {
        var f = new float[54];
        f[0] = Bits(0x203);
        f[8] = 3f;
        f[9] = Bits(100);
        f[11] = Bits(4);
        f[12] = 1f;
        f[14] = Bits(20);
        f[15] = Bits(1);
        f[16] = Bits(20);
        int i = 17;
        void Floats(params float[] keys) { f[i++] = Bits((uint)(keys.Length / 2)); foreach (float k in keys) f[i++] = k; }
        void Colours(params (float t, uint c)[] keys) { f[i++] = Bits((uint)keys.Length); foreach (var k in keys) { f[i++] = k.t; f[i++] = Bits(k.c); } }
        Floats(0f, 0f, 0.1f, 80f, 1f, 20f);
        Floats(0f, 3.6f, 0.1f, 3.6f, 1f, 3.6f);
        Floats(0f, 0f, 0.1f, 3.6f, 1f, 3.6f);
        Colours((0f, 0xffu), (0.1f, 0xff8080ffu), (1f, 0u), (1f, 0u));
        Colours((0f, 0u), (0.1f, 0x8080ffu), (1f, 0u));
        Assert.Equal(54, i);
        return f;
    }

    [Fact]
    public void Loader_ReadsTheSlicesAndTheCurves()
    {
        var sim = new VolGridSim(Record72360());
        Assert.Equal(20, sim.SlicesX);
        Assert.Equal(1, sim.SlicesY);
        Assert.Equal(20, sim.SlicesZ);
        Assert.Equal(100, sim.Material);
        Assert.Equal(3f, sim.Duration);
    }

    [Fact]
    public void Step_FollowsTheCurves_UntilTheDuration()
    {
        var sim = new VolGridSim(Record72360());
        Assert.True(sim.Step(1.65f)); // t = 0.55: half-way from 80 to 20
        Assert.Equal(50f, sim.Height, 3);
        Assert.Equal(3.6f, sim.BottomSize, 5);
        Assert.Equal(3.6f, sim.TopSize, 5);
        Assert.Equal(StockColorCurve.Interpolate(0xff8080ffu, 0u, 0.5f), sim.BottomColour);
        Assert.False(sim.Step(3f));
    }

    [Fact]
    public void Build_LaysOutTheXSlices_WithTheirColours()
    {
        var sim = new VolGridSim(Record72360());
        sim.Step(1.65f);
        int n = sim.SliceCount * VolGridSim.VerticesPerSlice;
        Assert.Equal(205, n);
        var p = new float[n * 3];
        var c = new uint[n];
        var uv = new float[n * 2];
        sim.Build(p, c, uv);

        // x slice 1 of 20: x = 0.05 * 3.6 - 1.8.
        int v = 5;
        Assert.Equal(-1.62f, p[v * 3], 5);
        Assert.Equal(25f, p[v * 3 + 1], 3); // centre at half the height
        Assert.Equal(1.8f, p[(v + 1) * 3 + 2], 5);
        Assert.Equal(0f, p[(v + 1) * 3 + 1]);
        Assert.Equal(50f, p[(v + 3) * 3 + 1], 3);
        Assert.Equal(sim.BottomColour, c[v + 1]);
        Assert.Equal(sim.TopColour, c[v + 3]);
        Assert.Equal(StockColorCurve.Interpolate(sim.BottomColour, sim.TopColour, 0.5f), c[v]);
        // No 0x400: the slice samples the texture's row at its place.
        Assert.Equal(0.05f, uv[(v + 2) * 2 + 1], 6);
        Assert.Equal(1f, uv[(v + 2) * 2]);
    }

    [Fact]
    public void Build_PutsTheZSlicesNext_AndTheFloorLast()
    {
        var sim = new VolGridSim(Record72360());
        sim.Step(1.65f);
        int n = sim.SliceCount * VolGridSim.VerticesPerSlice;
        var p = new float[n * 3];
        var c = new uint[n];
        var uv = new float[n * 2];
        sim.Build(p, c, uv);

        int z0 = 20 * 5; // the first z slice, at z = -1.8
        Assert.Equal(-1.8f, p[z0 * 3 + 2], 5);
        Assert.Equal(0f, p[z0 * 3]);
        Assert.Equal(-1.8f, p[(z0 + 1) * 3], 5);
        Assert.Equal(1.8f, p[(z0 + 4) * 3], 5);

        int floor = 40 * 5; // y slice 0 of 1: the square at y = 0 in the bottom colour, the whole texture
        Assert.Equal(0f, p[floor * 3 + 1]);
        Assert.Equal(sim.BottomColour, c[floor + 2]);
        Assert.Equal(1f, uv[(floor + 3) * 2]);
        Assert.Equal(1f, uv[(floor + 3) * 2 + 1]);
    }

    [Fact]
    public void EdgeFade_ScalesAlphaByTheViewAngle()
    {
        var sim = new VolGridSim(Record72360());
        sim.Step(1.65f);
        int n = sim.SliceCount * VolGridSim.VerticesPerSlice;
        var p = new float[n * 3];
        var c = new uint[n];
        var uv = new float[n * 2];
        sim.Build(p, c, uv);
        uint before = c[1];

        // An x slice seen along z is edge-on.
        VolGridSim.EdgeFade(p, c, 0, 0f, 0f, 1f);
        Assert.Equal(0u, c[1] >> 24);
        Assert.Equal(before & 0xffffff, c[1] & 0xffffff);

        // Face-on, stock's normal is the cross of the two unit edges and isn't normalised: for this tall
        // thin slice |n| = sin of the angle between them, 2 * 25 * 1.8 / (25^2 + 1.8^2) = 0.143.
        sim.Build(p, c, uv);
        VolGridSim.EdgeFade(p, c, 0, 1f, 0f, 0f);
        Assert.Equal(127u, before >> 24);
        Assert.Equal(18u, c[1] >> 24);
    }
}
