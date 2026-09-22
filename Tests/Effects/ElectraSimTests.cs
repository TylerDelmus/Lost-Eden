using System;
using Xunit;

/// <summary>
/// Locks <see cref="ElectraSim"/> to stock _GfxControlElectra_t mode 1 (<c>100da0c0</c>), with 43452
/// (nano 150501's Nullity Sphere buff): size 0.7, radius 0.7, spark life 500 ms, duration 6.
/// </summary>
public class ElectraSimTests
{
    const float Step = 1f / 30f;

    static float Bits(int v) => BitConverter.Int32BitsToSingle(v);

    static float[] Fields43452() => new[]
    {
        Bits(5), 0f, 0f, 0f, 0f, 0f, 0f, Bits(1000), -1f, Bits(2),
        Bits(1), 0.4f, 0.15f, 4f, 3f, 1f, 3f, 3f, 1f, 0.66f,
        0.5f, 0.31f, 0.6f, 0.99f, 0.54f, 0f, 6f, 0f, 0.7f, 0.7f,
        Bits(500), Bits(0),
    };

    static ElectraSim Make(uint seed = 1) => new ElectraSim(Fields43452(), new StarsCase3.MsvcRand(seed).Next);

    static int Visible(ElectraSim s)
    {
        int n = 0;
        foreach (ElectraSim.Sprite sprite in s.Sprites)
            if (sprite.Visible)
                n++;
        return n;
    }

    static float Dot(float ax, float ay, float az, float bx, float by, float bz) => ax * bx + ay * by + az * bz;

    [Fact]
    public void Loader_ReadsModeDurationAndLife()
    {
        ElectraSim s = Make();
        Assert.Equal(ElectraSim.ShellMode, s.Mode);
        Assert.Equal(6f, s.Duration);
        Assert.Equal(0.5f, s.Life);
    }

    [Fact]
    public void Spawns_FollowTheOwedCount_AtMostTwoACall()
    {
        ElectraSim s = Make();
        s.Step(0f, 0f, 0f, 0f);
        Assert.Equal(0, Visible(s)); // _ftol(56 * 0) = 0 owed

        s.Step(Step, 0f, 0f, 0f);    // 1 owed: slot 0 opens, its record untouched
        Assert.Equal(0, Visible(s));

        s.Step(2 * Step, 0f, 0f, 0f); // 3 owed: slot 0 draws, slots 1 and 2 open
        Assert.Equal(1, Visible(s));
        s.Step(3 * Step, 0f, 0f, 0f);
        Assert.Equal(3, Visible(s));

        ElectraSim jump = Make();
        jump.Step(0f, 0f, 0f, 0f);
        jump.Step(1f, 0f, 0f, 0f);    // 56 owed, but only 2 may open
        jump.Step(1f + Step, 0f, 0f, 0f);
        Assert.Equal(2, Visible(jump));
    }

    [Fact]
    public void Spark_LiesOnTheStretchedShell_FacingOutward()
    {
        ElectraSim s = Make(7);
        s.Step(0f, 0f, 0f, 0f);
        s.Step(Step, 10f, 20f, 30f);
        s.Step(2 * Step, 10f, 20f, 30f);
        ElectraSim.Sprite p = s.Sprites[0];
        Assert.True(p.Visible);

        // Offset = d * 0.7 with d a unit vector whose y was stretched by 1.5.
        float ox = p.X - 10f, oy = p.Y - 20f, oz = p.Z - 30f;
        float ex = ox / 0.7f, ey = oy / 1.05f, ez = oz / 0.7f;
        Assert.Equal(1f, ex * ex + ey * ey + ez * ez, 4);

        // Both axes are 0.7 long, square to each other and to the stretched direction.
        Assert.Equal(0.7f, (float)Math.Sqrt(Dot(p.Ax, p.Ay, p.Az, p.Ax, p.Ay, p.Az)), 4);
        Assert.Equal(0.7f, (float)Math.Sqrt(Dot(p.Cx, p.Cy, p.Cz, p.Cx, p.Cy, p.Cz)), 4);
        Assert.Equal(0f, Dot(p.Ax, p.Ay, p.Az, p.Cx, p.Cy, p.Cz), 4);
        Assert.Equal(0f, Dot(p.Ax, p.Ay, p.Az, ox, oy, oz), 4);
        Assert.Equal(0f, Dot(p.Cx, p.Cy, p.Cz, ox, oy, oz), 4);
    }

    [Fact]
    public void Spark_FollowsTheLocator()
    {
        ElectraSim s = Make(3);
        s.Step(0f, 0f, 0f, 0f);
        s.Step(Step, 0f, 0f, 0f);
        s.Step(2 * Step, 0f, 0f, 0f);
        float x0 = s.Sprites[0].X;
        s.Step(3 * Step, 5f, 0f, 0f);
        Assert.Equal(x0 + 5f, s.Sprites[0].X, 5);
    }

    [Fact]
    public void Frame_AndColour_FollowLifeAndProgress()
    {
        ElectraSim s = Make();
        s.Step(0f, 0f, 0f, 0f);
        s.Step(Step, 0f, 0f, 0f);
        s.Step(2 * Step, 0f, 0f, 0f);
        ElectraSim.Sprite p = s.Sprites[0];

        // Spawned at 1/30 with a 0.5 s life: t = 1 - (0.5333 - 0.0667) / 0.5 = 0.0667, frame _ftol(1.07).
        Assert.Equal(1, p.Frame);
        float[] start = { 1f, 0.66f, 0.5f, 0.31f }, end = { 0.6f, 0.99f, 0.54f, 0f };
        Assert.Equal(StockColorRamp.Eval(start, end, 2 * Step / 6f), p.Argb);
    }

    [Fact]
    public void SteadyState_HoldsAbout28Sparks()
    {
        ElectraSim s = Make();
        float age = 0f;
        s.Step(age, 0f, 0f, 0f);
        for (int n = 0; n < 90; n++)
        {
            age += Step;
            s.Step(age, 0f, 0f, 0f);
        }

        // 56 a second, 0.5 s each.
        Assert.InRange(Visible(s), 26, 30);
    }

    [Fact]
    public void Expiry_AddsOneSparkLife_ThenEnds()
    {
        ElectraSim s = Make();
        Assert.False(s.Expire(6f));
        Assert.False(s.Expire(6.01f));
        Assert.True(s.Terminating);
        Assert.Equal(6.5f, s.Duration, 5);
        Assert.False(s.Expire(6.5f));
        Assert.True(s.Expire(6.51f));
    }

    [Fact]
    public void Terminating_StopsSpawning_AndTheSparksRunOut()
    {
        ElectraSim s = Make();
        float age = 0f;
        s.Step(age, 0f, 0f, 0f);
        for (int n = 0; n < 45; n++)
        {
            age += Step;
            s.Step(age, 0f, 0f, 0f);
        }
        Assert.True(Visible(s) > 20);

        s.Terminating = true;
        for (int n = 0; n < 16; n++)
        {
            age += Step;
            s.Step(age, 0f, 0f, 0f);
        }
        Assert.Equal(0, Visible(s));
    }

    [Fact]
    public void RandomUnitVector_IsUnitLength()
    {
        var rand = new StarsCase3.MsvcRand(11);
        for (int i = 0; i < 200; i++)
        {
            ElectraSim.RandomUnitVector(rand.Next, out float x, out float y, out float z);
            Assert.Equal(1f, x * x + y * y + z * z, 4);
        }
    }
}
