using System;
using Xunit;

/// <summary>
/// Locks <see cref="DeformerSim"/> to stock _GfxControlDeformer_t mode 1, with 45057 (nano 266281's hit).
/// </summary>
public class DeformerSimTests
{
    static float Bits(int v) => BitConverter.Int32BitsToSingle(v);

    // 8 duration 3.1, 10 mode 1, 11 peak 1, 12 fade-in 0.1, 13 fade-out 3, 14 rate 0.2, 15 amplitude 0.12.
    static float[] Fields45057() => new[]
    {
        0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 3.1f, 0f,
        Bits(1), 1f, 0.1f, 3f, 0.2f, 0.12f,
    };

    [Fact]
    public void ReadsTheWobbleMode()
    {
        var s = new DeformerSim(Fields45057());
        Assert.Equal(DeformerSim.WobbleMode, s.Mode);
        Assert.Equal(3.1f, s.Duration);
        Assert.Equal(0f, s.Envelope);
    }

    [Fact]
    public void Envelope_RisesOverTheFadeIn_ThenFadesOutFromWhereItWas()
    {
        var s = new DeformerSim(Fields45057());
        Assert.False(s.Step(0f));
        Assert.Equal(0f, s.Envelope);
        Assert.False(s.Step(0.05f));
        Assert.Equal(0.5f, s.Envelope, 5);
        Assert.False(s.Step(0.09f));
        Assert.Equal(0.9f, s.Envelope, 5);
        Assert.False(s.Terminating);

        // duration - fade-out = 0.1 < age: terminates itself, fading from 0.9 over 3 s.
        Assert.False(s.Step(0.2f));
        Assert.True(s.Terminating);
        Assert.Equal(0.9f, s.Envelope, 5);
        Assert.False(s.Step(1.7f));
        Assert.Equal(0.45f, s.Envelope, 5);
        Assert.True(s.Step(3.2f));
    }

    [Fact]
    public void Terminate_OnlyOnce_KeepsTheFirstStart()
    {
        var s = new DeformerSim(Fields45057()) { Duration = -1f };
        s.Step(0.05f);
        s.Terminate(0.05f);
        s.Terminate(1f);
        s.Step(1.55f);
        Assert.Equal(0.25f, s.Envelope, 5); // (1 - 1.5 / 3) * 0.5
    }

    [Fact]
    public void Envelope_HoldsAfterTheFadeInWhileNotTerminating()
    {
        var s = new DeformerSim(Fields45057()) { Duration = -1f };
        s.Step(0.06f);
        s.Step(5f);
        Assert.False(s.Terminating);
        Assert.Equal(0.6f, s.Envelope, 5);
    }

    [Fact]
    public void WobbleWeight_IsSineSquaredOfTheSourcePosition_TimesAmplitudeAndEnvelope()
    {
        var s = new DeformerSim(Fields45057());
        Assert.Equal(0f, s.WobbleWeight(0f, 0.3f, 1.2f, -0.4f)); // envelope still 0

        s.Step(0.05f);
        // T = 0.01; arg = ((0.3 + 0.13) * 13 + (1.2 - 0.11) * 17 - 0.4 * 0.19) * 11 + 0.01 = 264.494
        // sin = 0.56483; 0.56483² * 0.12 * 0.5
        Assert.Equal(0.0191420f, s.WobbleWeight(0.05f, 0.3f, 1.2f, -0.4f), 6);
    }
}
