using System;
using Xunit;

/// <summary>
/// Locks <see cref="SparksSim"/> to stock _GfxControlSparks_t (Process <c>100f24c8</c>, spawn <c>100f1bbe</c>)
/// and GfxVisualSprite2Type0 (NewSprite <c>100261de</c>, ProcessSprites <c>100267c1</c>), with 2710 (nano
/// 45889's hit): a burst of 16 red sprites, speed 0.5-1 x 3, life 0.25-0.5 s, gravity -9.8, alpha blend.
/// </summary>
public class SparksSimTests
{
    static readonly float[] Identity = { 1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f };

    static float Bits(int i) => BitConverter.Int32BitsToSingle(i);

    static float[] Record2710()
    {
        var f = new float[41];
        f[0] = Bits(0x801);
        f[7] = Bits(1004);
        f[8] = 5f;
        f[9] = Bits(16);
        f[12] = 0.3f; f[13] = 0.3f; f[14] = 0.3f; f[15] = 0.3f;
        f[16] = 1f; f[17] = 1f;
        f[21] = 1f;
        f[26] = 6.28319f;
        f[27] = 1.5708f; f[28] = -1.5708f;
        f[29] = 0.5f; f[30] = 1f;
        f[31] = Bits(16);
        f[32] = 3f; f[33] = 3f;
        f[34] = 0.25f; f[35] = 0.5f;
        f[37] = 8f;
        f[39] = 1f;
        f[40] = -9.8f;
        return f;
    }

    static SparksSim Make(float[] f, float r = 0.25f) => new SparksSim(f, 0, 0, () => r, () => 0);

    [Fact]
    public void RateZero_IsOneBurstOfField31_OnTheFirstCall()
    {
        SparksSim sim = Make(Record2710());
        Assert.Equal(16, sim.Sprites.Length);
        sim.Step(0f, 0f, 0f, 0f, 0f, Identity);
        Assert.Equal(16, sim.LiveCount);
        sim.Step(0.1f, 0.1f, 0f, 0f, 0f, Identity);
        Assert.Equal(16, sim.LiveCount);
        Assert.False(sim.Additive); // flag 0x800: SrcAlpha / InvSrcAlpha
    }

    [Fact]
    public void Velocity_IsSphericalAlongTheAxes_TimesField32()
    {
        // r = 0.25: a = pi/2, b = pi/4, s = 0.625.
        SparksSim sim = Make(Record2710());
        sim.Step(0f, 0f, 1f, 2f, 3f, Identity);
        Sprite2Type0Visual.Sprite p = sim.Sprites[0];
        float k = (float)(Math.Sqrt(0.5) * 0.625 * 3.0);
        Assert.Equal(0f, p.VX, 3);
        Assert.Equal(k, p.VY, 3);
        Assert.Equal(k, p.VZ, 3);
        Assert.Equal(1f, p.X);
        Assert.Equal(0.3125f, p.Life, 5);
    }

    [Fact]
    public void Process_MovesThenFallsThenFadesThenDies()
    {
        SparksSim sim = Make(Record2710());
        sim.Step(0f, 0f, 0f, 0f, 0f, Identity);
        float vy = sim.Sprites[0].VY;
        sim.Step(0.1f, 0.1f, 0f, 0f, 0f, Identity);
        Sprite2Type0Visual.Sprite p = sim.Sprites[0];
        Assert.Equal(vy * 0.1f, p.Y, 5);
        Assert.Equal(vy - 0.98f, p.VY, 4);
        // Alpha 1 - 0.1 / 0.3125 = 0.68 -> FISTP(173.4 - 0.49999) = 173; red stays 255.
        Assert.Equal(173u, p.Argb >> 24);
        Assert.Equal(0xffu, (p.Argb >> 16) & 0xff);
        sim.Step(0.4f, 0.3f, 0f, 0f, 0f, Identity);
        Assert.Equal(0, sim.LiveCount);
    }

    [Fact]
    public void Rate_SpawnsUpToRateTimesAge_WhileBeforeDurationMinusLife()
    {
        float[] f = Record2710();
        f[10] = 20f;
        f[31] = Bits(0);
        SparksSim sim = Make(f);
        Assert.Equal(15, sim.Sprites.Length); // _ftol(20 * 1.5 * 0.5)
        sim.Step(0.2f, 0f, 0f, 0f, 0f, Identity);
        Assert.Equal(4, sim.LiveCount);
        sim.Step(4.6f, 0f, 0f, 0f, 0f, Identity); // past duration - life: nothing new
        Assert.Equal(4, sim.LiveCount);
    }

    [Fact]
    public void RandMask_GatesTheWholeSpawnCall()
    {
        float[] f = Record2710();
        f[24] = Bits(1);
        var sim = new SparksSim(f, 0, 0, () => 0.25f, () => 1);
        sim.Step(0f, 0f, 0f, 0f, 0f, Identity);
        Assert.Equal(0, sim.LiveCount);
    }

    [Fact]
    public void WindMode4_DriftsByHeightInTheBand()
    {
        float[] f = Record2710();
        f[29] = 0f; f[30] = 0f;   // no launch speed
        f[40] = 0f;               // no gravity
        f[36] = -8f; f[37] = 8f;  // band -8..8 about the emitter: h = 0.5 at the emitter
        f[38] = Bits(4);
        SparksSim sim = Make(f);  // r = 0.25: wind scale 0.9 * 0.25 + 0.1 = 0.325
        sim.Step(0f, 0f, 0f, 0f, 0f, Identity, 2f, 0f, 0f);
        sim.Step(0.1f, 0.1f, 0f, 0f, 0f, Identity, 2f, 0f, 0f);
        // f = (0.25 + 0.5) / 2 = 0.375; x = 2 * 0.375 * 0.325 * 0.1.
        Assert.Equal(2f * 0.375f * 0.325f * 0.1f, sim.Sprites[0].X, 5);
    }

    [Fact]
    public void WindModeZero_IgnoresTheWind()
    {
        float[] f = Record2710();
        f[29] = 0f; f[30] = 0f; f[40] = 0f;
        SparksSim sim = Make(f);
        sim.Step(0f, 0f, 0f, 0f, 0f, Identity, 5f, 0f, 0f);
        sim.Step(0.1f, 0.1f, 0f, 0f, 0f, Identity, 5f, 0f, 0f);
        Assert.Equal(0f, sim.Sprites[0].X);
    }

    [Fact]
    public void Cell_UsesRowsForTheRow()
    {
        Assert.Equal(9, Sprite2Type0Visual.Cell(5.7f, 4, 2)); // (5 / 2) * 4 + 5 % 4
        Assert.Equal(5, Sprite2Type0Visual.Cell(5f, 4, 4));
    }
}
