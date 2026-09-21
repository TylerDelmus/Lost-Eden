using System;
using Xunit;

/// <summary>
/// Locks the stock Flare/Sparks spawn maths (FUN_100dd0c4 / FUN_100f1997) using the real templates
/// from nano 28608's hit Meta 47388: Stars 43719, Sparks 43720, Flare 43721.
/// </summary>
public class SpriteEmitterMathTests
{
    // Flare 43721: elevation 0..pi/2, azimuth 0..2pi, magnitude 3.5..7.5, speedScale 0, pool 16.
    const float FlareSpeedScale = 0f;
    const int FlarePool = 16;

    /// <summary>
    /// The Spell1 hand-spike rungs are 0.05-0.1 wide by 1.0 long, so they must be classified as
    /// streaks and drawn along their direction of travel. Both rungs are identically shaped — they
    /// differ only in how far they fly — so neither may be treated differently from the other.
    /// </summary>
    [Theory]
    [InlineData(0.05f)]
    [InlineData(0.1f)]
    public void TheHandSpikeRungsAreStreaksAtEveryPointOfTheirWidthAnimation(float width)
    {
        Assert.True(SpriteEmitterMath.IsStreak(width, 1.0f));
    }

    /// <summary>
    /// Cord 8000 runs its length from 0 to 1 at a constant 0.05 width, so it is a streak for most of
    /// its life but has no travel to orient it — that is what keeps it on the screen-space roll.
    /// </summary>
    [Fact]
    public void TheCordBecomesAStreakOnceItHasGrown()
    {
        Assert.False(SpriteEmitterMath.IsStreak(0.05f, 0f));
        Assert.True(SpriteEmitterMath.IsStreak(0.05f, 1f));
    }

    /// <summary>
    /// The two templates already signed off on must stay square-ish so they keep the camera-facing
    /// path: Sparks 43720 is 0.67 square, and the hit Flare 43721 is 1-2 wide by 1 long.
    /// </summary>
    [Theory]
    [InlineData(0.67f, 0.67f)]
    [InlineData(0.4f, 0.4f)]
    [InlineData(1f, 1f)]
    [InlineData(2f, 1f)]
    public void TheApprovedBurstTemplatesAreNotStreaks(float width, float height)
    {
        Assert.False(SpriteEmitterMath.IsStreak(width, height));
    }

    // Sparks 43720: elevation 2.0..-0.062832, azimuth 6.283185..-0.015708,
    // magnitude 2.6..3.6, speedScale 3.2, pool 120, gravity -9.8.
    const float SparksElevA = 2.0f;
    const float SparksElevB = -0.062832f;
    const float SparksAzimA = 6.283185f;
    const float SparksAzimB = -0.015708f;
    const float SparksMagA = 2.6f;
    const float SparksMagB = 3.6f;
    const float SparksSpeedScale = 3.2f;
    const int SparksPool = 120;

    const int MaxSprites = 256;

    /// <summary>
    /// Stock is <c>max(round(rate * 1.5 * life35), field31)</c>, verified against the create at
    /// Gamecode 100f187e. The Spell1 hand rungs both ask for field 31 = 1, so the rate term is what
    /// sizes them.
    /// </summary>
    [Fact]
    public void TheSpell1HandGlowLadderIsSizedByItsRateAndLife()
    {
        // 46002: rate 16, life35 1.0.  46016: rate 64, life35 2.0.
        Assert.Equal(24, SpriteEmitterMath.PoolCapacity(1, 16f, 1f, MaxSprites));
        Assert.Equal(192, SpriteEmitterMath.PoolCapacity(1, 64f, 2f, MaxSprites));
    }

    [Fact]
    public void ABurstTemplateKeepsExactlyItsOwnCount()
    {
        // Flare 43721 and Sparks 43720 both have field 10 = 0, so the rate term vanishes and the
        // max leaves the burst untouched. These two are already signed off.
        Assert.Equal(FlarePool, SpriteEmitterMath.PoolCapacity(FlarePool, 0f, 4f, MaxSprites));
        Assert.Equal(SparksPool, SpriteEmitterMath.PoolCapacity(SparksPool, 0f, 2f, MaxSprites));
    }

    /// <summary>
    /// The hand glow, 8010 and 8011. Stock sizes both with the same formula in the Nano create at
    /// Gamecode FUN_100e7a0e, so a continuous sprayer and a one-shot burst land on very different
    /// pools from the same expression.
    /// </summary>
    [Fact]
    public void TheHandGlowPairSizesFromRateAndBurstRespectively()
    {
        // 8010: rate 150, life35 0.125, burst 1 -> the rate term wins.
        Assert.Equal(28, SpriteEmitterMath.PoolCapacity(1, 150f, 0.125f, MaxSprites));

        // 8011: rate 0, life35 0.5, burst 50 -> nothing to trickle, so the burst wins outright.
        Assert.Equal(50, SpriteEmitterMath.PoolCapacity(50, 0f, 0.5f, MaxSprites));
    }

    /// <summary>
    /// The burst loses to the rate term whenever the rate term is larger, because stock takes the
    /// max of the two rather than adding them.
    /// </summary>
    [Fact]
    public void TheCordHandCoreTakesWhicheverTermIsLarger()
    {
        // Cord 8000/8001: burst 32, rate 60, life35 1.0 -> 90 wins over the burst.
        Assert.Equal(90, SpriteEmitterMath.PoolCapacity(32, 60f, 1f, MaxSprites));

        // Nano1 8011: rate 0, burst 50 -> the burst wins.
        Assert.Equal(50, SpriteEmitterMath.PoolCapacity(50, 0f, 0.5f, MaxSprites));
    }

    /// <summary>
    /// Every Flare rung carries flags 0x707, so bit 0x100 is set and field 33 drives the leading end
    /// while field 32 drives the trailing one. The spike's length is the gap between them times the
    /// magnitude, which is what makes 201937's spikes so much larger than 45680's.
    /// </summary>
    [Theory]
    // record, field32, magnitude, expected reach of the leading point
    [InlineData(46002, -0.3f, 0.35f, 0.35f)]
    [InlineData(46002, -0.3f, 0.85f, 0.85f)]
    [InlineData(46016, -0.9f, 2.1f, 2.1f)]
    [InlineData(46016, -0.9f, 3.1f, 3.1f)]
    public void AFlareSpikeReachesFieldThirtyThreeTimesItsMagnitude(
        int record, float field32, float magnitude, float expected)
    {
        const int flags = 0x707;

        SpriteEmitterMath.SpikeEndScales(flags, field32, 1f, out float endA, out float endB);

        // Field 33 is 1.0 on every rung, so the leading point ends its life exactly one magnitude out.
        Assert.Equal(1f, endA);
        Assert.Equal(field32, endB);
        Assert.Equal(expected, endA * magnitude, 3);
        Assert.True(record > 0);
    }

    /// <summary>
    /// Both ends start together on the emitter, so the separation stock measures its roll from grows
    /// from nothing rather than appearing at full width.
    /// </summary>
    [Fact]
    public void ADrawnSpikeStartsWithNoLengthAtAll()
    {
        SpriteEmitterMath.SpikeEndScales(0x707, -0.9f, 1f, out float endA, out float endB);

        const float life = 2f;
        float atSpawn = MathF.Abs(endA - endB) * (0f / life);
        float halfway = MathF.Abs(endA - endB) * (1f / life);

        Assert.Equal(0f, atSpawn);
        Assert.Equal(0.95f, halfway, 3);
    }

    /// <summary>
    /// The square stock actually draws. Both flare templates animate size0 0.05 -> 0.1, so the quad
    /// runs 0.1 to 0.2 units on a side across the whole 46000..46017 ladder however far the spread
    /// throws it — the ladder buys reach, not size.
    /// </summary>
    [Theory]
    [InlineData(0.05f, 0.1f)]
    [InlineData(0.1f, 0.2f)]
    public void ASpikeIsDrawnAsTwiceItsSizeZero(float size0, float expectedSide)
    {
        Assert.Equal(expectedSide, SpriteEmitterMath.SpikeQuadSize(size0), 4);
    }

    /// <summary>
    /// The roll follows the screen-space heading between a sprite's two projected points. A sprite on
    /// its spawn frame has both points on one pixel and so has no heading to measure.
    /// </summary>
    [Theory]
    [InlineData(1f, 0f, 0f)]
    [InlineData(0f, 1f, 90f)]
    [InlineData(-1f, 0f, 180f)]
    [InlineData(0f, -1f, -90f)]
    [InlineData(1f, 1f, 45f)]
    [InlineData(0f, 0f, 0f)]
    public void ASpikeRollsToItsScreenHeading(float dx, float dy, float expected)
    {
        Assert.Equal(expected, SpriteEmitterMath.SpikeRollDegrees(dx, dy), 3);
    }

    /// <summary>
    /// Reach and size are independent: 46002 scatters over 0.35..0.85 units and 46016 over 2.1..3.1,
    /// yet both share size0 so both draw the same square. That is the property the stretched renderer
    /// violated, and violating it is what turned a heap of small squares at the hand into slivers.
    /// </summary>
    [Fact]
    public void ASpikesSizeIsIndependentOfHowFarItTravels()
    {
        SpriteEmitterMath.SpikeEndScales(0x707, -0.3f, 1f, out float nearA, out float nearB);
        SpriteEmitterMath.SpikeEndScales(0x707, -0.9f, 1f, out float farA, out float farB);

        Assert.True(MathF.Abs(farA - farB) > MathF.Abs(nearA - nearB));
        Assert.Equal(SpriteEmitterMath.SpikeQuadSize(0.1f), SpriteEmitterMath.SpikeQuadSize(0.1f), 4);
    }

    [Fact]
    public void WithoutTheSwapFlagTheEndsTradePlaces()
    {
        SpriteEmitterMath.SpikeEndScales(0, -0.3f, 1f, out float endA, out float endB);

        Assert.Equal(-0.3f, endA);
        Assert.Equal(1f, endB);
    }

    [Fact]
    public void CapacityNeverExceedsTheHardCapOrFallsBelowOne()
    {
        Assert.Equal(MaxSprites, SpriteEmitterMath.PoolCapacity(200, 500f, 10f, MaxSprites));
        Assert.Equal(MaxSprites, SpriteEmitterMath.PoolCapacity(0, 1000f, 1f, MaxSprites));
        Assert.Equal(1, SpriteEmitterMath.PoolCapacity(0, 0f, 0f, MaxSprites));
        Assert.Equal(1, SpriteEmitterMath.PoolCapacity(-5, -1f, -1f, MaxSprites));
        Assert.Equal(1, SpriteEmitterMath.PoolCapacity(0, float.NaN, float.NaN, MaxSprites));
        Assert.Equal(1, SpriteEmitterMath.PoolCapacity(0, float.PositiveInfinity, 1f, MaxSprites));
    }

    [Fact]
    public void TheSpawnCounterStartsAtMinusTheBurstCount()
    {
        // Stock seeds the counter at -field31 so the burst lands while target is still 0.
        Assert.Equal(-1, SpriteEmitterMath.InitialSpawnCounter(1));
        Assert.Equal(-16, SpriteEmitterMath.InitialSpawnCounter(FlarePool));
        Assert.Equal(-120, SpriteEmitterMath.InitialSpawnCounter(SparksPool));
    }

    [Fact]
    public void Flare43721_SpeedScaleZero_PinsEverySpriteToTheLocator()
    {
        // This is why the fire explosion must stack on the locator rather than scatter:
        // field 32 is 0, so no sampled angle or magnitude can move a sprite.
        for (int i = 0; i <= 10; i++)
        {
            float u = i / 10f;
            float elevation = SpriteEmitterMath.Range(0f, MathF.PI / 2f, u);
            float azimuth = SpriteEmitterMath.Range(0f, MathF.PI * 2f, u);
            float magnitude = SpriteEmitterMath.Range(3.5f, 7.5f, u);

            SpriteEmitterMath.SpawnVelocity(
                elevation, azimuth, magnitude, FlareSpeedScale, out float x, out float y, out float z);

            Assert.Equal(0f, x, 6);
            Assert.Equal(0f, y, 6);
            Assert.Equal(0f, z, 6);
        }
    }

    [Fact]
    public void Flare43721_BurstsWholePoolOnFirstFrameThenStops()
    {
        // field10 (emit rate) is 0, counter starts at -pool.
        int counter = SpriteEmitterMath.InitialSpawnCounter(FlarePool);
        Assert.Equal(-FlarePool, counter);

        int spawned = 0;
        int target = SpriteEmitterMath.TargetSpawnCount(emitRate: 0f, age: 0f);
        while (counter < target)
        {
            counter++;
            spawned++;
        }
        Assert.Equal(FlarePool, spawned);

        // Later frames must not emit again.
        foreach (float age in new[] { 0.1f, 1f, 3.9f })
            Assert.Equal(0, SpriteEmitterMath.TargetSpawnCount(0f, age) - counter);
    }

    [Fact]
    public void Sparks43720_BurstsOneHundredTwentySprites()
    {
        int counter = SpriteEmitterMath.InitialSpawnCounter(SparksPool);
        int spawned = 0;
        while (counter < SpriteEmitterMath.TargetSpawnCount(0f, 0f))
        {
            counter++;
            spawned++;
        }
        Assert.Equal(SparksPool, spawned);
    }

    [Fact]
    public void Sparks43720_SpraysAnUpwardConeNotAUniformSphere()
    {
        // Elevation is restricted to [-0.062832, 2.0] rad, so the downward hemisphere is nearly
        // empty. A uniform-sphere spray (the previous behaviour) would fail this.
        float minY = float.MaxValue;
        float maxSpeed = 0f;

        for (int e = 0; e <= 20; e++)
        {
            for (int a = 0; a <= 20; a++)
            {
                float elevation = SpriteEmitterMath.Range(SparksElevA, SparksElevB, e / 20f);
                float azimuth = SpriteEmitterMath.Range(SparksAzimA, SparksAzimB, a / 20f);
                float magnitude = SpriteEmitterMath.Range(SparksMagA, SparksMagB, 0.5f);

                SpriteEmitterMath.SpawnVelocity(
                    elevation, azimuth, magnitude, SparksSpeedScale,
                    out float x, out float y, out float z);

                minY = MathF.Min(minY, y);
                maxSpeed = MathF.Max(maxSpeed, MathF.Sqrt(x * x + y * y + z * z));
            }
        }

        // Steepest downward sample is sin(-0.062832) * 3.1 * 3.2 ~= -0.62 m/s.
        Assert.True(minY > -1f, $"expected an upward cone, but minimum vertical speed was {minY}");
        // Magnitude range 2.6..3.6 scaled by 3.2 stays inside ~11.6 m/s.
        Assert.True(maxSpeed <= 3.6f * 3.2f + 0.01f, $"unexpected speed {maxSpeed}");
    }

    [Theory]
    // Every Sparks (1018) template in gfxtweak.bin, e.g. 43720.
    [InlineData(0x801)]
    // FlareAlt (1006) templates 2000/2003 and 2001/2002/2004.
    [InlineData(0x901)]
    [InlineData(0x1901)]
    // Nano0 (1007) template 8014.
    [InlineData(0xF05)]
    public void TemplatesWithBit0x800AreAlphaBlendedNotGlowing(int flags)
        => Assert.False(SpriteEmitterMath.IsAdditive(flags));

    [Theory]
    // Flare (1005) flag values, including 43721.
    [InlineData(0x201)]
    [InlineData(0x205)]
    [InlineData(0x607)]
    [InlineData(0x707)]
    [InlineData(0x20004)]
    // Stars (2004), Cord (1003), Nano3 (1010), Sprite-family additive values.
    [InlineData(0x005)]
    [InlineData(0x006)]
    [InlineData(0x007)]
    [InlineData(0x1001)]
    public void TemplatesWithoutBit0x800Glow(int flags)
        => Assert.True(SpriteEmitterMath.IsAdditive(flags));

    [Fact]
    public void OnlyBitElevenSelectsTheBlendMode()
    {
        // Neighbouring bits must not leak into the decision.
        Assert.True(SpriteEmitterMath.IsAdditive(0x400));
        Assert.True(SpriteEmitterMath.IsAdditive(0x1000));
        Assert.True(SpriteEmitterMath.IsAdditive(~0x800));
        Assert.False(SpriteEmitterMath.IsAdditive(0x800));
        Assert.False(SpriteEmitterMath.IsAdditive(-1));
    }

    [Theory]
    // 46002 / 46003: the hand cast spikes, 20:1 at spawn and 10:1 at death.
    [InlineData(0.05f, 1f)]
    [InlineData(0.1f, 1f)]
    // Other Flare templates from gfxtweak.bin, 4:1 through 100:1.
    [InlineData(0.24f, 1f)]
    [InlineData(0.01f, 1f)]
    [InlineData(0.5f, 1f)]
    public void ElongatedFlareTemplatesAreStreaks(float width, float height)
    {
        Assert.True(SpriteEmitterMath.IsStreak(width, height));
    }

    [Theory]
    // 43720, the Sparks blood splat: square, and already validated on screen.
    [InlineData(0.67f, 0.67f)]
    [InlineData(0.4f, 0.4f)]
    // 43721, the Flare fire burst: channel 0 grows past channel 1, so never a streak.
    [InlineData(1f, 1f)]
    [InlineData(2f, 1f)]
    // 8010 / 8011, the sustained hand glow.
    [InlineData(0.25f, 0.25f)]
    public void SquareAndWideTemplatesStayScreenAligned(float width, float height)
    {
        Assert.False(SpriteEmitterMath.IsStreak(width, height));
    }

    [Fact]
    public void AStreakNeedsAMeaningfullyTallerHeight()
    {
        // Just under and just over the threshold, so the boundary cannot drift unnoticed.
        Assert.False(SpriteEmitterMath.IsStreak(1f, 1.19f));
        Assert.True(SpriteEmitterMath.IsStreak(1f, 1.21f));
    }

    [Fact]
    public void ADegenerateWidthIsNotAStreak()
    {
        // Guards against dividing the world by a zero-width quad.
        Assert.False(SpriteEmitterMath.IsStreak(0f, 1f));
        Assert.False(SpriteEmitterMath.IsStreak(-1f, 1f));
    }

    [Fact]
    public void ACordLengthOfZeroSurvivesInsteadOfSnappingToFullSize()
    {
        // Cord 8000 runs its length 0 -> 1. Treating the 0 as "missing" made every filament
        // spawn at full length.
        SpriteEmitterMath.ResolveSizeChannels(0.05f, 0f, out float width, out float height);
        Assert.Equal(0.05f, width, 5);
        Assert.Equal(0f, height, 5);
    }

    [Fact]
    public void AnUnusableSizePairFallsBackToUnitSize()
    {
        // Nothing usable at all keeps the old fallback rather than drawing nothing.
        SpriteEmitterMath.ResolveSizeChannels(0f, 0f, out float width, out float height);
        Assert.Equal(1f, width, 5);
        Assert.Equal(1f, height, 5);

        SpriteEmitterMath.ResolveSizeChannels(float.NaN, -3f, out width, out height);
        Assert.Equal(1f, width, 5);
        Assert.Equal(1f, height, 5);
    }

    [Fact]
    public void UsableSizeChannelsArePassedThroughUnchanged()
    {
        SpriteEmitterMath.ResolveSizeChannels(0.05f, 1f, out float width, out float height);
        Assert.Equal(0.05f, width, 5);
        Assert.Equal(1f, height, 5);
    }

    [Fact]
    public void ComposeWorldVelocity_OrientsTheConeByTheLocatorBasis()
    {
        // A hit/beam locator rotated so its local +Y points along world +Z (i.e. "forward").
        // Straight-up local motion must come out as world forward...
        SpriteEmitterMath.ComposeWorldVelocity(
            rightX: 1f, rightY: 0f, rightZ: 0f,
            upX: 0f, upY: 0f, upZ: 1f,
            forwardX: 0f, forwardY: -1f, forwardZ: 0f,
            localX: 0f, localY: 5f, localZ: 0f,
            out float x, out float y, out float z);

        Assert.Equal(0f, x, 5);
        Assert.Equal(0f, y, 5);
        Assert.Equal(5f, z, 5);
    }

    [Fact]
    public void GravityIsNotRoutedThroughComposeWorldVelocity()
    {
        // Regression: nano 45969's blood splat fell forward instead of down because gravity was
        // applied in locator space and then rotated. Gravity must stay world -Y no matter how the
        // locator is oriented, so it never passes through ComposeWorldVelocity. This test pins the
        // contract by showing what would happen if it did.
        const float gravity = -9.8f;

        SpriteEmitterMath.ComposeWorldVelocity(
            1f, 0f, 0f,
            0f, 0f, 1f,   // locator up points along world +Z
            0f, -1f, 0f,
            0f, gravity, 0f,
            out _, out float rotatedY, out float rotatedZ);

        // Rotating gravity would push it along world Z (forward) and zero out the vertical fall.
        Assert.Equal(0f, rotatedY, 5);
        Assert.Equal(gravity, rotatedZ, 5);

        // The emitter therefore uses the untransformed vector, which always falls on world -Y.
        Assert.True(gravity < 0f);
    }

    [Fact]
    public void ComposeWorldVelocityIsIdentityForAnUnrotatedLocator()
    {
        SpriteEmitterMath.ComposeWorldVelocity(
            1f, 0f, 0f,
            0f, 1f, 0f,
            0f, 0f, 1f,
            2f, 3f, 4f,
            out float x, out float y, out float z);

        Assert.Equal(2f, x, 5);
        Assert.Equal(3f, y, 5);
        Assert.Equal(4f, z, 5);
    }

    [Fact]
    public void ComposeWorldVelocityPreservesSpeedForAnOrthonormalBasis()
    {
        // 45 degree yaw basis.
        float s = MathF.Sqrt(0.5f);
        SpriteEmitterMath.SpawnVelocity(0.6f, 2.1f, 3f, 3.2f,
            out float lx, out float ly, out float lz);
        float localSpeed = MathF.Sqrt(lx * lx + ly * ly + lz * lz);

        SpriteEmitterMath.ComposeWorldVelocity(
            s, 0f, -s,
            0f, 1f, 0f,
            s, 0f, s,
            lx, ly, lz,
            out float x, out float y, out float z);
        float worldSpeed = MathF.Sqrt(x * x + y * y + z * z);

        Assert.Equal(localSpeed, worldSpeed, 4);
    }

    [Fact]
    public void SpawnVelocityMagnitudeIsMagnitudeTimesSpeedScale()
    {
        SpriteEmitterMath.SpawnVelocity(0.7f, 1.3f, 3f, 2f, out float x, out float y, out float z);
        float speed = MathF.Sqrt(x * x + y * y + z * z);
        Assert.Equal(6f, speed, 4);
    }

    [Fact]
    public void RangeAcceptsInvertedPairsBecauseStockDoesNotSort()
    {
        // Sparks 43720 stores elevation as (2.0, -0.062832) — high value first.
        Assert.Equal(2.0f, SpriteEmitterMath.Range(SparksElevA, SparksElevB, 0f), 5);
        Assert.Equal(-0.062832f, SpriteEmitterMath.Range(SparksElevA, SparksElevB, 1f), 5);
    }

    [Theory]
    [InlineData(1f, 0f, 1.1f)]   // Flare alpha 1 -> 0 over its own life
    [InlineData(0.67f, 0.4f, 0.58f)] // Sparks size 0.67 -> 0.4
    public void RatePerSecondReachesTheEndValueExactlyAtEndOfLife(float start, float end, float life)
    {
        float rate = SpriteEmitterMath.RatePerSecond(start, end, life);
        Assert.Equal(end, start + rate * life, 4);
    }

    [Fact]
    public void RatePerSecondIsSafeForDegenerateLife()
    {
        Assert.Equal(0f, SpriteEmitterMath.RatePerSecond(1f, 0f, 0f));
    }

    [Fact]
    public void ZeroRandMaskAlwaysPassesTheEmitGate()
    {
        // Sparks 43720 and Flare 43721 both carry field24 = 0.
        foreach (int r in new[] { 0, 1, int.MaxValue, int.MinValue, -12345 })
            Assert.True(SpriteEmitterMath.RandMaskPasses(0, r));
    }

    [Fact]
    public void EmitWindowClosesOneMaxSpriteLifeBeforeTheControlEnds()
    {
        // Sparks 43720: control duration 30s, max sprite life 0.86s.
        Assert.True(SpriteEmitterMath.WithinEmitWindow(30f, 10f, 0.86f));
        Assert.False(SpriteEmitterMath.WithinEmitWindow(30f, 29.5f, 0.86f));
        // Infinite duration always emits.
        Assert.True(SpriteEmitterMath.WithinEmitWindow(-1f, 1000f, 0.86f));
    }
}
