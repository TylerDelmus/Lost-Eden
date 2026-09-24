using System;
using Xunit;

/// <summary>
/// Locks <see cref="ScatterSim"/> to stock GfxControlScatter_t (loader <c>10110cc2</c>, slot build
/// <c>10110db5</c>, Process <c>101108a8</c>), using records 72278, 71229, 71306 and 72343.
/// </summary>
public class ScatterSimTests
{
    static float Bits(int i) => BitConverter.Int32BitsToSingle(i);

    /// <summary>Every Scatter record has exactly 11 fields.</summary>
    static float[] Record(
        int flags = 0, float ox = 0f, float oy = 0f, float oz = 0f,
        int positionMode = 0, int timeMode = 1, int count = 4, float duration = 8f,
        float distance = 0f, float jitter = 0f, int child = 72277)
    {
        var f = new float[11];
        f[0] = Bits(flags);
        f[1] = ox; f[2] = oy; f[3] = oz;
        f[4] = Bits(positionMode);
        f[5] = Bits(timeMode);
        f[6] = Bits(count);
        f[7] = duration;
        f[8] = distance;
        f[9] = jitter;
        f[10] = Bits(child);
        return f;
    }

    [Fact]
    public void TheLoader_ReadsTheElevenFields()
    {
        // 72278, the Brood scatter: one copy on the locator, repeating.
        var sim = new ScatterSim(Record(flags: 0x2400, count: 1, duration: 8f), () => 0.5f);
        Assert.Equal(1, sim.Count);
        Assert.Equal(8f, sim.Duration, 4);
        Assert.Equal(72277, sim.ChildId);
        Assert.True(sim.OnLocator);
        Assert.True(sim.Repeats);
        Assert.False(sim.Ground);
        Assert.False(sim.EndsOnTerminate);
    }

    [Fact]
    public void TheEvenTimeMode_SpacesSlotsOverTheDuration()
    {
        var sim = new ScatterSim(Record(timeMode: ScatterSim.TimeEven, count: 4, duration: 8f), () => 0f);
        Assert.Equal(0f, sim.FireTime(0), 4);
        Assert.Equal(2f, sim.FireTime(1), 4);
        Assert.Equal(4f, sim.FireTime(2), 4);
        Assert.Equal(6f, sim.FireTime(3), 4);
    }

    [Fact]
    public void TheRandomTimeMode_DrawsOverTheDuration()
    {
        var draws = new[] { 0f, 0.25f, 0.5f, 1f };
        int i = 0;
        var sim = new ScatterSim(Record(timeMode: ScatterSim.TimeRandom, count: 4, duration: 8f), () => draws[i++]);
        Assert.Equal(0f, sim.FireTime(0), 4);
        Assert.Equal(2f, sim.FireTime(1), 4);
        Assert.Equal(4f, sim.FireTime(2), 4);
        Assert.Equal(8f, sim.FireTime(3), 4);
    }

    [Fact]
    public void AnUnknownTimeMode_LeavesEverySlotAtZero()
    {
        var sim = new ScatterSim(Record(timeMode: 7, count: 3), () => 0.9f);
        for (int i = 0; i < sim.Count; i++)
            Assert.Equal(0f, sim.FireTime(i), 4);
    }

    [Fact]
    public void TheCubeMode_DrawsAPointInACube()
    {
        // 2r - 1 per axis, times field 8. Draw 1 -> +1, 0 -> -1, 0.5 -> 0.
        var draws = new[] { 1f, 0f, 0.5f };
        int i = 0;
        var sim = new ScatterSim(
            Record(positionMode: ScatterSim.PositionCube, distance: 50f), () => draws[i++]);
        sim.NextOffset(out float x, out float y, out float z);
        Assert.Equal(50f, x, 4);
        Assert.Equal(-50f, y, 4);
        Assert.Equal(0f, z, 4);
        Assert.Equal(0f, sim.Cursor, 4);   // the cube mode never walks the cursor
    }

    [Fact]
    public void TheLineMode_WalksTheCursorAndJitters()
    {
        // 71306's shape: 4 copies over 50 units, jitter 6.
        var sim = new ScatterSim(
            Record(positionMode: ScatterSim.PositionLine, count: 4, distance: 50f, jitter: 6f),
            () => 0.5f);   // every draw is the middle, so no jitter

        sim.NextOffset(out float x, out float y, out float z);
        Assert.Equal(0f, x, 4);
        Assert.Equal(0f, y, 4);
        Assert.Equal(0f, z, 4);
        Assert.Equal(12.5f, sim.Cursor, 4);   // distance / count

        sim.NextOffset(out _, out _, out z);
        Assert.Equal(12.5f, z, 4);
        Assert.Equal(25f, sim.Cursor, 4);
    }

    [Fact]
    public void TheLineMode_AddsTheJitterToTheCursor()
    {
        var draws = new[] { 1f, 1f, 1f };
        int i = 0;
        var sim = new ScatterSim(
            Record(positionMode: ScatterSim.PositionLine, count: 4, distance: 50f, jitter: 6f),
            () => draws[i++]);
        sim.NextOffset(out float x, out float y, out float z);
        Assert.Equal(6f, x, 4);
        Assert.Equal(6f, y, 4);
        Assert.Equal(6f, z, 4);   // cursor 0 + 6
    }

    [Fact]
    public void AnUnknownPositionMode_HasNoOffset()
    {
        var sim = new ScatterSim(Record(positionMode: 5, distance: 50f), () => 1f);
        sim.NextOffset(out float x, out float y, out float z);
        Assert.Equal(0f, x, 4);
        Assert.Equal(0f, y, 4);
        Assert.Equal(0f, z, 4);
    }

    [Fact]
    public void ARearm_ClearsTheFiredSlotsAndMovesTheCycleBase()
    {
        var sim = new ScatterSim(Record(flags: 0x400, count: 3, duration: 9f), () => 0f);
        Assert.Equal(5f, sim.Elapsed(5f), 4);   // the first cycle starts at 0
        for (int i = 0; i < sim.Count; i++)
            sim.MarkFired(i);
        Assert.True(sim.Fired(0));

        sim.Rearm(12f);
        Assert.False(sim.Fired(0));
        Assert.Equal(12f, sim.CycleBase, 4);
        // The slot times are unchanged; they are measured from the new base.
        Assert.Equal(0f, sim.Elapsed(12f), 4);
        Assert.Equal(3f, sim.Elapsed(15f), 4);
    }

    [Fact]
    public void TheFlags_AreReadFromRealRecords()
    {
        // 71229: 30 copies over 10 s, dropped to the ground, no repeat, at a world point.
        var ground = new ScatterSim(Record(flags: 0x1000, count: 30, duration: 10f), () => 0f);
        Assert.True(ground.Ground);
        Assert.False(ground.OnLocator);
        Assert.False(ground.Repeats);

        // 72624: on the locator is off, 0x4000 ends it on terminate.
        var quick = new ScatterSim(Record(flags: 0x4400), () => 0f);
        Assert.True(quick.EndsOnTerminate);
        Assert.True(quick.Repeats);

        // 72343: an offset with no flags at all.
        var offset = new ScatterSim(
            Record(ox: 0.2f, oy: 0.9f, oz: -0.25f, positionMode: 1, count: 6, duration: 6f, distance: 3f),
            () => 0.5f);
        Assert.Equal(0.2f, offset.OffsetX, 4);
        Assert.Equal(0.9f, offset.OffsetY, 4);
        Assert.Equal(-0.25f, offset.OffsetZ, 4);
        Assert.False(offset.OnLocator);
    }
}
