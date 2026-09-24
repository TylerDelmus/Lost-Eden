using System;
using Xunit;

/// <summary>
/// Locks <see cref="SunsSim.StepRing"/> to stock _GfxControlSuns_t sunType 1 (<c>100fcbeb</c>), with 43307
/// (the stun buff of 125772 Stunned by Brawl and 16 more: a ring round the head) and 43068 (Butterfly Kick,
/// field 30 set).
/// </summary>
public class SunsRingTests
{
    static float Bits(int i) => BitConverter.Int32BitsToSingle(i);

    static float[] Record43307()
    {
        var f = new float[32];
        f[0] = Bits(5);
        f[7] = Bits(1006);
        f[8] = -1f;
        f[9] = Bits(8);
        f[10] = Bits(SunsSim.RingType);
        f[26] = 6f;
        f[28] = 1.8f;
        f[29] = 9.25f;
        f[30] = 0f;
        f[31] = Bits(1);
        return f;
    }

    static SunsSim Make(float[] f) => new SunsSim(f, () => 0);

    [Fact]
    public void Ring_EighteenSpritesOnACircleRoundTheLocator()
    {
        SunsSim sim = Make(Record43307());
        sim.StepRing(3f, 1f, 2f, 3f); // q = 0.5: w = 0.5, s = 0.75
        Assert.Equal(SunsSim.RingSprites, sim.LastCount);
        for (int i = 0; i < SunsSim.RingSprites; i++)
            Assert.True(sim.Sprites[i].Visible);
        Assert.False(sim.Sprites[18].Visible);

        SunsSim.Sprite a = sim.Sprites[0];
        float theta = (float)(3.0 * 9.25);
        Assert.Equal(1f + (float)Math.Sin(theta) * 0.75f, a.X, 4);
        Assert.Equal(3f + (float)Math.Cos(theta) * 0.75f, a.Z, 4);
        float wobble = (float)Math.Sin(3f * 4f + theta * 3f) * 0.1f;
        Assert.Equal(2f + wobble * 0.75f, a.Y, 4);
        // The pair shares its point.
        Assert.Equal(a.X, sim.Sprites[1].X);
        Assert.Equal(a.Z, sim.Sprites[1].Z);
    }

    [Fact]
    public void Ring_SizesAlphaSpinAndPalette()
    {
        SunsSim sim = Make(Record43307());
        sim.StepRing(3f, 0f, 0f, 0f);
        SunsSim.Sprite a = sim.Sprites[0], b = sim.Sprites[1];
        // s = 0.75, burst adds 1.8 (1 - 0.5)^8.
        float extra = 1.8f * (float)Math.Pow(0.5, 8);
        Assert.Equal(0.9f * 1.8f * 0.75f + extra, a.Width, 4);
        Assert.Equal(1.1f * 1.8f * 0.75f + extra, b.Width, 4);
        Assert.Equal(a.Width, a.Height);
        // alpha = _ftol((1 - 0.25) 255) = 191 over the palette's colour.
        Assert.Equal((191u << 24) | (SunsSim.RingPalette[0] & 0xffffff), a.Argb);
        Assert.Equal((191u << 24) | (SunsSim.RingPalette[1] & 0xffffff), b.Argb);
        Assert.Equal(6f, a.Angle, 5);
        Assert.Equal(-4.5f, b.Angle, 5);
        Assert.Equal(0f, a.Offset);
        Assert.Equal(0.75f * 0.05f, b.Offset, 5);
        Assert.Equal(0, a.Frame);
    }

    [Fact]
    public void Ring_PairsAreSpacedRoundTheCircle()
    {
        float[] f = Record43307();
        f[29] = 0f; // no spin
        SunsSim sim = Make(f);
        sim.StepRing(3f, 0f, 0f, 0f);
        float step = (float)(2 * 6.28000020980835 * 0.05555550009012222);
        Assert.Equal((float)Math.Sin(step) * 0.75f, sim.Sprites[2].X, 4);
        Assert.Equal(SunsSim.RingPalette[2] & 0xffffff, sim.Sprites[2].Argb & 0xffffff);
    }

    [Fact]
    public void Burst_HoldsTheRadiusAtPointThreeEarly()
    {
        SunsSim sim = Make(Record43307());
        sim.StepRing(0.3f, 0f, 0f, 0f); // q = 0.05: s = 1 - 0.95² = 0.0975, held at 0.3
        Assert.Equal(0.3f * 0.05f, sim.Sprites[1].Offset, 5);
        float r = (float)Math.Sqrt(sim.Sprites[0].X * sim.Sprites[0].X + sim.Sprites[0].Z * sim.Sprites[0].Z);
        Assert.Equal(0.3f, r, 4);
    }

    [Fact]
    public void Reversed_ShrinksAndEasesItsFadeNearTheEnd()
    {
        float[] f = Record43307();
        f[26] = 4f;
        f[30] = Bits(1); // 43068
        f[31] = 0;
        SunsSim sim = Make(f);
        sim.StepRing(1f, 0f, 0f, 0f); // x = 0.75, w = 0.25: s = 0.9375, alpha = _ftol((1 - 0.5625) 255)
        Assert.Equal(111u, sim.Sprites[0].Argb >> 24);
        float r = (float)Math.Sqrt(sim.Sprites[0].X * sim.Sprites[0].X + sim.Sprites[0].Z * sim.Sprites[0].Z);
        Assert.Equal(0.9375f, r, 4);

        sim.StepRing(3.8f, 0f, 0f, 0f); // x = 0.05 < 0.2: x = 1 - 0.95 (0.25)^27, alpha ~ 0
        Assert.Equal(0u, sim.Sprites[0].Argb >> 24);
    }

    static float[] Record43768()
    {
        float[] f = Record43307();
        f[7] = Bits(1000);
        f[10] = Bits(SunsSim.HaloType);
        f[26] = 180f;
        f[28] = 0.6f;
        f[29] = 0.25f;
        f[31] = 0;
        return f;
    }

    [Fact]
    public void Halo_EightSpritesTwoAboveTheLocator_GrowingByIndex()
    {
        SunsSim sim = Make(Record43768());
        Assert.True(sim.AgeDriven);
        sim.StepHalo(90f, 1f, 2f, 3f); // q = 0.5: envelope 1
        Assert.Equal(SunsSim.HaloSprites, sim.LastCount);
        Assert.False(sim.Sprites[8].Visible);
        SunsSim.Sprite s3 = sim.Sprites[3];
        Assert.Equal(1f, s3.X);
        Assert.Equal(4f, s3.Y);
        Assert.Equal(3f, s3.Z);
        Assert.Equal(0.6f + 3 * 0.25f, s3.Width, 5);
        Assert.Equal(s3.Width, s3.Height);
        Assert.Equal((3f - 4f) * 45f, s3.Angle, 4);
        Assert.Equal(0xc000ffffu, s3.Argb);
    }

    [Fact]
    public void Halo_EnvelopeRisesAndFallsOverTheDuration()
    {
        SunsSim sim = Make(Record43768());
        sim.StepHalo(0f, 0f, 0f, 0f);
        Assert.Equal(0f, sim.Sprites[0].Width, 6);
        sim.StepHalo(45f, 0f, 0f, 0f); // q = 0.25: e = 1 - 0.5^4
        Assert.Equal(0.6f * (1f - 0.0625f), sim.Sprites[0].Width, 5);
        Assert.Equal(0xc0ffc0c0u, sim.Sprites[0].Argb);
        sim.StepHalo(180f, 0f, 0f, 0f);
        Assert.Equal(0f, sim.Sprites[0].Width, 6);
    }

    [Fact]
    public void Done_AsSoonAsItTerminates()
    {
        SunsSim sim = Make(Record43307());
        sim.StepRing(1f, 0f, 0f, 0f);
        Assert.False(sim.Done);
        sim.Terminating = true;
        Assert.True(sim.Done);
    }
}
