using System;
using Xunit;

/// <summary>
/// Locks <see cref="Tracer5Sim"/> to stock _GfxControlTracer5_t (init <c>1010077d</c>, update
/// <c>1010065a</c>) with 45708 (nano 45889's tracer): speed 50, streak 4.5, width 0.225, colour 1/1/0.8/0.8.
/// </summary>
public class Tracer5SimTests
{
    static float Bits(int i) => BitConverter.Int32BitsToSingle(i);

    static float[] Record45708()
    {
        var f = new float[20];
        f[0] = Bits(2);
        f[7] = Bits(0);
        f[8] = -1f;
        f[9] = Bits(15);
        f[10] = 50f;
        f[11] = 4.5f;
        f[12] = 0.225f;
        f[13] = 1f; f[14] = 1f; f[15] = 0.8f; f[16] = 0.8f;
        f[17] = Bits(7);
        f[19] = Bits(1);
        return f;
    }

    [Fact]
    public void Streak_RunsTailAtSpeedTimesAge_HeadAhead_CappedAtTheEnd()
    {
        var sim = new Tracer5Sim(Record45708(), 0f, 0f, 0f, 0f, 0f, 20f);
        Assert.Equal(0xffffccccu, sim.Argb);
        sim.Step(0.1f);
        Assert.Equal(5f, sim.Tail, 4);
        Assert.Equal(9.5f, sim.Head, 4);
        Assert.Equal(9.5f, sim.LinkPositions[1], 4); // newest link: the head, along local y
        Assert.Equal(5f, sim.LinkPositions[4], 4);
        Assert.Equal(0f, sim.LinkPositions[7]);
        sim.Step(0.35f);
        Assert.Equal(20f, sim.Head, 4);
        Assert.False(sim.Arrived);
        sim.Step(0.4f);
        Assert.True(sim.Arrived);
        Assert.Equal(20f, sim.Tail, 4);
    }

    [Fact]
    public void Speed_IsCappedAtFiveLengthsASecond()
    {
        var sim = new Tracer5Sim(Record45708(), 0f, 0f, 0f, 2f, 0f, 0f);
        Assert.Equal(10f, sim.Speed, 4);
    }

    [Fact]
    public void LocalBasis_PutsYAlongTheFlight()
    {
        var sim = new Tracer5Sim(Record45708(), 1f, 2f, 3f, 1f, 2f, 13f);
        Assert.Equal(1f, sim.Basis[5], 5);
        Assert.Equal(1f, sim.OriginX);
        Assert.Equal(3f, sim.OriginZ);
    }

    [Fact]
    public void ShortSegment_IsDegenerate()
    {
        var sim = new Tracer5Sim(Record45708(), 0f, 0f, 0f, 0.005f, 0f, 0f);
        Assert.True(sim.Degenerate);
    }
}
