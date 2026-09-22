using System;
using Xunit;

/// <summary>
/// Locks <see cref="SunsSim"/> to stock _GfxControlSuns_t sunType 4 (<c>100fc48b</c>), with 17000 (half of
/// nano 56213's tracer): size 0.4, frame scale 15.99, life 300 ms, duration 1.
/// </summary>
public class SunsSimTests
{
    const float Step = 1f / 30f;

    static float Bits(int v) => BitConverter.Int32BitsToSingle(v);

    static float[] Fields17000() => new[]
    {
        Bits(5), 0f, 0f, 0f, 0f, 0f, 0f, Bits(1000), -1f, Bits(2),
        Bits(4), 0.4f, 0.15f, 4f, 3f, 1f, 3f, 3f, 1f, 0.3f,
        0.3f, 1f, 1f, 0.3f, 0.3f, 1f, 1f, 0f, 0.4f, 15.99f,
        Bits(300), Bits(6),
    };

    static SunsSim Make(int along) => new SunsSim(Fields17000(), () => along);

    static void StepAlongX(SunsSim s, float age, bool found = true)
        => s.Step(age, found, 0f, 1f, 0f, 10f, 1f, 0f);

    [Fact]
    public void Loader_ReadsTypeDurationAndLife()
    {
        SunsSim s = Make(0);
        Assert.Equal(SunsSim.SparkLineType, s.SunType);
        Assert.Equal(1f, s.Duration);
        Assert.Equal(0.3f, s.Life);
    }

    [Fact]
    public void Spawns_ThreeACall_StillOnTheLine()
    {
        SunsSim s = Make(8192); // q = 0.25
        StepAlongX(s, 0f);
        Assert.Equal(3, s.LastCount);
        StepAlongX(s, Step);
        Assert.Equal(6, s.LastCount);
        SunsSim.Sprite p = s.Sprites[0];
        Assert.True(p.Visible);
        Assert.Equal(2.5f, p.X, 5);
        Assert.Equal(1f, p.Y, 5);
        StepAlongX(s, 2 * Step);
        Assert.Equal(2.5f, s.Sprites[0].X, 5);
    }

    [Fact]
    public void Sprite_TakesSizeFrameColourAndSlotAngle()
    {
        SunsSim s = Make(0);
        StepAlongX(s, 0f);
        StepAlongX(s, Step);
        SunsSim.Sprite p = s.Sprites[2];
        float t = (float)(((double)Step + 0.3f - 0.3f) / 0.3f);
        Assert.Equal(0.4f, p.Width);
        Assert.Equal(0.4f, p.Height);
        Assert.Equal(15 - (int)(15.99f * (double)t), p.Frame);
        Assert.Equal((float)(2 * 0.30000001192092896), p.Angle);
        Assert.Equal(StockColorRamp.Eval(new[] { 1f, 0.3f, 0.3f, 1f }, new[] { 1f, 0.3f, 0.3f, 1f }, t), p.Argb);
    }

    [Fact]
    public void Angle_CyclesEverySevenSlots()
    {
        SunsSim s = Make(0);
        float age = 0f;
        StepAlongX(s, age);
        for (int n = 0; n < 4; n++)
        {
            age += Step;
            StepAlongX(s, age);
        }
        Assert.Equal(s.Sprites[0].Angle, s.Sprites[7].Angle);
        Assert.Equal(0f, s.Sprites[7].Angle);
    }

    [Fact]
    public void WithoutAHitLocation_NothingSpawns_AndTerminatingDrains()
    {
        SunsSim none = Make(0);
        StepAlongX(none, 0f, found: false);
        Assert.Equal(0, none.LastCount);

        SunsSim s = Make(0);
        StepAlongX(s, 0f);
        s.Terminating = true;
        for (int n = 1; n < 12; n++)
            StepAlongX(s, n * Step);
        Assert.True(s.Drained);
    }
}
