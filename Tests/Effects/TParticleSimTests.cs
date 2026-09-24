using System;
using Xunit;

/// <summary>
/// Locks <see cref="TParticleSim"/> to stock GfxControlTParticle_t (Process <c>10113101</c>) and
/// GfxVisualTParticle (ctor <c>10029de9</c>, ProcessParticles <c>10029c81</c>).
/// </summary>
public class TParticleSimTests
{
    static float Bits(int i) => BitConverter.Int32BitsToSingle(i);

    /// <summary>Shaped like 71029: mode 0, radius 0.1, trail 0.3, gravity -1, x/z +-160, y 0..10.</summary>
    static float[] Record(int mode = 0, float duration = 2f)
    {
        var f = new float[41];
        f[0] = Bits(0x303);
        f[8] = duration;
        f[10] = Bits(mode);
        f[11] = Bits(4);
        f[12] = 0.1f;
        f[15] = 0.3f;
        f[16] = -1f;
        f[17] = -160f; f[18] = 160f;
        f[19] = 0f; f[20] = 10f;
        f[21] = -160f; f[22] = 160f;
        f[23] = Bits(unchecked((int)0xff000000u)); f[24] = Bits(unchecked((int)0xffffffffu));
        f[25] = Bits(unchecked((int)0x80000000u)); f[26] = Bits(unchecked((int)0x80ffffffu));
        f[27] = Bits(0); f[28] = Bits(0);
        f[35] = 0.1f; f[36] = 0.2f; f[37] = 0.3f; f[38] = 0.4f; f[39] = 0.5f; f[40] = 0.6f;
        return f;
    }

    [Fact]
    public void Mode0_FliesBallistically_WithTheTailTrailingByVelocity()
    {
        var sim = new TParticleSim(Record(), () => 0.5f);
        // Every draw 0.5: a zero direction, so p = 0; v = (0, 5, 0).
        Assert.True(sim.Advance(0.1f));
        TParticleSim.Particle p = sim.Particles[0];
        Assert.Equal(0.5f, p.Y, 5);          // p += v dt
        Assert.Equal(5f - 0.1f, p.VY, 5);    // v += a dt
        Assert.Equal(0.5f - 4.9f * 0.3f, p.TY, 4); // tail = p - v * trail
    }

    [Fact]
    public void ColoursAndWidths_BlendThroughTheMiddleKey()
    {
        var sim = new TParticleSim(Record(), () => 0.5f);
        sim.Advance(0f);
        Assert.Equal(0xff000000u, sim.TailArgb);
        Assert.Equal(0xffffffffu, sim.HeadArgb);
        Assert.Equal(0.1f, sim.TailWidth, 5);
        Assert.Equal(0.2f, sim.HeadWidth, 5);

        sim.Advance(1f); // u = 0.5: the second half at t = 0, the middle key
        Assert.Equal(0x80000000u, sim.TailArgb);
        Assert.Equal(0.3f, sim.TailWidth, 5);
        Assert.Equal(0.4f, sim.HeadWidth, 5);
    }

    [Fact]
    public void Ready_OncePastTheDuration()
    {
        var sim = new TParticleSim(Record(duration: 1f), () => 0.5f);
        Assert.True(sim.Advance(1f));
        Assert.False(sim.Advance(0.01f));
    }

    [Fact]
    public void Mode1_OnlyPlacesTails_AndNothingMoves()
    {
        var sim = new TParticleSim(Record(mode: 1), () => 1f);
        sim.Advance(0.5f);
        TParticleSim.Particle p = sim.Particles[0];
        Assert.Equal(0.1f, p.TX, 5);
        Assert.Equal(0f, p.X);
    }
}
