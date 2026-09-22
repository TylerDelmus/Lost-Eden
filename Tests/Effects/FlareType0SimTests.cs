using Xunit;

/// <summary>
/// Locks <see cref="FlareType0Sim"/> to stock _GfxControlFlare_t + GfxVisualFlareType0, with the
/// fields of 46000 — the left-hand flare of nano cast 46116 (Spell1).
/// </summary>
public class FlareType0SimTests
{
    static float Bits(int v) => System.BitConverter.Int32BitsToSingle(v);

    /// <summary>46000 as stored in gfxtweak.bin.</summary>
    static float[] Fields46000() => new[]
    {
        Bits(0x707), 0f, 0f, 0f, 0f, 0f, 0f, Bits(2001), -1f, Bits(15),
        16f, 0f, 0.05f, 0.1f, 1f, 1f, 1f, 0.5f, 0.4f, 1f,
        0f, 0.5f, 0.4f, 1f, Bits(0), 0f, 6.28319f, -1.5708f, 1.5708f, 0.25f,
        0.65f, Bits(1), -0.3f, 1f, 0.5f, 1f,
    };

    /// <summary>Every sprite: angle a = 0, angle b = 0, length 0.25, life 0.5.</summary>
    static FlareType0Sim Deterministic()
    {
        float[] seq = { 0f, 0.5f, 0f, 0f };
        int i = 0;
        return new FlareType0Sim(Fields46000(), 0, 0, () => seq[i++ % seq.Length], () => 0);
    }

    [Fact]
    public void Pool_IsRateTimesOnePointFiveTimesLife_AtLeastTheBurst()
    {
        Assert.Equal(24, Deterministic().Sprites.Length); // max(_ftol(16 * 1.5 * 1), 1)
    }

    [Fact]
    public void FirstCall_ReleasesTheBurst_ThenFollowsRateTimesAge()
    {
        FlareType0Sim s = Deterministic();
        s.Step(0f, 0f, 0f, 0f, 0f, null);
        Assert.Equal(1, s.LiveCount); // counter starts at -field31

        s.Step(0.25f, 0.25f, 0f, 0f, 0f, null);
        Assert.Equal(5, s.LiveCount); // + _ftol(16 * 0.25)
    }

    [Fact]
    public void Segment_EndsSlideApartAlongOneDirection_SwappedByFlag0x100()
    {
        FlareType0Sim s = Deterministic();
        s.Step(0f, 0f, 0f, 0f, 0f, null);
        s.Step(0.25f, 0.25f, 0f, 0f, 0f, null);

        // d = (0.25, 0, 0); flag 0x100 gives P1 field 33 (1) and P2 field 32 (-0.3), over a 0.5 s life.
        FlareType0Visual.Sprite first = s.Sprites[0];
        Assert.Equal(0.125f, first.P1x, 5);
        Assert.Equal(-0.0375f, first.P2x, 5);
        Assert.Equal(0f, first.P1y, 5);
        Assert.Equal(0f, first.P1z, 5);
        Assert.Equal(0.075f, first.Size0, 5); // 0.05 -> 0.1 over the life, half way
        Assert.Equal(0.25f, first.Life, 5);
    }

    [Fact]
    public void Spell1Colours_RampAlphaAndPackWithFistp()
    {
        FlareType0Sim s = Deterministic();
        s.SetStartColor(1f, 0.45f, 0.4f, 0.5f); // 46116 fields 10-13
        s.SetStopColor(0f, 0.45f, 0.4f, 0.5f);  // 46116 fields 14-17
        s.Step(0f, 0f, 0f, 0f, 0f, null);
        s.Step(0.25f, 0.25f, 0f, 0f, 0f, null);

        // Half way: A .5 -> 127, R .45 -> 114, G .4 -> 102, B .5 -> 127.
        Assert.Equal(0x7F72667Fu, s.Sprites[0].Argb);
    }

    [Fact]
    public void Terminate_StopsSpawning_AndReadiesWhenThePoolEmpties()
    {
        FlareType0Sim s = Deterministic();
        float age = 0f;
        s.Step(age, 0f, 0f, 0f, 0f, null);
        for (int n = 0; n < 30; n++)
        {
            age += 1f / 30f;
            s.Step(age, 1f / 30f, 0f, 0f, 0f, null);
        }

        s.TerminateGracefully(age);
        bool ready = false;
        int calls = 0;
        while (!ready && calls < 100)
        {
            age += 1f / 30f;
            ready = s.Step(age, 1f / 30f, 0f, 0f, 0f, null);
            calls++;
        }

        Assert.True(ready);
        Assert.InRange(calls, 14, 16); // the youngest sprite's 0.5 s
    }

    [Fact]
    public void WorldMode_RotatesTheDirectionByTheLocator()
    {
        FlareType0Sim s = Deterministic();
        // 90 degrees about Y as stock rows: x -> -z.
        float[] rot = { 0f, 0f, -1f, 0f, 1f, 0f, 1f, 0f, 0f };
        s.Step(0f, 0f, 10f, 0f, 0f, rot);
        s.Step(0.25f, 0.25f, 10f, 0f, 0f, rot);

        FlareType0Visual.Sprite first = s.Sprites[0];
        Assert.Equal(10f, first.P1x, 4);
        Assert.Equal(-0.125f, first.P1z, 4);
    }

    [Fact]
    public void Quad_RunsFromP1ToP2_ExtendedAndWidenedBySize0()
    {
        var c = new float[12];
        // Camera at origin looking down +z, right = +x, up = +y; points in front of it.
        FlareType0Visual.SpriteQuad(
            0f, 0f, 5f, 1f, 0f, 5f,
            0f, 0f, 5f, 1f, 0f, 5f,
            1f, 0f, 0f, 0f, 1f, 0f,
            0.1f, c);

        Assert.Equal(new[] { -0.1f, 0.1f, 5f }, new[] { c[0], c[1], c[2] });
        Assert.Equal(new[] { -0.1f, -0.1f, 5f }, new[] { c[3], c[4], c[5] });
        Assert.Equal(new[] { 1.1f, 0.1f, 5f }, new[] { c[6], c[7], c[8] });
        Assert.Equal(new[] { 1.1f, -0.1f, 5f }, new[] { c[9], c[10], c[11] });
    }

    [Fact]
    public void Quad_WithBothEndsOnOnePixel_FallsBackToTheCameraAxes()
    {
        var c = new float[12];
        FlareType0Visual.SpriteQuad(
            0f, 0f, 5f, 0f, 0f, 5f,
            0f, 0f, 5f, 0f, 0f, 5f,
            1f, 0f, 0f, 0f, 1f, 0f,
            0.1f, c);

        // A = right, B = up: a 0.2 square.
        Assert.Equal(-0.1f, c[0], 5);
        Assert.Equal(-0.1f, c[1], 5);
        Assert.Equal(0.1f, c[9], 5);
        Assert.Equal(0.1f, c[10], 5);
    }
}
