using System;
using Xunit;

/// <summary>
/// Locks <see cref="ShieldSim"/> to stock _GfxControlShield_t (<c>100edcf4</c>) and GfxVisualShield's vertex
/// build (<c>1001c94f</c>), with 43608 (nano 56213's hit): cyan 0x8800fffa, offset 0.255, UV x2 scrolled
/// by (0.05, 0.2), fade-in over a second, 3 s.
/// </summary>
public class ShieldSimTests
{
    static float Bits(int v) => BitConverter.Int32BitsToSingle(v);

    static float[] Fields43608(int pulseMode = 0, float pulseMin = 0f, float pulseMax = 0f, float fadeTime = 0f)
    {
        var f = new float[32];
        f[0] = Bits(0x400);
        f[8] = 3f;
        f[9] = Bits(32);
        f[10] = Bits(unchecked((int)0x8800fffa));
        f[11] = 0.255f;
        f[18] = Bits(0);
        f[19] = 2f;
        f[20] = 2f;
        f[21] = 1.5708f;
        f[22] = 15f;
        f[23] = 1.5708f;
        f[24] = 0.05f;
        f[25] = 0.2f;
        f[26] = Bits(pulseMode);
        f[27] = pulseMin;
        f[28] = pulseMax;
        f[29] = Bits(1);
        f[30] = fadeTime;
        return f;
    }

    [Fact]
    public void Loader_ReadsTheShell()
    {
        var s = new ShieldSim(Fields43608());
        Assert.Equal(0x400, s.Flags);
        Assert.Equal(0x8800fffau, s.Colour);
        Assert.Equal(0.255f, s.Offset);
        Assert.Equal(3f, s.Duration);
        Assert.True(s.UniformColour);
    }

    [Fact]
    public void Alpha_FadesInAsSineSquared_OverTheFirstSecond()
    {
        var s = new ShieldSim(Fields43608());
        s.Step(0f);
        Assert.Equal(0, s.UniformAlpha());
        s.Step(0.5f);
        Assert.InRange(s.UniformAlpha(), 67, 68); // 136 * sin²(pi / 4)
        s.Step(1.5f);
        Assert.InRange(s.UniformAlpha(), 135, 136); // clamped at pi / 2: the full 0x88
    }

    [Fact]
    public void Uv_Mode0_ScrollsThenScales()
    {
        var s = new ShieldSim(Fields43608());
        s.Step(2f);
        s.Uv(0.25f, 0.5f, 0f, 0f, 0f, out float u, out float v);
        Assert.Equal((0.25f + 0.1f) * 2f, u, 5);
        Assert.Equal((0.5f + 0.4f) * 2f, v, 5);
    }

    [Fact]
    public void TerminatesItself_AtDurationMinusFade_AndGoes()
    {
        var s = new ShieldSim(Fields43608());
        Assert.False(s.Step(2.9f));
        Assert.False(s.Step(3.01f)); // 3 - 0 < age: terminates, ready from the next call
        Assert.True(s.Terminating);
        Assert.True(s.Step(3.02f));
    }

    [Fact]
    public void FadeMode1_ScalesTheAlphaDown()
    {
        var s = new ShieldSim(Fields43608(fadeTime: 2f));
        s.Step(0.5f);
        s.Terminate(1f);
        Assert.False(s.Step(2f));
        Assert.Equal(0.5f, s.Fade, 5);
        Assert.True(s.Step(3f));
    }

    [Fact]
    public void Pulse_Sawtooth_AndTriangle()
    {
        var saw = new ShieldSim(Fields43608(pulseMode: 1, pulseMin: 1f, pulseMax: 2f));
        saw.Step(2.5f);
        Assert.Equal(1.5f, saw.Time, 5);
        saw.Step(0.5f); // below the range: untouched
        Assert.Equal(0.5f, saw.Time, 5);

        var tri = new ShieldSim(Fields43608(pulseMode: 2, pulseMin: 0f, pulseMax: 1f));
        tri.Step(0.25f);
        Assert.Equal(0.25f, tri.Time, 5);
        tri.Step(1.5f);
        Assert.Equal(0.5f, tri.Time, 5);
    }
}
