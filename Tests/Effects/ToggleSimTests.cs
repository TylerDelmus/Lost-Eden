using System;
using Xunit;

/// <summary>
/// Locks <see cref="ToggleSim"/> to stock GfxControlToggle_t (loader <c>1011265a</c>, Process <c>10112952</c>,
/// playfield test <c>101125ed</c>, moving flag <c>10112580</c>) with the vehicle buffs' 72412 (0x2c03) and
/// 72416 (0xac03), and 72106's playfield list.
/// </summary>
public class ToggleSimTests
{
    static float I(int v) => BitConverter.Int32BitsToSingle(v);

    static float[] Record(int flags, int child, params int[] playfields)
    {
        var f = new float[5 + playfields.Length];
        f[0] = I(flags);
        f[1] = -1f;
        f[2] = I(child);
        f[3] = 0f;
        f[4] = I(playfields.Length);
        for (int i = 0; i < playfields.Length; i++)
            f[5 + i] = I(playfields[i]);
        return f;
    }

    [Fact]
    public void Loads_72412()
    {
        var sim = new ToggleSim(Record(0x2c03, 72411));
        Assert.Equal(72411, sim.ChildId);
        Assert.Equal(-1f, sim.Duration);
        Assert.True(sim.Rearm);
        Assert.Equal(0, sim.PlayfieldCount);
    }

    [Fact]
    public void Playfields_IncludeOrExclude()
    {
        var only = new ToggleSim(Record(3, 71119, 385, 19)); // 72106
        Assert.True(only.PlayfieldAllows(19, 0));
        Assert.False(only.PlayfieldAllows(20, 0));
        Assert.False(new ToggleSim(Record(3, 1)).PlayfieldAllows(19, 0)); // an empty include list fails

        var except = new ToggleSim(Record(0x1803, 71121, 153, 4106)); // 3421
        Assert.False(except.PlayfieldAllows(153, 0));
        Assert.True(except.PlayfieldAllows(20, 0));
        Assert.True(new ToggleSim(Record(0x2c03, 72411)).PlayfieldAllows(0, 0)); // 72412: no list
    }

    [Fact]
    public void StartMoving_IsARisingEdge()
    {
        var sim = new ToggleSim(Record(0x2c03, 72411));
        Assert.False(sim.StartOnDynel(1, 0f));   // standing
        Assert.True(sim.StartOnDynel(1, 5f));    // starts forward
        Assert.False(sim.StartOnDynel(1, 5f));   // still moving: no second child
        Assert.False(sim.StartOnDynel(-1, 0f));  // backing holds the flag
        Assert.True(sim.Moving);
        Assert.False(sim.StartOnDynel(1, 0f));   // stops
        Assert.True(sim.StartOnDynel(1, 2f));    // starts again
    }

    [Fact]
    public void StopWithDynel_OnlyWith0x8000_AsTheSpeedDrops()
    {
        var stop = new ToggleSim(Record(0xac03, 72415)); // 72416
        stop.StartOnDynel(1, 5f);
        Assert.False(stop.StopWithDynel(1, 5f));
        Assert.True(stop.StopWithDynel(1, 0f));
        Assert.False(stop.StopWithDynel(1, 0f));

        var keep = new ToggleSim(Record(0x2c03, 72411)); // 72412
        keep.StartOnDynel(1, 5f);
        Assert.False(keep.StopWithDynel(1, 0f));
    }

    [Fact]
    public void WithoutStartMoving_TheChildStartsAtOnce()
    {
        var sim = new ToggleSim(Record(3, 71119, 385, 19));
        Assert.True(sim.StartOnDynel(1, 0f));
    }

    [Fact]
    public void Terminate_ClearsRearm()
    {
        var sim = new ToggleSim(Record(0x2c03, 72411));
        sim.Terminate();
        Assert.False(sim.Rearm);
    }
}
