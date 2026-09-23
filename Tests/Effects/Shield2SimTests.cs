using System;
using Xunit;

/// <summary>
/// Locks <see cref="Shield2Sim"/> to stock GfxControlShield2_t (loader <c>1011148b</c>, Process
/// <c>10111110</c>, slot 6 <c>10111071</c>) and GfxVisualShield2's transform (<c>1001d569</c>) and
/// draw (<c>1001d3ee</c>), using records 72274 and 71320.
/// </summary>
public class Shield2SimTests
{
    static float Bits(int i) => BitConverter.Int32BitsToSingle(i);

    static float Bits(uint i) => BitConverter.Int32BitsToSingle(unchecked((int)i));

    /// <summary>Record 72274 (the Brood shield), fields 0..35.</summary>
    static float[] Brood()
    {
        var f = new float[36];
        f[0] = Bits(0x61400);
        f[8] = 20f;
        f[9] = Bits(0);          // displacement mode
        f[10] = Bits(0);         // uv mode
        f[11] = Bits(2);         // colour mode
        f[12] = Bits(32);        // material
        f[13] = 2f; f[14] = 2f;  // u/v scale
        f[15] = 3f; f[16] = 1f;  // u/v scroll
        f[17] = 0f;
        f[18] = 10f;             // flicker rate
        f[19] = Bits(1);         // one layer
        f[20] = Bits(unchecked((int)0xffffffff));  // no mech
        f[21] = Bits(4);
        f[22] = 0f; f[23] = Bits(0x00000000u);
        f[24] = 0.4f; f[25] = Bits(0xffff3030u);
        f[26] = 0.6f; f[27] = Bits(0xffff3030u);
        f[28] = 1f; f[29] = Bits(0x00000000u);
        f[30] = Bits(2);
        f[31] = 0f; f[32] = 0.01f;
        f[33] = 1f; f[34] = 0.01f;
        f[35] = Bits(0);
        return f;
    }

    [Fact]
    public void TheLoader_ReadsBothCurvesAndTheTrailingId()
    {
        var sim = new Shield2Sim(Brood());
        Assert.Equal(20f, sim.Duration, 4);
        Assert.Equal(0, sim.DisplaceMode);
        Assert.Equal(0, sim.UvMode);
        Assert.Equal(2, sim.ColourMode);
        Assert.Equal(32, sim.Material);
        Assert.Equal(1, sim.Layers);
        Assert.Equal(4, sim.Colour.Count);
        Assert.Equal(2, sim.Fade.Count);
        Assert.Equal(0, sim.EndEffectId);
        Assert.False(sim.OnMechModel);
    }

    [Fact]
    public void TheFlags_PickAdditiveDropAndCull()
    {
        var sim = new Shield2Sim(Brood());
        Assert.True(sim.Additive);        // 0x400
        Assert.True(sim.DropsLayers);     // 0x1000
        Assert.False(sim.HostMaterial);   // no 0x10000
        Assert.Equal(0, sim.CullMode);    // neither 0x4000 nor 0x8000

        float[] f = Brood();
        f[0] = Bits(0x61400 | Shield2Sim.FlagCullBack);
        Assert.Equal(1, new Shield2Sim(f).CullMode);
        f[0] = Bits(0x61400 | Shield2Sim.FlagCullFront);
        Assert.Equal(2, new Shield2Sim(f).CullMode);
    }

    [Fact]
    public void AStep_SetsTAndBothCurves()
    {
        var sim = new Shield2Sim(Brood());
        Assert.True(sim.Step(10f));      // half of the 20 s duration
        Assert.Equal(0.5f, sim.T, 4);
        Assert.Equal(0.01f, sim.Alpha, 5);
        // The colour curve holds 0xffff3030 between 0.4 and 0.6.
        Assert.Equal(0xffff3030u, sim.Argb);
    }

    [Fact]
    public void TheDisplacement_IsTheFadeCurve_InModeZero()
    {
        var sim = new Shield2Sim(Brood());
        sim.Step(10f);
        // Mode 0 pushes every vertex out by alpha, whatever its position.
        Assert.Equal(0.01f, sim.Displacement(1f, 2f, 3f), 5);
        Assert.Equal(0.01f, sim.Displacement(-4f, 0f, 9f), 5);
    }

    [Fact]
    public void TheDisplacement_IsASquaredSine_InModeOne()
    {
        float[] f = Brood();
        f[9] = Bits(1);
        var sim = new Shield2Sim(f);
        sim.Step(10f);   // t = 0.5, alpha = 0.01
        // sin(2x + 3y + z + 30t)^2 * alpha
        double s = Math.Sin(2.0 * 1f + 3.0 * 2f + 3f + 0.5 * 30.0);
        Assert.Equal((float)(s * s * 0.01), sim.Displacement(1f, 2f, 3f), 6);
    }

    [Fact]
    public void TheLayers_RampScaleAndDrop()
    {
        float[] f = Brood();
        f[19] = Bits(4);
        var sim = new Shield2Sim(f);
        sim.Step(10f);   // alpha = 0.01
        Assert.Equal(0f, sim.LayerAmount(0), 6);
        Assert.Equal(0.0025f, sim.LayerAmount(1), 6);
        Assert.Equal(0.005f, sim.LayerAmount(2), 6);
        Assert.Equal(1f, sim.LayerScale(0), 6);
        Assert.Equal(1.0075f, sim.LayerScale(3), 6);
    }

    [Fact]
    public void TheUv_ScrollsTheSourceUvsInModeZero()
    {
        var sim = new Shield2Sim(Brood());
        sim.Step(10f);   // t = 0.5
        // (u + field15 * t) * field13, (v + field16 * t) * field14
        sim.Uv(0.25f, 0.5f, 0f, 0f, 0f, out float u, out float v);
        Assert.Equal((0.25f + 3f * 0.5f) * 2f, u, 4);
        Assert.Equal((0.5f + 1f * 0.5f) * 2f, v, 4);
    }

    [Fact]
    public void TheUv_IsCylindricalInModeOne()
    {
        float[] f = Brood();
        f[10] = Bits(1);
        var sim = new Shield2Sim(f);
        sim.Step(10f);
        sim.Uv(0f, 0f, 1f, 7f, 0f, out float u, out float v);
        // atan2(x, z) * field13 / 2pi + field15 * t
        Assert.Equal((float)(Math.Atan2(1f, 0f) * (2f / 6.2831854820251465) + 3f * 0.5), u, 4);
        // y * field14 + field16 * t
        Assert.Equal(7f * 2f + 1f * 0.5f, v, 4);
    }

    [Fact]
    public void TheFlicker_IsOneWithoutTheFlag()
    {
        var sim = new Shield2Sim(Brood());   // 0x61400 has no 0x800
        sim.Step(10f);
        Assert.Equal(1f, sim.Flicker(), 6);
    }

    [Fact]
    public void TheFlicker_RunsOffTAndFieldEighteen()
    {
        float[] f = Brood();
        f[0] = Bits(0x61400 | Shield2Sim.FlagFlicker);
        var sim = new Shield2Sim(f);
        sim.Step(10f);   // t = 0.5, field18 = 10 -> d = 50
        double d = 0.5 * 10.0 * 10.0;
        double a = Math.Cos(d * 1.100000023841858 * 2.5);
        double b = Math.Sin(d * 2.299999952316284) * a;
        double want = Math.Sin(d * 1.2000000476837158 * 1.5) * b * 0.10000000149011612 + 0.5;
        Assert.Equal((float)want, sim.Flicker(), 5);
    }

    [Fact]
    public void TheVertexScale_IsThreeSines_AndModeTwoAddsTheTimeRamp()
    {
        var sim = new Shield2Sim(Brood());   // colour mode 2
        sim.Step(10f);
        float got = sim.VertexScale(1f, 2f, 3f, 1f);

        double t = 0.5 * 10.0;
        double n1 = Math.Cos((4.0 * 1 + 2.5 * 2 + 3.0 * 3 + t * 1.100000023841858) * 2.5);
        double n2 = Math.Sin((2.5 * 1 + 3.0 * 2 + 4.0 * 3 + t) * 2.299999952316284) * n1;
        double m = Math.Sin((3.0 * 1 + 4.0 * 2 + 1.5 * 3 + t * 1.2000000476837158) * 1.5) * n2;
        Assert.Equal((float)(0.5 * 1.5 * m), got, 5);

        // Mode 1 is the same field without the t * 1.5.
        float[] f = Brood();
        f[11] = Bits(1);
        var one = new Shield2Sim(f);
        one.Step(10f);
        Assert.Equal((float)m, one.VertexScale(1f, 2f, 3f, 1f), 5);
    }

    [Fact]
    public void TheAlpha_ScalesTheCurveColourAndClamps()
    {
        var sim = new Shield2Sim(Brood());
        sim.Step(10f);           // colour 0xffff3030, alpha byte 0xff
        Assert.Equal(255, sim.ScaleAlpha(1f));
        Assert.Equal(127, sim.ScaleAlpha(0.5f));
        Assert.Equal(0, sim.ScaleAlpha(-3f));
        Assert.Equal(255, sim.ScaleAlpha(9f));
    }

    [Fact]
    public void ATerminate_OpensTwoSeconds_ThenTheControlIsReady()
    {
        var sim = new Shield2Sim(Brood());
        Assert.True(sim.Step(5f));
        sim.Terminate(5f);
        Assert.True(sim.Stopping);
        Assert.True(sim.Step(6.9f));     // inside the window
        Assert.False(sim.Step(7.1f));    // 1 < (age - 5) / 2
    }

    [Fact]
    public void TheControl_TerminatesItselfTwoSecondsBeforeTheDuration()
    {
        var sim = new Shield2Sim(Brood());   // duration 20
        Assert.False(sim.ShouldTerminate(17.9f));
        Assert.True(sim.ShouldTerminate(18.1f));
    }

    [Fact]
    public void AMechRecord_IsFlagged()
    {
        // 71320 puts the shell on EP03 mech model 0 instead of the host's body.
        var f = new float[40];
        f[0] = Bits(0x1c00);
        f[8] = 16f;
        f[19] = Bits(3);
        f[20] = Bits(0);
        f[21] = Bits(3);
        f[22] = 0f; f[23] = Bits(0x0090ddffu);
        f[24] = 0.85f; f[25] = Bits(0xaa90ddffu);
        f[26] = 1f; f[27] = Bits(0xff90ddffu);
        f[28] = Bits(9);
        var sim = new Shield2Sim(f);
        Assert.True(sim.OnMechModel);
        Assert.Equal(3, sim.Layers);
        Assert.Equal(3, sim.Colour.Count);
    }
}
