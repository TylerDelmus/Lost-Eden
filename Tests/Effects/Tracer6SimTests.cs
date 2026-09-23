using System;
using Xunit;

/// <summary>
/// Locks <see cref="Tracer6Sim"/> to stock <c>_GfxControlTracer6_t</c> (loader <c>10100c93</c>, init
/// <c>101014ff</c>, Process <c>10101872</c> -> update <c>1010107d</c>), using record 70000, the tracer
/// of nano 294977 Michizure's Decuple and the only record of this type.
/// </summary>
public class Tracer6SimTests
{
    static float Bits(int i) => BitConverter.Int32BitsToSingle(i);

    /// <summary>
    /// Record 70000: flags 2 (local), duration -1, material 11, speed 30, spacing 0.5, sprite size
    /// 0.125, colour A (1, 0.25, 0.25, 1) blue, no size gain, growth 4, 3 links, ribbon speed 50,
    /// ribbon length 5, width 0.125, colour B (1, 1, 0.25, 0.25) red.
    /// </summary>
    static float[] Record70000()
    {
        var f = new float[27];
        f[0] = Bits(2);
        f[8] = -1f;
        f[9] = Bits(11);
        f[10] = 30f;
        f[11] = 0.5f;
        f[12] = 0.125f;
        f[13] = 1f; f[14] = 0.25f; f[15] = 0.25f; f[16] = 1f;
        f[17] = 0f;
        f[18] = 4f;
        f[19] = Bits(3);
        f[20] = 50f;
        f[21] = 5f;
        f[22] = 0.125f;
        f[23] = 1f; f[24] = 1f; f[25] = 0.25f; f[26] = 0.25f;
        return f;
    }

    static Tracer6Sim Make(float length = 60f) =>
        new Tracer6Sim(Record70000(), 0f, 0f, 0f, 0f, 0f, length);

    [Fact]
    public void TheLoader_ReadsBothColoursAndEveryField()
    {
        Tracer6Sim sim = Make();
        Assert.True(sim.Local);
        Assert.Equal(-1f, sim.Duration, 4);
        Assert.Equal(30f, sim.Speed, 4);
        Assert.Equal(0.5f, sim.Spacing, 4);
        Assert.Equal(3, sim.LinkCount);
        Assert.Equal(0.125f, sim.RibbonWidth, 4);
        // 13-16 -> A,R,G,B, each FISTP(c * 255 - 0.49999), so 0.25 lands on 63 rather than 64.
        Assert.Equal(0xff3f3fffu, sim.SpriteArgb);   // the sprites are blue
        Assert.Equal(0xffff3f3fu, sim.RibbonArgb);   // the ribbons are red
    }

    [Fact]
    public void ASegmentUnderAHundredth_IsDegenerate()
    {
        var sim = new Tracer6Sim(Record70000(), 0f, 0f, 0f, 0f, 0f, 0.005f);
        Assert.True(sim.Degenerate);
        sim.Step(1f, 0.1f);   // must not throw or move anything
        Assert.Equal(0f, sim.SpriteHead, 4);
    }

    [Fact]
    public void TheSpeedIsCappedAtFiveLengths()
    {
        // 10101618: a 2 m line caps 30 down to 10, so the flight still takes 0.2 s.
        var sim = new Tracer6Sim(Record70000(), 0f, 0f, 0f, 0f, 0f, 2f);
        Assert.Equal(10f, sim.Speed, 4);
        Assert.Equal(60f, Make().Distance, 4);
        Assert.Equal(30f, Make().Speed, 4);
    }

    [Fact]
    public void FourSpacingsPastTheEnd_PullsTheSpacingToAQuarterOfTheLine()
    {
        // 1010162d: 0.5 * 4 = 2 > 1.5, so the spacing becomes 1.5 * 0.24.
        var sim = new Tracer6Sim(Record70000(), 0f, 0f, 0f, 0f, 0f, 1.5f);
        Assert.Equal(1.5f * 0.24f, sim.Spacing, 4);
        // A long line leaves it alone.
        Assert.Equal(0.5f, Make().Spacing, 4);
    }

    [Fact]
    public void TheTrailRunsFromTheTailToASpacingAhead()
    {
        Tracer6Sim sim = Make();
        sim.Step(1f, 0.1f);
        Assert.Equal(30f, sim.SpriteTail, 4);
        Assert.Equal(30.5f, sim.SpriteHead, 4);
    }

    [Fact]
    public void TheHeadIsCappedAtTheLengthAndTheTailNeverGoesNegative()
    {
        Tracer6Sim sim = Make();
        sim.Step(10f, 0.1f);
        Assert.Equal(60f, sim.SpriteHead, 4);
        sim.Step(0f, 0.1f);
        Assert.Equal(0f, sim.SpriteTail, 4);
    }

    [Fact]
    public void WhenTheTrailLands_TheControlBuysItselfASecond()
    {
        // 101010c8: the record's -1 stands until the tail reaches the length at 60 / 30 = 2 s.
        Tracer6Sim sim = Make();
        sim.Step(1f, 0.1f);
        Assert.Equal(-1f, sim.Duration, 4);
        sim.Step(2f, 0.1f);
        Assert.Equal(3f, sim.Duration, 4);
    }

    [Fact]
    public void TheRibbonIsTimedToArriveWithTheTrail()
    {
        // 101011df: a = 50 * (age - (60/30 - 60/50)) = 50 * (age - 0.8), so a hits 60 at age 2.
        Tracer6Sim sim = Make();
        sim.Step(0.8f, 0.1f);
        Assert.Equal(0f, sim.RibbonTail, 3);
        sim.Step(1.4f, 0.1f);
        Assert.Equal(30f, sim.RibbonTail, 3);
        Assert.Equal(35f, sim.RibbonHead, 3);
    }

    [Fact]
    public void TheRibbonHeadIsCappedAtTheLength()
    {
        Tracer6Sim sim = Make();
        sim.Step(1.9f, 0.1f);
        Assert.Equal(60f, sim.RibbonHead, 3);
        Assert.True(sim.RibbonTail < 60f);
    }

    [Fact]
    public void InTheLastSecond_TheRibbonsCollapseAndGoColourless()
    {
        // 10101225 / 10101463: while age < duration both ends are zero and the links lose their colour.
        Tracer6Sim sim = Make();
        sim.Step(2f, 0.1f);          // lands, duration becomes 3
        Assert.Equal(3f, sim.Duration, 4);
        sim.Step(2.5f, 0.1f);
        Assert.Equal(0f, sim.RibbonTail, 4);
        Assert.Equal(0f, sim.RibbonHead, 4);
        Assert.False(sim.RibbonsVisible);
    }

    [Fact]
    public void TheThreeLinksAreTheHeadTheTailAndTheOrigin()
    {
        Tracer6Sim sim = Make();
        sim.Step(1.4f, 0.1f);
        float[] p = sim.LinkPositions;
        // Local mode, so the frame's origin is zero and the line runs up local y.
        Assert.Equal(sim.RibbonHead, p[1], 3);
        Assert.Equal(sim.RibbonTail, p[4], 3);
        Assert.Equal(0f, p[7], 4);
        Assert.Equal(0f, p[0], 4);
        Assert.Equal(0f, p[2], 4);
    }

    [Fact]
    public void SpritesAreSpawnedEverySpacingUpToTheHead()
    {
        Tracer6Sim sim = Make();
        sim.Step(0.1f, 0.1f);
        // head = 30 * 0.1 + 0.5 = 3.5, spacing 0.5 -> cursor 0, 0.5 .. 3.5 = 8 sprites.
        Assert.Equal(8, CountAlive(sim));
        Assert.Equal(4f, sim.Cursor, 4);
    }

    [Fact]
    public void ASpawnedSpriteStartsAtFullLifeOnTheSpawnFrame()
    {
        Tracer6Sim sim = Make();
        sim.Step(0f, 0f);
        Tracer6Sim.Sprite s = First(sim);
        Assert.Equal(1f, s.Life, 4);
        Assert.Equal(Tracer6Sim.SpawnFrame, s.Frame);
        // size = cursor * gain + base, and this record has no gain.
        Assert.Equal(0.125f, s.Width, 4);
        Assert.Equal(0.125f, s.Height, 4);
        Assert.Equal(0f, s.Y, 4);
    }

    [Fact]
    public void TheSizeGainWidensSpritesFurtherAlongTheLine()
    {
        float[] f = Record70000();
        f[17] = 0.1f;   // gain
        f[18] = 0f;     // no growth, so the spawn size is what we read back
        var sim = new Tracer6Sim(f, 0f, 0f, 0f, 0f, 0f, 60f);
        sim.Step(0.1f, 0f);
        // The sprite at cursor 3.5 is 3.5 * 0.1 + 0.125 wide.
        bool found = false;
        foreach (Tracer6Sim.Sprite s in sim.Sprites)
            if (s.Alive && Math.Abs(s.Y - 3.5f) < 1e-3f)
            {
                Assert.Equal(3.5f * 0.1f + 0.125f, s.Width, 4);
                found = true;
            }
        Assert.True(found, "no sprite reached the head of the trail");
    }

    [Fact]
    public void ASpriteGrowsFadesAndWalksItsAtlasCells()
    {
        Tracer6Sim sim = Make();
        sim.Step(0f, 0f);
        sim.Step(0.1f, 0.25f);
        Tracer6Sim.Sprite s = First(sim);
        // 10101103: size += growth * dt, life -= dt.
        Assert.Equal(0.125f + 4f * 0.25f, s.Width, 4);
        Assert.Equal(0.75f, s.Life, 4);
        // 1010118e: frame = ftol((1 - life) * 30 + 33).
        Assert.Equal((int)((1f - 0.75f) * 30f + 33f), s.Frame);
    }

    [Fact]
    public void ASpritesLifeNeverGoesNegative()
    {
        Tracer6Sim sim = Make();
        sim.Step(0f, 0f);
        sim.Step(0.1f, 5f);
        foreach (Tracer6Sim.Sprite s in sim.Sprites)
            Assert.True(s.Life >= 0f);
    }

    [Fact]
    public void TheAtlasCellIsRowMajorInAnEightByEightSheet()
    {
        Tracer6Sim.AtlasCell(0, out int c, out int r);
        Assert.Equal(0, c);
        Assert.Equal(0, r);
        Tracer6Sim.AtlasCell(33, out c, out r);
        Assert.Equal(1, c);
        Assert.Equal(4, r);
        Tracer6Sim.AtlasCell(63, out c, out r);
        Assert.Equal(7, c);
        Assert.Equal(7, r);
    }

    static int CountAlive(Tracer6Sim sim)
    {
        int n = 0;
        foreach (Tracer6Sim.Sprite s in sim.Sprites)
            if (s.Alive)
                n++;
        return n;
    }

    static Tracer6Sim.Sprite First(Tracer6Sim sim)
    {
        foreach (Tracer6Sim.Sprite s in sim.Sprites)
            if (s.Alive)
                return s;
        throw new InvalidOperationException("no sprite was spawned");
    }
}
