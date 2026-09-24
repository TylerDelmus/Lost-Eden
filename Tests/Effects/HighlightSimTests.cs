using System;
using Xunit;

/// <summary>
/// Locks <see cref="HighlightSim"/> to stock _GfxControlHighlight_t (loader <c>100e2dc0</c>,
/// Process <c>100e2efc</c>, slot 6 <c>100e2d91</c>), using records 11500, 11502, 11506 and 11507.
/// </summary>
public class HighlightSimTests
{
    static float Bits(int i) => BitConverter.Int32BitsToSingle(i);

    static float[] Record(int flags = 0x3, int mode = 0, float duration = 2f, int field11 = 0)
    {
        var f = new float[12];
        f[0] = Bits(flags);
        f[1] = Bits(mode);
        f[2] = duration;
        f[3] = 1f; f[4] = 0f; f[5] = 0f; f[6] = 0f;      // start (transparency, r, g, b)
        f[7] = 0f; f[8] = 0f; f[9] = 1f; f[10] = 0f;     // end
        f[11] = Bits(field11);
        return f;
    }

    [Fact]
    public void TheLoader_ReadsModeDurationAndFlags()
    {
        // 11500: mode 0 over 2 s.
        var sim = new HighlightSim(Record(mode: 0, duration: 2f));
        Assert.Equal(0x3, sim.Flags);
        Assert.Equal(HighlightSim.ModeRamp, sim.Mode);
        Assert.Equal(2f, sim.Duration, 4);
        Assert.Equal(2f, sim.Pulse, 4);
        Assert.False(sim.WritesSpecular);
    }

    [Fact]
    public void ModeTwo_KeepsThePulseAndRunsOnAnInfiniteDuration()
    {
        // 11506: mode 2 over 2 s — the loader moves it to the pulse and forces -1.
        var sim = new HighlightSim(Record(mode: HighlightSim.ModeHold, duration: 2f));
        Assert.Equal(2f, sim.Pulse, 4);
        Assert.Equal(HighlightSim.HoldDuration, sim.Duration, 4);
    }

    [Fact]
    public void ModeZero_IsAPlainRamp()
    {
        var sim = new HighlightSim(Record(mode: 0, duration: 2f));
        Assert.Equal(0f, sim.Phase(0f, 2f), 4);
        Assert.Equal(0.5f, sim.Phase(1f, 2f), 4);
        Assert.Equal(1f, sim.Phase(2f, 2f), 4);
        // Stock does not clamp it.
        Assert.Equal(1.5f, sim.Phase(3f, 2f), 4);
    }

    [Fact]
    public void ModesOneAndThree_ArcUpAndBackDown()
    {
        foreach (int mode in new[] { HighlightSim.ModeArc, HighlightSim.ModeSpecular })
        {
            var sim = new HighlightSim(Record(mode: mode, duration: 1f));
            // 1 - (2u - 1)^2
            Assert.Equal(0f, sim.Phase(0f, 1f), 4);
            Assert.Equal(0.75f, sim.Phase(0.25f, 1f), 4);
            Assert.Equal(1f, sim.Phase(0.5f, 1f), 4);
            Assert.Equal(0.75f, sim.Phase(0.75f, 1f), 4);
            Assert.Equal(0f, sim.Phase(1f, 1f), 4);
        }
    }

    [Fact]
    public void OnlyModeThree_WritesSpecular()
    {
        Assert.False(new HighlightSim(Record(mode: 0)).WritesSpecular);
        Assert.False(new HighlightSim(Record(mode: 1)).WritesSpecular);
        Assert.False(new HighlightSim(Record(mode: 2)).WritesSpecular);
        Assert.True(new HighlightSim(Record(mode: 3)).WritesSpecular);
    }

    [Fact]
    public void ModeTwo_RisesToOneAndHolds()
    {
        var sim = new HighlightSim(Record(mode: HighlightSim.ModeHold, duration: 2f));
        Assert.Equal(0f, sim.Phase(0f, -1f), 4);
        Assert.Equal(0.5f, sim.Phase(1f, -1f), 4);
        Assert.Equal(1f, sim.Phase(2f, -1f), 4);
        Assert.Equal(1f, sim.Phase(30f, -1f), 4);   // held, not run away
    }

    [Fact]
    public void ModeTwo_FadesOverOnePulseAfterATerminate()
    {
        var sim = new HighlightSim(Record(mode: HighlightSim.ModeHold, duration: 2f));
        sim.Terminate();
        Assert.True(sim.FadingOut);
        // Stock's slot 6 sets duration = pulse + age; terminated at 10 s that is 12.
        Assert.Equal(1f, sim.Phase(10f, 12f), 4);
        Assert.Equal(0.5f, sim.Phase(11f, 12f), 4);
        Assert.Equal(0f, sim.Phase(12f, 12f), 4);
    }

    [Fact]
    public void AnUnknownMode_StaysAtZero()
    {
        var sim = new HighlightSim(Record(mode: 9, duration: 2f));
        Assert.Equal(0f, sim.Phase(0f, 2f), 4);
        Assert.Equal(0f, sim.Phase(1f, 2f), 4);
    }

    [Fact]
    public void FieldElevenIsReadButEveryRecordLeavesItZero()
    {
        Assert.Equal(0, new HighlightSim(Record()).Field11);
        Assert.Equal(1, new HighlightSim(Record(field11: 1)).Field11);
    }
}
