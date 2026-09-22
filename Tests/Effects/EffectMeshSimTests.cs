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
}
