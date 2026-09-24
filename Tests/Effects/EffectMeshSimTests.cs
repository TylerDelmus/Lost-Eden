using System;
using Xunit;

/// <summary>
/// Locks <see cref="EffectMeshSim"/> and <see cref="StockFloatCurve"/> to stock GfxControlEffectMesh_t
/// (loader <c>1010caf8</c>, Process <c>1010ce9c</c>) and the float curve at <c>1011689d</c>, with 71028
/// (71016's blast wave): model 2, scale 1 + 120/s - 120/s², rendering effect 4, alpha 0 -> 1 at 0.1 -> 0.
/// </summary>
public class EffectMeshSimTests
{
    static float Bits(int i) => BitConverter.Int32BitsToSingle(i);

    static float[] Record71028()
    {
        var f = new float[39];
        f[0] = Bits(3);
        f[8] = 1f;
        f[10] = Bits(2);
        f[11] = 0.1f; f[12] = 0.9f;
        f[23] = 1f;
        f[28] = 1f; f[29] = 120f; f[30] = -120f;
        f[31] = Bits(4);
        f[32] = Bits(3);
        f[33] = 0f; f[34] = 0f;
        f[35] = 0.1f; f[36] = 1f;
        f[37] = 1f; f[38] = 0f;
        return f;
    }

    [Fact]
    public void Loader_ReadsModelEffectAndAxis()
    {
        var sim = new EffectMeshSim(Record71028());
        Assert.Equal("EP03_blast_wave_effect.abiff", EffectMeshSim.ModelName(sim.ModelIndex));
        Assert.Equal(4, sim.RenderingEffect);
        Assert.Equal(0f, sim.AxisX);
        Assert.Equal(1f, sim.AxisY);
        Assert.Equal(3, sim.Curve.Count);
        Assert.Null(EffectMeshSim.ModelName(28));
    }

    [Fact]
    public void ZeroAxis_IsUp()
    {
        float[] f = Record71028();
        f[23] = 0f;
        var sim = new EffectMeshSim(f);
        Assert.Equal(1f, sim.AxisY);
    }

    [Fact]
    public void Scale_IsEuler_ValueBeforeRate()
    {
        var sim = new EffectMeshSim(Record71028());
        sim.Step(0f, 0f);
        const float dt = 1f / 30f;
        for (int n = 1; n <= 30; n++)
            sim.Step(dt, n * dt);
        // 1 + 120 n dt - 120 dt² n (n - 1) / 2 at n = 30.
        Assert.Equal(63f, sim.Scale, 2);
    }

    [Fact]
    public void Alpha_FollowsTheCurveOverTimeOverDuration()
    {
        var sim = new EffectMeshSim(Record71028());
        sim.Step(0f, 0f);
        Assert.Equal(0f, sim.Alpha, 5);
        sim.Step(0.05f, 0.05f);
        Assert.Equal(0.5f, sim.Alpha, 5);
        sim.Step(0.5f, 0.55f);
        Assert.Equal(0.5f, sim.Alpha, 5);
        sim.Step(0.5f, 1.05f);
        Assert.Equal(0f, sim.Alpha, 5);
    }

    [Fact]
    public void NegativeDuration_HoldsFullAlpha()
    {
        float[] f = Record71028();
        f[8] = -1f;
        var sim = new EffectMeshSim(f);
        sim.Step(0.1f, 0.1f);
        Assert.Equal(1f, sim.Alpha);
    }

    [Fact]
    public void Motion1_BobsWithTheAgeSine_AndSpins()
    {
        float[] f = Record71028();
        f[8] = 8f;
        f[9] = Bits(1);
        f[17] = 1.5f;
        f[26] = 1f;
        var sim = new EffectMeshSim(f);
        sim.Step(0.1f, 2f);
        // sin(2 pi * 2 / 8) = 1.
        Assert.Equal(0.15f, sim.OffsetY, 5);
        Assert.Equal(0.1f, sim.Angle, 5);
    }

    [Fact]
    public void Motion0_MovesThenAccelerates()
    {
        float[] f = Record71028();
        f[16] = 2f;
        f[19] = 10f;
        var sim = new EffectMeshSim(f);
        sim.Step(0.5f, 0.5f);
        Assert.Equal(1f, sim.OffsetX, 5);
        sim.Step(0.5f, 1f);
        Assert.Equal(1f + 7f * 0.5f, sim.OffsetX, 5);
    }

    [Fact]
    public void DistanceFade_ZeroUnder5_RampTo10_ThenCurve()
    {
        Assert.Equal(0f, EffectMeshSim.DistanceAlpha(4f, 0.8f));
        Assert.Equal(0.25f, EffectMeshSim.DistanceAlpha(7.5f, 0.8f), 5);
        Assert.Equal(0.8f, EffectMeshSim.DistanceAlpha(12f, 0.8f));
        Assert.Equal(0f, EffectMeshSim.DistanceAlpha(0f, 0.8f));
    }

    [Fact]
    public void FloatCurve_OneKeyIsItsValue_NoKeysIsOne()
    {
        Assert.Equal(1f, new StockFloatCurve(null, null).Evaluate(0.5f));
        Assert.Equal(0.3f, new StockFloatCurve(new[] { 0f }, new[] { 0.3f }).Evaluate(5f));
        var curve = new StockFloatCurve(new[] { 0.5f, 1f }, new[] { 1f, 0f });
        Assert.Equal(0f, curve.Evaluate(0.2f)); // before the first key: the last key
    }

    /// <summary>72345 (a vehicle buff's hoverbike circle): flags 0x11203, model 21, effect 6.</summary>
    static float[] Record72345()
    {
        var f = new float[39];
        f[0] = Bits(0x11203);
        f[6] = 3.14159274f;
        f[7] = Bits(2006);
        f[8] = -1f;
        f[10] = Bits(21);
        f[22] = 1f;
        f[28] = 1f;
        f[31] = Bits(6);
        f[32] = Bits(3);
        f[35] = 0.1f; f[36] = 1f;
        f[37] = 1f;
        return f;
    }

    /// <summary>72452 (a vehicle buff's hoverboard fan): flags 0x8203, model 23, spin 10.</summary>
    static float[] Record72452()
    {
        var f = new float[35];
        f[0] = Bits(0x8203);
        f[7] = Bits(2006);
        f[8] = -1f;
        f[10] = Bits(23);
        f[23] = 1f;
        f[26] = 10f;
        f[28] = 1f;
        f[32] = Bits(1);
        f[34] = 1f;
        return f;
    }

    [Fact]
    public void AtroxSize_Is145Percent_OnlyForAnAtrox()
    {
        Assert.Equal((float)(1f * EffectMeshSim.AtroxSize), new EffectMeshSim(Record72452(), hostBreed: 4).Scale);
        Assert.Equal(1f, new EffectMeshSim(Record72452(), hostBreed: 1).Scale);
        Assert.Equal(1f, new EffectMeshSim(Record72452()).Scale);
    }

    [Fact]
    public void AtroxModel_SwapsTheHoverbikeCircles_AndDropsOthers()
    {
        Assert.Equal("hoverbike_b_effect_circle_atrox.abiff", new EffectMeshSim(Record72345(), hostBreed: 4).Model);
        Assert.Equal("hoverbike_b_effect_circle.abiff", new EffectMeshSim(Record72345(), hostBreed: 2).Model);
        float[] f = Record72345();
        f[10] = Bits(22);
        Assert.Null(new EffectMeshSim(f, hostBreed: 4).Model);
        Assert.Equal("hoverbike_a_effect_circle_atrox.abiff", EffectMeshSim.AtroxModelName(20));
        Assert.Equal("hoverbike_b_effect_circle_red_atrox.abiff", EffectMeshSim.AtroxModelName(27));
    }

    [Fact]
    public void BodyScale_MultipliesTheDrawnScale()
    {
        var sim = new EffectMeshSim(Record72452(), hostBreed: 4);
        sim.BodyScale = 1.2f;
        Assert.Equal((float)((double)1.2f * (float)(1f * EffectMeshSim.AtroxSize)), sim.DrawScale);
    }

    [Fact]
    public void TheVehicleRecords_AreModelled()
    {
        Assert.True(EffectMeshSim.IsModelled(Record72345()));
        Assert.True(EffectMeshSim.IsModelled(Record72452()));
    }

    /// <summary>A 32-field record with the given flags, model and rendering effect.</summary>
    static float[] MeshRecord(int flags, int model, int effect)
    {
        var f = new float[32];
        f[0] = System.BitConverter.Int32BitsToSingle(flags);
        f[10] = System.BitConverter.Int32BitsToSingle(model);
        f[31] = System.BitConverter.Int32BitsToSingle(effect);
        return f;
    }

    [Fact]
    public void Effect4_IsModelledOnAnyModelNowItIsDrawnLit()
    {
        // 71123, 71224 and 72623 are effect 4 on models 1, 4 and 24. They were carved out while the
        // port faked the lighting with a constant that is only right for the blast wave's emissive
        // white; they go through a lit additive material now, so all of them count.
        Assert.True(EffectMeshSim.IsModelled(MeshRecord(0x3, 1, 4)));
        Assert.True(EffectMeshSim.IsModelled(MeshRecord(0x403, 4, 4)));
        Assert.True(EffectMeshSim.IsModelled(MeshRecord(0x3, 24, 4)));
        Assert.True(EffectMeshSim.IsModelled(MeshRecord(0x3, 2, 4)));
    }

    [Fact]
    public void TheEffectsStockDrawsDifferently_AreStillNotModelled()
    {
        // 1 Holo, 2 Space and 7 ZBias, and the two flags, are untouched by the effect-4 change.
        Assert.False(EffectMeshSim.IsModelled(MeshRecord(0x3, 1, 1)));
        Assert.False(EffectMeshSim.IsModelled(MeshRecord(0x3, 1, 2)));
        Assert.False(EffectMeshSim.IsModelled(MeshRecord(0x3, 1, 7)));
        Assert.False(EffectMeshSim.IsModelled(MeshRecord(0x2000, 1, 4)));
        Assert.False(EffectMeshSim.IsModelled(MeshRecord(0x4000, 1, 4)));
    }

    [Fact]
    public void Animation_RunsWith0x1000_WrappingAtTheTreeTotal()
    {
        var sim = new EffectMeshSim(Record72345());
        Assert.False(sim.StepAnimation(0f, 0.8f)); // time 0: not set
        Assert.True(sim.StepAnimation(0.5f, 0.8f));
        Assert.Equal(0.5f, sim.AnimTime);
        Assert.True(sim.StepAnimation(0.5f, 0.8f)); // 1.0 past 0.8: fmod
        Assert.Equal((float)(1.0 % 0.8f), sim.AnimTime, 5);

        var still = new EffectMeshSim(Record72452()); // no 0x1000
        Assert.False(still.StepAnimation(0.5f, 0.8f));
        Assert.Equal(0f, still.AnimTime);
    }
}
