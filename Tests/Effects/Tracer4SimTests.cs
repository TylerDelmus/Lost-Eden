using System;
using Xunit;

/// <summary>
/// Locks <see cref="Tracer4Sim"/> and <see cref="Cord4Strip"/> to stock _GfxControlTracer4_t and
/// GfxVisualCord4, with 45502 (nano 266281's tracer).
/// </summary>
[Collection("Tracer4 statics")]
public class Tracer4SimTests
{
    static float Bits(int v) => BitConverter.Int32BitsToSingle(v);

    static float[] Fields45502() => new[]
    {
        Bits(2), 0f, 0f, 0f, 0f, 0f, 0f, Bits(0), 1f, Bits(15),
        100f, 0.1f, 0.85f, 1f, 0.2f, 0.3f, 0.8f, Bits(7), Bits(0), Bits(0),
    };

    static Tracer4Sim Along(float dx, float dy, float dz)
        => new Tracer4Sim(Fields45502(), 1f, 2f, 3f, 1f + dx, 2f + dy, 3f + dz);

    [Fact]
    public void Frame_HasLocalYAlongTheFlight_AndColourIsFistpPacked()
    {
        Tracer4Sim s = Along(0f, 0f, 4f);
        float[] b = s.Basis;
        Assert.Equal(new[] { 0f, 0f, 1f }, new[] { b[3], b[4], b[5] }); // row 1 = dir
        // A 1 -> 255, R .2 -> 51, G .3 -> FISTP(76.0) = 76, B .8 -> 204.
        Assert.Equal(0xFF334CCCu, s.Argb);
        Assert.Equal(0.1f, s.LinkSize);
    }

    [Fact]
    public void Links_SpiralAlongTheLine_WithABulgingRadius()
    {
        Tracer4Sim.ResetShared();
        Tracer4Sim s = Along(0f, 0f, 4f);
        s.Step(0f, () => 1, () => 0.5f); // rand() & 3 != 0: no flip, no jitter

        float[] l = s.LinkPositions(0);
        // Link 0: y 0, angle = phase 0, r = (sin 0 + 0.2) * 0.85 = 0.17.
        Assert.Equal(0.17f, l[0], 5);
        Assert.Equal(0f, l[1], 5);
        Assert.Equal(0f, l[2], 5);

        // Link 10: y 0.5, height 4 * 1.1 * 0.5, angle 10 steps of 0.314.
        double r = (Math.Sin(0.5 * Math.PI * 20 / 19) + 0.2) * 0.85;
        Assert.Equal(4 * 1.1 * 0.5, l[10 * 3 + 1], 3);
        Assert.Equal(Math.Cos(3.14) * r, l[10 * 3], 3);
        Assert.Equal(Math.Sin(3.14) * r, l[10 * 3 + 2], 3);
    }

    [Fact]
    public void SharedPhases_Spin_AndOneRandInFourFlipsTheTwist()
    {
        Tracer4Sim.ResetShared();
        Tracer4Sim s = Along(0f, 0f, 4f);
        s.Step(0.1f, () => 1, () => 0.5f);
        Assert.Equal(1.256f, Tracer4Sim.Phase(0), 4);
        Assert.Equal(0.314f, Tracer4Sim.TwistStep);

        s.Step(0f, () => 4, () => 0.5f); // & 3 == 0 for each of the three ribbons: three flips
        Assert.Equal(-0.314f, Tracer4Sim.TwistStep);
        Tracer4Sim.ResetShared();
    }

    [Fact]
    public void ShortSegment_ReadiesAtBuild()
    {
        Assert.True(Along(0.005f, 0f, 0f).Degenerate);
    }

    [Fact]
    public void Cord4_DropsTheOldestLink_AndSpansTwoVerticesPerLink()
    {
        const int n = 5;
        var local = new float[n * 3];
        var cam = new float[n * 3];
        var size = new float[n];
        var life = new float[n];
        for (int i = 0; i < n; i++)
        {
            local[i * 3] = i;        // along x
            cam[i * 3] = i;          // camera sees them left to right at depth 10
            cam[i * 3 + 2] = 10f;
            size[i] = 0.1f;
            life[i] = 0.9f;
        }

        var pos = new float[2 * (n - 1) * 3];
        var uv = new float[2 * (n - 1) * 2];
        int count = Cord4Strip.Build(n, local, cam, 1f, 0f, 0f, 0f, 1f, 0f, size, life, 1f, true, pos, uv);

        Assert.Equal(8, count); // links 0..3, two each
        // Screen direction +x: side = right * 0 - up * 1 = (0, -1, 0); times size 0.1.
        Assert.Equal(new[] { 0f, 0.1f, 0f }, new[] { pos[0], pos[1], pos[2] });   // P - s
        Assert.Equal(new[] { 0f, -0.1f, 0f }, new[] { pos[3], pos[4], pos[5] });  // P + s
        Assert.Equal(3f, pos[(count - 1) * 3], 5);                                // last is link 3
        Assert.Equal(0f, uv[0]);
        Assert.Equal(1f, uv[2]);
        Assert.Equal(0.1f, uv[1], 5); // v = 1 - life
    }
}
