using System;
using Xunit;

/// <summary>
/// Locks <see cref="FireSim"/> to stock _GfxControlFire_t (Process <c>100dbed5</c>, init <c>100dc59c</c>)
/// on GfxVisualSprite2Type0 (InitSpriteDefault <c>10025844</c>, NewSprite <c>10025be0</c>), with 2200 (nano
/// 100250's buff): 16 sprites a second off a 0.5 m disc, 3 m wide shrinking to 1 m, 1 s life, wind mode 4.
/// </summary>
public class FireSimTests
{
    static float Bits(int i) => BitConverter.Int32BitsToSingle(i);

    static float[] Record2200()
    {
        var f = new float[32];
        f[0] = Bits(5);
        f[3] = -0.25f;
        f[8] = -1f;
        f[9] = Bits(9);
        f[10] = 16f;
        f[11] = 0f; f[12] = 0.5f;
        f[13] = 4f;
        f[14] = 3f; f[15] = 1f;
        f[16] = 3f; f[17] = 3f;
        for (int i = 18; i <= 25; i++)
            f[i] = 1f;
        f[26] = 1f;
        f[27] = 0f; f[28] = 4f;
        f[29] = Bits(4);
        f[30] = 0.25f;
        f[31] = 0;
        return f;
    }

    /// <summary>
    /// rand() for the gate, then (0.25, 0.5, 0.25) for the unit vector (each axis v / 16384 - 1), so
    /// d = (1, 2, 1) / sqrt(6), for as many calls as the test makes.
    /// </summary>
    static Func<int> Rand()
    {
        int[] seq = { 0, 20480, 24576, 20480 };
        int i = 0;
        return () => seq[i++ % seq.Length];
    }

    static FireSim Make(float[] f, int first = 0, int last = 0)
        => new FireSim(f, first, last, () => 0.25f, Rand());

    // A locator whose z points down the world y: stock sends the fire down its z, so up.
    static readonly float[] ZDown = { 1f, 0f, 0f, 0f, 0f, 1f, 0f, -1f, 0f };

    [Fact]
    public void Pool_IsRateTimesOneAndAHalfTimesLife()
    {
        FireSim sim = Make(Record2200());
        Assert.Equal(24, sim.Sprites.Length); // _ftol(16 * 1.5 * 1)
    }

    [Fact]
    public void Spawns_FollowTheRateOverAge()
    {
        FireSim sim = Make(Record2200());
        sim.Step(0f, 0f, 0f, 0f, 0f, ZDown, 0f, 0f, 0f);
        Assert.Equal(0, sim.LiveCount);
        sim.Step(0.0625f, 0.0625f, 0f, 0f, 0f, ZDown, 0f, 0f, 0f);
        Assert.Equal(1, sim.LiveCount);
        sim.Step(0.5f, 0.4375f, 0f, 0f, 0f, ZDown, 0f, 0f, 0f);
        Assert.Equal(8, sim.LiveCount);
    }

    [Fact]
    public void Spawn_SitsOnTheDiscAndRisesDownTheLocatorZ()
    {
        FireSim sim = Make(Record2200());
        sim.Step(0.0625f, 0f, 1f, 2f, 3f, ZDown, 0f, 0f, 0f);
        Sprite2Type0Visual.Sprite p = sim.Sprites[0];
        float d = (float)(1.0 / Math.Sqrt(6.0));
        float r = 0.125f; // 0.25 * (0.5 - 0) + 0
        // Local (d.x r, d.z r, 0) through X = (1, 0, 0), Y = (0, 0, 1).
        Assert.Equal(1f + d * r, p.X, 5);
        Assert.Equal(2f, p.Y, 5);
        Assert.Equal(3f + d * r, p.Z, 5);
        // -Z = (0, 1, 0), times |d.y| * 4 / 1.
        Assert.Equal(0f, p.VX, 5);
        Assert.Equal(2f * d * 4f, p.VY, 5);
        Assert.Equal(0f, p.VZ, 5);
    }

    [Fact]
    public void Spawn_TakesTheDefaultsAndTheWindBandAboveItsOwnHeight()
    {
        FireSim sim = Make(Record2200(), first: 0, last: 15);
        sim.Step(0.0625f, 0f, 0f, 2f, 0f, ZDown, 0f, 0f, 0f);
        Sprite2Type0Visual.Sprite p = sim.Sprites[0];
        Assert.Equal(3f, p.Width);
        Assert.Equal(-2f, p.DWidth);
        Assert.Equal(3f, p.Height);
        Assert.Equal(0f, p.DHeight);
        Assert.Equal(1f, p.Life);
        Assert.Equal(1f, p.A);
        Assert.Equal(0f, p.DA);
        Assert.Equal(15f, p.FrameRate);
        Assert.Equal(4, p.WindMode);
        Assert.Equal(2f, p.WindLow, 5);
        Assert.Equal(6f, p.WindHigh, 5);
        Assert.Equal(0.25f, p.WindScale);
    }

    [Fact]
    public void Flag0x100_RisesUpTheWorldY_AndField2LiftsTheSprite()
    {
        float[] f = Record2200();
        f[0] = Bits(0x105);
        f[2] = 0.5f;
        FireSim sim = Make(f);
        float[] identity = { 1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f };
        sim.Step(0.0625f, 0f, 0f, 0f, 0f, identity, 0f, 0f, 0f);
        Sprite2Type0Visual.Sprite p = sim.Sprites[0];
        float d = (float)(1.0 / Math.Sqrt(6.0));
        Assert.Equal(d * 0.125f + 0.5f, p.Y, 5); // local y = d.z r, then the lift
        Assert.Equal(0f, p.VZ, 5);
        Assert.Equal(2f * d * 4f, p.VY, 5);
    }

    [Fact]
    public void LocalMode_SpawnsInTheLocatorFrame_DownItsZ()
    {
        float[] f = Record2200();
        f[0] = Bits(7);
        FireSim sim = Make(f);
        Assert.True(sim.Local);
        sim.Step(0.0625f, 0f, 0f, 0f, 0f, null, 0f, 0f, 0f);
        Sprite2Type0Visual.Sprite p = sim.Sprites[0];
        float d = (float)(1.0 / Math.Sqrt(6.0));
        Assert.Equal(d * 0.125f, p.X, 5);
        Assert.Equal(d * 0.125f, p.Y, 5);
        Assert.Equal(0f, p.Z);
        Assert.Equal(-2f * d * 4f, p.VZ, 5);
    }

    [Fact]
    public void RandMask_GatesTheWholeCall()
    {
        float[] f = Record2200();
        f[31] = Bits(7);
        var sim = new FireSim(f, 0, 0, () => 0.25f, () => 1);
        sim.Step(0.5f, 0f, 0f, 0f, 0f, ZDown, 0f, 0f, 0f);
        Assert.Equal(0, sim.LiveCount);
    }

    [Fact]
    public void Duration_StopsSpawningALifeEarly_AndTerminateSetsAgePlusLife()
    {
        float[] f = Record2200();
        f[8] = 1.5f; // 2204: spawns until age 0.5
        FireSim sim = Make(f);
        sim.Step(0.5f, 0f, 0f, 0f, 0f, ZDown, 0f, 0f, 0f);
        Assert.Equal(0, sim.LiveCount);
        sim.Step(0.4375f, 0f, 0f, 0f, 0f, ZDown, 0f, 0f, 0f);
        Assert.Equal(7, sim.LiveCount);

        sim.TerminateGracefully(2f);
        Assert.Equal(3f, sim.Duration);
    }

    [Fact]
    public void StartColor_ChangesLaterSpawnsAndTheirRate()
    {
        FireSim sim = Make(Record2200());
        sim.SetStartColor(0.5f, 1f, 0f, 0f);
        sim.Step(0.0625f, 0f, 0f, 0f, 0f, ZDown, 0f, 0f, 0f);
        Sprite2Type0Visual.Sprite p = sim.Sprites[0];
        Assert.Equal(0.5f, p.A);
        Assert.Equal(0.5f, p.DA); // (1 - 0.5) / 1
        Assert.Equal(0f, p.G);
        Assert.Equal(1f, p.DG);
    }

    [Fact]
    public void WindMode4_BandStartsAtTheSpawnHeight_SoTheDriftGrowsAsItRises()
    {
        FireSim sim = Make(Record2200());
        sim.Step(0.0625f, 0f, 0f, 0f, 0f, ZDown, 0f, 0f, 0f);
        float x0 = sim.Sprites[0].X;

        // At its own spawn height h = 0, so no drift on the first move.
        sim.Step(0.0625f, 0.1f, 0f, 0f, 0f, ZDown, 2f, 0f, 0f);
        Assert.Equal(x0, sim.Sprites[0].X, 6);

        float h = sim.Sprites[0].Y / 4f;
        sim.Step(0.0625f, 0.1f, 0f, 0f, 0f, ZDown, 2f, 0f, 0f);
        float f = (h * h + h) * 0.5f;
        Assert.Equal(x0 + 2f * f * 0.25f * 0.1f, sim.Sprites[0].X, 5);
    }
}
