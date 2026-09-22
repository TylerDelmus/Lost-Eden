using System;
using Xunit;

/// <summary>
/// Locks <see cref="Tracer1Sim"/> to stock _GfxControlTracer1_t with the fields of 45694, nano
/// 28612's projectile.
/// </summary>
public class Tracer1SimTests
{
    static float Bits(int v) => BitConverter.Int32BitsToSingle(v);

    /// <summary>45694 as stored in gfxtweak.bin.</summary>
    static float[] Fields45694() => new[]
    {
        Bits(0x802), 0f, 0f, 0f, 1.5708f, 0f, 0f, Bits(0), -1f, Bits(16),
        1f, 0.3f, 0.6f, 1f, 0f, 0.3f, 0.6f, 1f, 0.06f, Bits(3),
        Bits(1), 0f, -1.25f, 0.15f, 0f, 0.675f, 0f, 0f, -0.25f, 0.03f,
        0f, 1.155f, 0.06f, 0f, 0.225f, 0.18f, 0f, 1.275f, -0.3f,
    };

    static Tracer1Sim Along(float[] fields, float dx, float dy, float dz)
        => new Tracer1Sim(fields, 1f, 2f, 3f, 1f + dx, 2f + dy, 3f + dz, 0);

    /// <summary>A sprite end as drawn: the visual position plus the point through the visual's rows.</summary>
    static (float x, float y, float z) World(Tracer1Sim s, float px, float py, float pz)
    {
        float[] b = s.Local ? s.Basis : new[] { 1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f };
        return (
            s.VisualX + px * b[0] + py * b[3] + pz * b[6],
            s.VisualY + px * b[1] + py * b[4] + pz * b[7],
            s.VisualZ + px * b[2] + py * b[5] + pz * b[8]);
    }

    [Theory]
    [InlineData(4f, 20f, 0.2f)]    // 5 * dist caps the speed: every flight under 20 m takes 0.2 s
    [InlineData(40f, 100f, 0.4f)]  // beyond that 100 m/s
    public void Speed_IsMinOf100AndFiveTimesTheDistance(float dist, float speed, float flight)
    {
        Tracer1Sim s = Along(Fields45694(), 0f, 0f, dist);
        Assert.Equal(speed, s.Speed, 4);
        Assert.Equal(flight, s.FlightSeconds, 4);
    }

    [Fact]
    public void ShortSegment_ReadiesAtBuild()
    {
        Assert.True(Along(Fields45694(), 0.005f, 0f, 0f).Degenerate);
        Assert.False(Along(Fields45694(), 0.02f, 0f, 0f).Degenerate);
    }

    [Fact]
    public void OneSpritePerLink_InTheTemplateColour()
    {
        Tracer1Sim s = Along(Fields45694(), 0f, 0f, 4f);
        s.Step(0f, 0f);

        Assert.Equal(3, s.Visual.Sprites.Length); // N = 3 links, M = 1 ring
        Assert.Equal(3, s.Visual.LiveCount);
        // A 1, R .3, G .6, B 1 through FISTP(c * 255 - 0.49999).
        Assert.Equal(0xFF4C99FFu, s.Visual.Sprites[0].Argb);
        Assert.Equal(0.06f, s.Visual.Sprites[0].Size0);
    }

    [Fact]
    public void LocalMode_TheBoltLiesAlongTheFlight()
    {
        // Field 4 turns local +Y onto the direction of travel.
        Tracer1Sim s = Along(Fields45694(), 3f, 0f, 4f);
        s.Step(0f, 0f);

        FlareType0Visual.Sprite link0 = s.Visual.Sprites[0];
        var p1 = World(s, link0.P1x, link0.P1y, link0.P1z);
        var p2 = World(s, link0.P2x, link0.P2y, link0.P2z);

        // Link 0 runs from y = -1.25 to y = 0.675: 1.925 along the flight, the head in front.
        float along = (p2.x - p1.x) * s.DirX + (p2.y - p1.y) * s.DirY + (p2.z - p1.z) * s.DirZ;
        Assert.Equal(1.925f, along, 3);
        float head = (p2.x - 1f) * s.DirX + (p2.y - 2f) * s.DirY + (p2.z - 3f) * s.DirZ;
        Assert.Equal(0.675f, head, 3);
    }

    [Fact]
    public void WorldMode_PointsAreNotTurnedByTheLocator()
    {
        float[] f = Fields45694();
        f[0] = Bits(0x800);
        Tracer1Sim s = Along(f, 3f, 0f, 4f);
        s.Step(0f, 0f);

        FlareType0Visual.Sprite link0 = s.Visual.Sprites[0];
        var p2 = World(s, link0.P2x, link0.P2y, link0.P2z);
        Assert.Equal(1f, p2.x, 4);
        Assert.Equal(2.675f, p2.y, 4);
        Assert.Equal(3f, p2.z, 4);
    }

    [Fact]
    public void Flight_MovesTheVisualBySpeedTimesAge_AndArrivesAtTheEnd()
    {
        Tracer1Sim s = Along(Fields45694(), 0f, 0f, 4f); // 20 m/s
        Assert.False(s.Step(0.1f, 0.1f));
        Assert.Equal(1f, s.VisualX, 4);
        Assert.Equal(2f, s.VisualY, 4);
        Assert.Equal(5f, s.VisualZ, 4);

        Assert.True(s.Step(0.25f, 0.15f));
        Assert.Equal(7f, s.VisualZ, 4); // clamped to the end
    }

    [Fact]
    public void Sprites_LastOneSecond()
    {
        Tracer1Sim s = Along(Fields45694(), 0f, 0f, 300f); // 3 s at 100 m/s
        float age = 0f;
        s.Step(age, 0f);
        for (int n = 0; n < 29; n++)
        {
            age += 1f / 30f;
            s.Step(age, 1f / 30f);
        }
        Assert.Equal(3, s.Visual.LiveCount);

        for (int n = 0; n < 2; n++)
        {
            age += 1f / 30f;
            s.Step(age, 1f / 30f);
        }
        Assert.Equal(0, s.Visual.LiveCount);
    }

    [Fact]
    public void Rings_TurnAboutLocalY()
    {
        float[] f = Fields45694();
        f[20] = Bits(2);
        f[21] = 0.1f; f[22] = 0f; f[23] = 0.2f;
        Tracer1Sim s = Along(f, 0f, 0f, 4f);

        Assert.Equal(6, s.Visual.Sprites.Length);
        FlareType0Visual.Sprite ring1 = s.Visual.Sprites[3];
        Assert.Equal(-0.1f, ring1.P1x, 4);
        Assert.Equal(-0.2f, ring1.P1z, 4);
    }

    [Theory]
    [InlineData(0f, 0f, 1f)]
    [InlineData(1f, 0f, 0f)]
    [InlineData(0.6f, 0f, 0.8f)]
    [InlineData(0.57735f, 0.57735f, 0.57735f)]
    public void FindPerpendicular_IsAUnitNormal(float x, float y, float z)
    {
        Tracer1Sim.FindPerpendicular(x, y, z, out float px, out float py, out float pz);
        Assert.Equal(0f, px * x + py * y + pz * z, 4);
        Assert.Equal(1f, px * px + py * py + pz * pz, 4);
    }
}
