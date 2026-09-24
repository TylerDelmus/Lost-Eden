using System;
using Xunit;

/// <summary>
/// Locks <see cref="Sprite2Type0Visual"/> to stock DisplaySystem GfxVisualSprite2Type0: the direction every
/// NewSprite draws (<c>10026231</c>) and wind mode 7 (<c>1002682b</c> / <c>10026a03</c>, 80011's fountain).
/// </summary>
public class Sprite2Type0VisualTests
{
    static Sprite2Type0Visual.Sprite Steered(float gravity, float speed) => new Sprite2Type0Visual.Sprite
    {
        Life = 1f,
        VX = 100f, VY = 100f, VZ = 100f, // mode 7 overwrites the spawn velocity
        WindMode = Sprite2Type0Visual.SteeredMode,
        WindLow = gravity,
        WindHigh = speed,
    };

    [Fact]
    public void NewSprite_DrawsAnUpwardDirection()
    {
        var v = new Sprite2Type0Visual(4, () => 0.25f);
        v.NewSprite(new Sprite2Type0Visual.Sprite { Life = 1f });
        Sprite2Type0Visual.Sprite p = v.Sprites[0];
        Assert.Equal(-0.5f, p.DirX, 6);
        Assert.Equal(0.25f * 0.89f + 0.1f, p.DirY, 6);
        Assert.Equal(-0.5f, p.DirZ, 6);
        Assert.Equal(0f, v.Gravity); // only mode 7 touches it
    }

    [Fact]
    public void Mode7_SetsGravityFromTheBandBottom_EvenWhenThePoolIsFull()
    {
        var v = new Sprite2Type0Visual(1, () => 0.25f);
        Assert.True(v.NewSprite(Steered(-8.8f, 10.5f)));
        Assert.Equal(-8.8f, v.Gravity);
        Assert.False(v.NewSprite(Steered(-3f, 10.5f)));
        Assert.Equal(-3f, v.Gravity);
    }

    [Fact]
    public void Mode7_FliesAtItsSpeedAlongTheDirection_ThenBendsUnderGravity()
    {
        var v = new Sprite2Type0Visual(1, () => 0.25f);
        v.NewSprite(Steered(-8.8f, 10.5f));
        Sprite2Type0Visual.Sprite s = v.Sprites[0];
        float dx = s.DirX, dy = s.DirY, dz = s.DirZ;

        v.ProcessSprites(0.1f, 50f, 0f, 0f); // wind is ignored
        Sprite2Type0Visual.Sprite p = v.Sprites[0];
        Assert.Equal(dx * 10.5f * 0.1f, p.X, 5);
        Assert.Equal(dy * 10.5f * 0.1f, p.Y, 5);
        Assert.Equal(dz * 10.5f * 0.1f, p.Z, 5);

        float vy = dy * 10.5f - 8.8f * 0.1f;
        Assert.Equal(vy, p.VY, 5);
        float len = (float)Math.Sqrt(p.VX * p.VX + p.VY * p.VY + p.VZ * p.VZ);
        Assert.Equal(1f, (float)Math.Sqrt(p.DirX * p.DirX + p.DirY * p.DirY + p.DirZ * p.DirZ), 5);
        Assert.Equal(vy / len, p.DirY, 5);

        // Next frame the speed is back to 10.5 along the bent direction.
        v.ProcessSprites(0.1f, 0f, 0f, 0f);
        Sprite2Type0Visual.Sprite q = v.Sprites[0];
        Assert.Equal(p.DirX * 10.5f, q.VX, 4);
        Assert.Equal(p.DirZ * 10.5f, q.VZ, 4);
    }
}
