using System;
using Xunit;

/// <summary>
/// Locks <see cref="Tracer8Sim"/> to stock GfxControlTracer8_t (loader <c>10114c68</c>, init
/// <c>10114d6d</c>, Process <c>10114b73</c>), with 71001 — the only one of the six records a nano
/// reaches (305335 Nanobot Suppression Rocket).
/// </summary>
public class Tracer8SimTests
{
    static float Bits(int i) => BitConverter.Int32BitsToSingle(i);

    /// <summary>71001: child 71520 (an EffectMesh) at 40 m/s, teardown fires Meta 71004.</summary>
    static float[] Record71001()
    {
        var f = new float[15];
        f[0] = Bits(3);
        f[8] = 60f;
        f[9] = Bits(-1);
        f[11] = Bits(71520);
        f[12] = 40f;
        f[14] = Bits(71004);
        return f;
    }

    [Fact]
    public void ReadsTheChildTheSpeedAndTheTeardownEffect()
    {
        var sim = new Tracer8Sim(Record71001(), 0f, 0f, 0f, 0f, 0f, 100f);
        Assert.Equal(71520, sim.ChildId);
        Assert.Equal(71004, sim.EndEffectId);
        Assert.Equal(40f, sim.Speed, 4);
        Assert.Equal(60f, sim.Duration, 4);
        Assert.Equal(100f, sim.Length, 4);
        Assert.False(sim.ReadyAtStart);
        // The direction is the line normalised; the start is kept for the teardown's spawn.
        Assert.Equal(0f, sim.DirX, 5);
        Assert.Equal(1f, sim.DirZ, 5);
    }

    [Fact]
    public void FliesAtTheSpeed_ThenLandsExactlyOnTheTarget()
    {
        var sim = new Tracer8Sim(Record71001(), 1f, 2f, 3f, 1f, 2f, 43f);   // 40 m along +z
        Assert.Equal(40f, sim.Length, 4);

        Assert.False(sim.Position(0f, out float x, out float y, out float z));
        Assert.Equal(1f, x, 4); Assert.Equal(2f, y, 4); Assert.Equal(3f, z, 4);

        Assert.False(sim.Position(0.5f, out _, out _, out z));
        Assert.Equal(23f, z, 4);                       // 40 m/s for half a second

        // 10114bd1: reaching the length clamps the child to the target on the very call that ends it.
        Assert.True(sim.Position(1f, out _, out _, out z));
        Assert.Equal(43f, z, 4);
        Assert.True(sim.Position(5f, out _, out _, out z));
        Assert.Equal(43f, z, 4);
    }

    [Fact]
    public void TheSpeedIsCappedAtFiveTimesTheLength()
    {
        // 10114e13: 40 m/s over a 2 m line would cross in 50 ms, so stock pulls it back to 10 m/s.
        var sim = new Tracer8Sim(Record71001(), 0f, 0f, 0f, 0f, 0f, 2f);
        Assert.Equal(10f, sim.Speed, 4);
        Assert.Equal(2f, sim.Length, 4);
        Assert.True(sim.Position(0.2f, out _, out _, out float z));
        Assert.Equal(2f, z, 4);

        // Only ever downwards: a slow record keeps its speed.
        float[] slow = Record71001();
        slow[12] = 3f;
        Assert.Equal(3f, new Tracer8Sim(slow, 0f, 0f, 0f, 0f, 0f, 100f).Speed, 4);
    }

    [Fact]
    public void AFlightShorterThanACentimetreIsDoneBeforeItStarts()
    {
        // 10114ddd compares 0.01 against the length and only readies when 0.01 is the greater.
        Assert.True(new Tracer8Sim(Record71001(), 0f, 0f, 0f, 0f, 0f, 0.009f).ReadyAtStart);
        Assert.False(new Tracer8Sim(Record71001(), 0f, 0f, 0f, 0f, 0f, 0.01f).ReadyAtStart);
        Assert.False(new Tracer8Sim(Record71001(), 0f, 0f, 0f, 0f, 0f, 0.011f).ReadyAtStart);
        // Unordered takes the same arm as greater, so a NaN line flies rather than stopping.
        Assert.False(new Tracer8Sim(Record71001(), 0f, 0f, 0f, float.NaN, 0f, 0f).ReadyAtStart);
    }

    [Fact]
    public void FieldsTenAndThirteenAreLoadedAndNeverUsed()
    {
        // The loader writes them to +0x38 and +0x40 and nothing in the class reads either again.
        float[] rec = Record71001();
        rec[10] = Bits(7);
        rec[13] = 1.5f;
        var sim = new Tracer8Sim(rec, 0f, 0f, 0f, 0f, 0f, 100f);
        Assert.Equal(7, sim.UnusedField10);
        Assert.Equal(1.5f, sim.UnusedField13, 4);
        Assert.Equal(40f, sim.Speed, 4);
    }

    [Fact]
    public void TheOtherFiveRecordsCarryAChildButFireNothingOnTeardown()
    {
        // 72151-72155 all leave field 14 at 0; only 71001 sets it, and it points back at the start.
        float[] rec = Record71001();
        rec[11] = Bits(71512);
        rec[12] = 25f;
        rec[14] = 0f;
        var sim = new Tracer8Sim(rec, 0f, 0f, 0f, 0f, 0f, 50f);
        Assert.Equal(71512, sim.ChildId);
        Assert.Equal(0, sim.EndEffectId);
        Assert.Equal(25f, sim.Speed, 4);
    }
}
