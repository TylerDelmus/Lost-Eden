using System;
using Xunit;

/// <summary>
/// Locks <see cref="SmokeSim"/> to stock _GfxControlSmoke_t (Process <c>100f04bd</c>, init <c>100f0cd4</c>)
/// on GfxVisualSprite2Type0 (full NewSprite <c>100261de</c>), with 43657 (nano 83943's buff: still black
/// puffs at the head) and a moving variant after 43658 (speed 4, height offset 0.5-1).
/// </summary>
public class SmokeSimTests
{
    static float Bits(int i) => BitConverter.Int32BitsToSingle(i);

    static float[] Record43657()
    {
        var f = new float[33];
        f[0] = Bits(5);
        f[2] = 0.5f;
        f[4] = 1.5708f;
        f[7] = Bits(2002);
        f[8] = 30f;
        f[9] = Bits(31);
        f[10] = 2.58667f;
        f[11] = 0.1f; f[12] = 0.1f;
        f[13] = 0f;
        f[14] = 1.7f; f[15] = 2f;
        f[16] = 1.5f; f[17] = 2f;
        f[18] = 1f; f[22] = 1f;
        f[26] = 2f;
        f[27] = 1f; f[28] = 8f;
        f[29] = 0;
        f[30] = 0.25f;
        return f;
    }

    static float[] Moving()
    {
        float[] f = Record43657();
        f[13] = 4f;
        f[31] = 0.5f; f[32] = 1f;
        f[29] = Bits(4);
        return f;
    }

    static SmokeSim Make(float[] f, int id = 43657, int first = 0, int last = 0)
        => new SmokeSim(f, id, first, last, () => 0.25f);

    static readonly float[] Identity = { 1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f };

    // The template's 90° turn about x: the locator's z points down the world y.
    static readonly float[] ZDown = { 1f, 0f, 0f, 0f, 0f, 1f, 0f, -1f, 0f };

    [Fact]
    public void Pool_AndBlend()
    {
        SmokeSim sim = Make(Record43657());
        Assert.Equal(7, sim.Sprites.Length); // _ftol(2.58667 * 1.5 * 2)
        Assert.False(sim.Additive);
        Assert.True(Make(Record43657(), id: 80005).Additive);
    }

    [Fact]
    public void Spawn_LifeSizeAndDiscFromTheDraws()
    {
        SmokeSim sim = Make(Record43657(), first: 0, last: 15);
        sim.Step(1f, 0f, 0f, 0f, 0f, Identity, 0f, 0f, 0f); // _ftol(2.58667) = 2 spawns
        Assert.Equal(2, sim.LiveCount);
        Sprite2Type0Visual.Sprite p = sim.Sprites[0];

        // r = 0.25 for every draw: life 2 * 1.075, a = π/2, b = π/8, radius 0.1, scale 1.075.
        float life = 2f * 1.075f;
        Assert.Equal(life, p.Life, 5);
        Assert.Equal(0f, p.X, 5);
        Assert.Equal(0f, p.Y, 5);
        Assert.Equal((float)(Math.Cos(Math.PI / 8) * 0.1), p.Z, 4);
        Assert.Equal(1.7f * 1.075f, p.Width, 5);
        Assert.Equal(1.5f * 1.075f, p.Height, 5);
        Assert.Equal(0.3f * 1.075f / life, p.DWidth, 5);
        Assert.Equal(0.5f * 1.075f / life, p.DHeight, 5);
        Assert.Equal(15f / life, p.FrameRate, 4);
        // Speed 0: it stays put.
        Assert.Equal(0f, p.VX);
        Assert.Equal(0f, p.VZ);
        // Black, opaque, and no colour change.
        Assert.Equal(1f, p.A);
        Assert.Equal(0f, p.DA);
        Assert.Equal(0f, p.R);
    }

    [Fact]
    public void Spawn_FliesDownTheLocatorZ_WithSpreadOnItsXAndY()
    {
        SmokeSim sim = Make(Moving());
        sim.Step(0.5f, 0f, 0f, 0f, 0f, Identity, 0f, 0f, 0f);
        Sprite2Type0Visual.Sprite p = sim.Sprites[0];
        float inv = 1f / (2f * 1.075f);
        float z = (float)(Math.Cos(Math.PI / 8) * 0.1);
        Assert.Equal(0.625f, p.Y, 5); // height 0.5..1 at r = 0.25
        Assert.Equal(0f, p.VX, 5);
        Assert.Equal(0.3f * z * 4f * inv, p.VY, 5); // local y takes 0.3 of the disc's z
        Assert.Equal(-4f * inv, p.VZ, 5);
    }

    [Fact]
    public void TurnedLocator_RisesAndTheBandSitsOnTheSpawnHeight()
    {
        SmokeSim sim = Make(Moving());
        sim.Step(0.5f, 0f, 1f, 2f, 3f, ZDown, 0f, 0f, 0f);
        Sprite2Type0Visual.Sprite p = sim.Sprites[0];
        float inv = 1f / (2f * 1.075f);
        float z = (float)(Math.Cos(Math.PI / 8) * 0.1);
        // Local (0, 0.625, z) through X = (1,0,0), Y = (0,0,1), Z = (0,-1,0).
        Assert.Equal(1f, p.X, 5);
        Assert.Equal(2f - z, p.Y, 5);
        Assert.Equal(3f + 0.625f, p.Z, 5);
        Assert.Equal(4f * inv, p.VY, 5); // -Z is up
        Assert.Equal(p.Y + 1f, p.WindLow, 5);
        Assert.Equal(8f + p.Y, p.WindHigh, 5);
        Assert.Equal(4, p.WindMode);
        Assert.Equal(0.25f * (0.25f * 0.9f + 0.1f), p.WindScale, 5);
    }

    [Fact]
    public void Duration_StopsSpawningALifeEarly_AndTerminateSetsAgePlusLife()
    {
        float[] f = Record43657();
        f[8] = 3f; // spawns while age < 1
        SmokeSim sim = Make(f);
        sim.Step(1f, 0f, 0f, 0f, 0f, Identity, 0f, 0f, 0f);
        Assert.Equal(0, sim.LiveCount);
        sim.Step(0.9f, 0f, 0f, 0f, 0f, Identity, 0f, 0f, 0f);
        Assert.Equal(2, sim.LiveCount);

        sim.TerminateGracefully(5f);
        Assert.Equal(7f, sim.Duration);
    }

    [Fact]
    public void Flag0x400_KeepsRunningWhenLost()
    {
        float[] f = Record43657();
        Assert.False(Make(f).KeepsRunningWhenLost);
        f[0] = Bits(0x1403);
        SmokeSim sim = Make(f);
        Assert.True(sim.KeepsRunningWhenLost);
        Assert.True(sim.Local);
    }
}
