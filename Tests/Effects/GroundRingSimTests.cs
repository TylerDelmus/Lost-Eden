using System;
using Xunit;

/// <summary>
/// Locks <see cref="GroundRingSim"/> to stock <c>_GfxControlGroundRing_t</c>
/// (<c>Gamecode 100e1c7e</c>) and <c>GfxVisualGroundRing::Update</c>
/// (<c>DisplaySystem 10016e84</c>), using the shipped records 60005 (follows) and 61101 (baked).
/// </summary>
public class GroundRingSimTests
{
    static float I(int v) => BitConverter.Int32BitsToSingle(v);
    static float U(uint v) => BitConverter.Int32BitsToSingle(unchecked((int)v));

    /// <summary>Record 60005: flags 0x303, 10 s, 16 segments, radii 0/8, fades 0.5/1, white + blue.</summary>
    static float[] R60005()
    {
        var f = new float[20];
        f[0] = I(0x303);
        f[8] = 10f;
        f[9] = I(34);
        f[10] = I(16);
        f[11] = 15f;        // u span
        f[12] = -1f;        // v span
        f[13] = 0f;         // inner radius
        f[14] = 8f;         // outer radius
        f[15] = 0.5f;       // fade in
        f[16] = 1f;         // fade out
        f[17] = U(0xffffffff);
        f[18] = U(0x000000ff);
        f[19] = 0.25f;      // height above the ground
        return f;
    }

    /// <summary>Record 61101: flags 0xb03, so it is baked to the ground at spawn.</summary>
    static float[] R61101()
    {
        float[] f = R60005();
        f[0] = I(0xb03);
        f[8] = 6.2f;
        f[13] = 0f;
        f[14] = 6f;
        f[15] = 0.6f;
        f[16] = 0.3f;
        return f;
    }

    static GroundRingSim Make(float[] fields = null) => new GroundRingSim(fields ?? R60005());

    [Fact]
    public void TheRingIsTwoNPlusTwoPoints()
    {
        // 100e21a0 allocates (2 * field10 + 2) vec3s; Update writes back the same count.
        GroundRingSim r = Make();
        Assert.Equal(16, r.Segments);
        Assert.Equal(34, r.PointCount);
        Assert.Equal(34 * 3, r.Points.Length);
    }

    [Fact]
    public void TheLoaderWorksOutWhereTheFadeOutStarts()
    {
        // 100e21df: duration - field 16.
        Assert.Equal(9f, Make().FadeOutStart, 4);
        Assert.Equal(5.9f, Make(R61101()).FadeOutStart, 4);
    }

    [Fact]
    public void PairsAlternateInnerThenOuter()
    {
        GroundRingSim r = Make();
        r.Build(0f, 0f, 0f);
        r.Step(1f, true, 0f, 0f, 0f);

        // The first pair is at angle 0, where x is the radius and z nothing: stock takes x from the
        // cosine and z from the sine.
        Assert.Equal(0f, r.Points[0], 4);        // inner, radius 0
        Assert.Equal(0f, r.Points[2], 4);
        Assert.Equal(8f, r.Points[3], 4);        // outer, radius 8
        Assert.Equal(0f, r.Points[5], 4);

        // A quarter of the way round, the outer point is on +z.
        int quarter = (r.Segments / 4) * 6;
        Assert.Equal(0f, r.Points[quarter + 3], 3);
        Assert.Equal(8f, r.Points[quarter + 5], 3);
    }

    [Fact]
    public void TheLastPairClosesOntoTheFirst()
    {
        GroundRingSim r = Make();
        r.Build(0f, 0f, 0f);
        r.Step(1f, true, 0f, 0f, 0f);
        int last = (r.PointCount - 2) * 3;
        Assert.Equal(r.Points[3], r.Points[last + 3], 3);
        Assert.Equal(r.Points[5], r.Points[last + 5], 3);
    }

    [Fact]
    public void EachPointSitsOnTheGroundPlusFieldNineteen()
    {
        // 100e1ea4: ground + field 19, stored relative to the centre the visual is placed at.
        GroundRingSim r = Make();
        r.GroundHeight = (x, z) => x;      // a ramp running up +x
        r.Build(0f, 5f, 0f);
        r.Step(1f, true, 0f, 5f, 0f);

        // The outer point at angle 0 is at x = 8, so the ground there is 8.
        Assert.Equal(8f + 0.25f - 5f, r.Points[4], 3);
        // Its opposite is at x = -8.
        int half = (r.Segments / 2) * 6;
        Assert.Equal(-8f + 0.25f - 5f, r.Points[half + 4], 3);
    }

    [Fact]
    public void WithoutAGroundProviderTheRingIsFlat()
    {
        GroundRingSim r = Make();
        r.Build(0f, 3f, 0f);
        r.Step(1f, true, 0f, 3f, 0f);
        for (int i = 0; i < r.PointCount; i++)
            Assert.Equal(0.25f, r.Points[i * 3 + 1], 4);   // just field 19 above the centre
    }

    [Fact]
    public void TheAlphaRampsUpOverFieldFifteen()
    {
        // 100e1cda: only the alpha moves; the colour itself is the record's.
        GroundRingSim r = Make();
        r.Build(0f, 0f, 0f);
        r.Step(0.25f, true, 0f, 0f, 0f);       // half of the 0.5 s fade-in
        Assert.Equal(0x80u, r.CurrentA >> 24);
        Assert.Equal(0xffffffu, r.CurrentA & 0xffffffu);
        // 60005's field 18 is 0x000000ff — the OUTER rim's alpha is already nothing, so the band
        // fades away toward its outer edge and the ramp has nothing to move.
        Assert.Equal(0u, r.CurrentB >> 24);
        Assert.Equal(0x0000ffu, r.CurrentB & 0xffffffu);
    }

    [Fact]
    public void TheMiddleIsTheRecordsOwnColoursExactly()
    {
        // 100e1d50: no interpolation at all through the middle.
        GroundRingSim r = Make();
        r.Build(0f, 0f, 0f);
        r.Step(5f, true, 0f, 0f, 0f);
        Assert.Equal(0xffffffffu, r.CurrentA);
        Assert.Equal(0x000000ffu, r.CurrentB);
    }

    [Fact]
    public void TheAlphaRampsDownOverFieldSixteen()
    {
        // 100e1d6b: starts at duration - field 16.
        GroundRingSim r = Make();
        r.Build(0f, 0f, 0f);
        r.Step(9.5f, true, 0f, 0f, 0f);        // half of the 1 s fade-out
        Assert.Equal(0x80u, r.CurrentA >> 24);
        r.Step(9.99f, true, 0f, 0f, 0f);
        Assert.InRange(r.CurrentA >> 24, 0u, 3u);
    }

    [Fact]
    public void ABakedRingNeverMovesAgain()
    {
        // 100e1dee: with flag 0x800 Process jumps straight to the visual update.
        GroundRingSim r = Make(R61101());
        Assert.True(r.Baked);
        r.Build(0f, 0f, 0f);
        float before = r.Points[3];

        r.Step(1f, true, 100f, 50f, 100f);     // the locator has run off
        Assert.Equal(0f, r.CentreX, 4);        // the centre stayed where it was built
        Assert.Equal(before, r.Points[3], 4);
    }

    [Fact]
    public void AnUnbakedRingFollowsItsLocator()
    {
        GroundRingSim r = Make();
        Assert.False(r.Baked);
        r.Build(0f, 0f, 0f);
        r.Step(1f, true, 10f, 2f, -4f);
        Assert.Equal(10f, r.CentreX, 4);
        Assert.Equal(2f, r.CentreY, 4);
        Assert.Equal(-4f, r.CentreZ, 4);
    }

    [Fact]
    public void AnUnbakedRingReSamplesTheGroundAsItMoves()
    {
        GroundRingSim r = Make();
        r.GroundHeight = (x, z) => x * 0.5f;
        r.Build(0f, 0f, 0f);
        r.Step(1f, true, 0f, 0f, 0f);
        float atOrigin = r.Points[4];

        r.Step(2f, true, 20f, 0f, 0f);
        float movedOut = r.Points[4];
        Assert.NotEqual(atOrigin, movedOut);
        // The outer point is now at x = 28, so the ground under it is 14.
        Assert.Equal(14f + 0.25f - 0f, movedOut, 3);
    }

    [Fact]
    public void ADeadLocatorEndsItUnlessFlagOneThousandIsSet()
    {
        // 100e1c98. 60005's 0x303 has no 0x1000; 61071's 0x1f03 does.
        GroundRingSim plain = Make();
        plain.Build(0f, 0f, 0f);
        plain.Step(1f, false, 0f, 0f, 0f);
        Assert.True(plain.Dead);

        float[] f = R61101();
        f[0] = I(0x1f03);
        var tough = new GroundRingSim(f);
        tough.Build(0f, 0f, 0f);
        tough.Step(1f, false, 0f, 0f, 0f);
        Assert.False(tough.Dead);
        Assert.True(tough.OutlivesLocator);
    }

    [Fact]
    public void EveryShippedRecordWalksTheUvRoundTheRing()
    {
        // 10016ea0 is cmp/je on zero, so the SET bit falls through to the ring walk at 10016eac.
        // Bit 8 is set on 0x303, 0xb03 and 0x1f03 alike, so none of them is world-projected.
        Assert.True(Make().RingWalkUv);
        Assert.True(Make(R61101()).RingWalkUv);
        Assert.Equal(15f, Make().USpan, 4);
        Assert.Equal(-1f, Make().VSpan, 4);
    }
}
