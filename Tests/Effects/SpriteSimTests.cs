using System;
using Xunit;

/// <summary>
/// Locks <see cref="SpriteSim"/> to stock _GfxControlSprite_t (Process <c>100f6d2b</c>) with 80006, the
/// buff of 201723 Spawn Entrance Nano, and 61085, the buff of 204195 Clan Beam.
/// </summary>
public class SpriteSimTests
{
    static float Bits(uint i) => BitConverter.Int32BitsToSingle(unchecked((int)i));

    static float[] Record80006()
    {
        var f = new float[27];
        f[0] = Bits(7);
        f[7] = Bits(2014);
        f[8] = -1f;
        f[9] = Bits(8);
        f[10] = Bits(2512); // 0x9d0: pulse, size, colour, repeat, follow; Sprite2, additive
        f[11] = 0.5f; f[12] = 0f; f[13] = 0.5f; f[14] = 0f;
        f[15] = 1f; f[16] = 1f; f[17] = 0f; f[18] = 0f;
        f[19] = 0f; f[20] = 0.5f; f[21] = 0f; f[22] = 0f;
        f[23] = 0.5f;
        f[24] = Bits(0xffffffff);
        f[25] = 0.025f;
        f[26] = 1f;
        return f;
    }

    static float[] Record61085()
    {
        var f = new float[27];
        f[0] = Bits(7);
        f[2] = 29.7f;
        f[7] = Bits(2023);
        f[8] = -1f;
        f[9] = Bits(44);
        f[10] = Bits(10579); // 0x2953: face camera, pulse, size, repeat, follow; Sprite3, additive
        f[11] = 1.5f; f[12] = 1.5f; f[13] = 70f; f[14] = 70f;
        f[15] = 1f; f[16] = 1f; f[17] = 0.5f; f[18] = 0.5f;
        f[19] = 1f; f[20] = 1f; f[21] = 0.5f; f[22] = 0.5f;
        f[23] = 1f;
        f[24] = Bits(0xffffffff);
        f[25] = 0.07f;
        f[26] = 10f;
        return f;
    }

    [Fact]
    public void Loader_PicksTheVisual_AndStartsOnTheStartColour()
    {
        var halo = new SpriteSim(Record80006(), 0, 0);
        Assert.Equal(SpriteSim.Kind.Sprite2, halo.Visual);
        Assert.True(halo.Additive);
        Assert.Equal(0xffff0000u, halo.Argb);
        Assert.Equal(0.5f, halo.Width);

        var beam = new SpriteSim(Record61085(), 0, 0);
        Assert.Equal(SpriteSim.Kind.Sprite3, beam.Visual);
        Assert.Equal(70f, beam.Height);
    }

    [Fact]
    public void EachPeriod_ShrinksAndFadesToTheStopColour()
    {
        var sim = new SpriteSim(Record80006(), 0, 0);
        // age 0.35: p = 0.7, f = 0.7; the pulse is sin(0.35 · 2pi) · 0.025.
        Assert.True(sim.Step(0.35f));
        double pulse = Math.Sin(0.35 * 2 * Math.PI) * 0.025;
        Assert.Equal(0.5 * 0.3 + pulse, sim.Width, 5);
        Assert.Equal(sim.Width, sim.Height);
        // Alpha 1 -> 0, red 1 -> 0.5: 0.3 · 255 = 76.5 -> 76, 0.65 · 255 = 165.75 -> 165.
        Assert.Equal(76u, sim.Argb >> 24);
        Assert.Equal(165u, (sim.Argb >> 16) & 0xff);
    }

    [Fact]
    public void WithoutRepeat_OnePeriodEndsIt_AndNegativeRepeatsRunForever()
    {
        var forever = new SpriteSim(Record80006(), 0, 0);
        Assert.True(forever.Step(100.2f));

        float[] f = Record80006();
        f[10] = Bits(2512 & ~SpriteSim.FlagRepeat);
        var once = new SpriteSim(f, 0, 0);
        Assert.True(once.Step(0.49f));
        Assert.False(once.Step(0.5f));

        f = Record80006();
        f[24] = Bits(2);
        var thrice = new SpriteSim(f, 0, 0);
        Assert.True(thrice.Step(1.49f)); // n = 2
        Assert.False(thrice.Step(1.5f)); // n = 3 > 2
    }

    [Fact]
    public void Flag0x20_RunsTheFramesOverThePeriod()
    {
        float[] f = Record80006();
        f[10] = Bits(0x20);
        var sim = new SpriteSim(f, 4, 12);
        Assert.Equal(4, sim.Frame);
        sim.Step(0.25f); // f = 0.5
        Assert.Equal(8, sim.Frame);
    }

    [Fact]
    public void SetStartColour_ShowsAtOnce()
    {
        var sim = new SpriteSim(Record61085(), 0, 0);
        sim.SetStart(1f, 0f, 1f, 0f);
        Assert.Equal(0xff00ff00u, sim.Argb);
    }

    [Fact]
    public void VisualsOtherThan0And3_AreNone()
    {
        float[] f = Record80006();
        f[10] = Bits(1);
        Assert.Equal(SpriteSim.Kind.None, new SpriteSim(f, 0, 0).Visual);
    }
}
