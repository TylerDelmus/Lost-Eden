using System;
using Xunit;

/// <summary>
/// Locks <see cref="StarsLimbSparks"/> to stock _GfxControlStars_t case 22 (<c>100fb299</c>), with 43426
/// (nano 43878's heal hit): life 350 ms, radius 1.6, size 1.1, 9 spawns a call.
/// </summary>
public class StarsLimbSparksTests
{
    const float Step = 1f / 30f;
    static readonly float[] Start = { 1f, 1f, 0.2f, 0.2f };
    static readonly float[] End = { 1f, 1f, 0.3f, 0.3f };

    // Points for AttachIds, in order: LCalf, LThigh, RCalf, RThigh, Pelvis, Head, LForearm, LUpperArm,
    // RForearm, RUpperArm, LUpperArm, RUpperArm.
    static readonly float[] Skeleton =
    {
        -0.1f, 0.5f, 0f,   -0.1f, 0.95f, 0f,
         0.1f, 0.5f, 0f,    0.1f, 0.95f, 0f,
         0f, 1f, 0f,        0f, 1.7f, 0f,
        -0.45f, 1.3f, 0f,  -0.2f, 1.45f, 0f,
         0.45f, 1.3f, 0f,   0.2f, 1.45f, 0f,
        -0.2f, 1.45f, 0f,   0.2f, 1.45f, 0f,
    };

    /// <summary>Every spawn: segment k, then q, then an XZ direction of (1, 0, 0).</summary>
    static StarsLimbSparks Make(int segment, int alongRand, float radius = 1.6f)
    {
        int[] seq = { segment, alongRand, 32767, 16384 };
        int i = 0;
        return new StarsLimbSparks(0.35f, radius, 1.1f, 9, Start, End, () => seq[i++ % seq.Length]);
    }

    [Fact]
    public void Spawn_SitsOnACylinderRoundTheSegment()
    {
        StarsLimbSparks s = Make(2, 16384); // pelvis to head, q = 0.5
        s.Step(0f, Skeleton);
        Assert.Equal(9, s.LastCount);
        s.Step(Step, Skeleton);
        StarsCase3.Sprite p = s.Sprites[0];
        Assert.True(p.Visible);

        // The start was (0, 1.35, 0) plus 1.6 off the vertical axis; one pull later it has moved by
        // (c - p) * 0.01 toward c = P + D * 0.35 (dot / L, not dot / L²).
        float off = (float)Math.Sqrt(p.X * p.X + p.Z * p.Z);
        Assert.Equal(1.6f * 0.99f, off, 3);
        Assert.Equal(1.35f + (1.245f - 1.35f) * 0.01f, p.Y, 4);
    }

    [Fact]
    public void Spark_SettlingOnItsSegment_IsDueAgain()
    {
        StarsLimbSparks s = Make(2, 0, radius: 0f); // spawns exactly on the pelvis point
        s.Step(0f, Skeleton);
        s.Step(Step, Skeleton);
        int serial = s.Sprites[0].Serial;
        Assert.True(s.Sprites[0].Visible);

        s.Step(2 * Step, Skeleton); // timer was set to 0: respawned
        s.Step(3 * Step, Skeleton);
        Assert.NotEqual(serial, s.Sprites[0].Serial);
    }

    [Fact]
    public void Size_Frame_AndColour_FollowLife()
    {
        StarsLimbSparks s = Make(0, 16384);
        s.Step(0f, Skeleton);
        s.Step(Step, Skeleton);
        StarsCase3.Sprite p = s.Sprites[0];

        // t = (0.35 + 1/30 - 0.35) / 0.35.
        float t = (float)((0.35 + (double)Step - 0.35f) / 0.35f);
        Assert.Equal((float)((t * 0.699999988079071 + 0.30000001192092896) * 1.1f), p.Size, 5);
        Assert.Equal((int)(t * 15.99), p.Frame);
        Assert.Equal(StockColorRamp.Eval(Start, End, t), p.Argb);
    }

    [Fact]
    public void Terminating_StopsSpawning_AndDrains()
    {
        StarsLimbSparks s = Make(1, 16384);
        s.Step(0f, Skeleton);
        s.Step(Step, Skeleton);
        s.Terminating = true;
        float age = Step;
        for (int n = 0; n < 15; n++)
        {
            age += Step;
            s.Step(age, Skeleton);
        }
        Assert.True(s.Drained);
    }

    [Fact]
    public void ZeroLengthSegment_StaysFinite()
    {
        var flat = new float[Skeleton.Length];
        StarsLimbSparks s = Make(3, 16384);
        s.Step(0f, flat);
        s.Step(Step, flat);
        StarsCase3.Sprite p = s.Sprites[0];
        Assert.False(float.IsNaN(p.X) || float.IsNaN(p.Y) || float.IsNaN(p.Z));
    }

    [Fact]
    public void RandomUnitXZ_IsFlatAndUnitLength()
    {
        var rand = new StarsCase3.MsvcRand(5);
        for (int i = 0; i < 200; i++)
        {
            StarsLimbSparks.RandomUnitXZ(rand.Next, out float x, out float y, out float z);
            Assert.Equal(0f, y);
            Assert.Equal(1f, x * x + z * z, 4);
        }
    }
}
