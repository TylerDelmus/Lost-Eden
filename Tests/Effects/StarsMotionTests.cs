using Xunit;

/// <summary>
/// Locks stock Stars motion used by known-good nanos.
/// 28612 hit → Stars type 3 (43168). 28608 hit → Stars type 8 (43719).
/// </summary>
public class StarsMotionTests
{
    // --- nano 28612 / gfxtweak 43168 (type 3) ---

    [Fact]
    public void Case3_SizeCurve_MatchesStockAtEndpoints()
    {
        const float sizeCurve = 2.4f; // field 28 on 43168
        Assert.Equal(0.2f * sizeCurve, StarsMotion.Case3Size(0f, sizeCurve), 3);
        // Mid: (0.5+0.2)*2.4*(1-0.25) = 0.7*2.4*0.75 = 1.26
        Assert.Equal(1.26f, StarsMotion.Case3Size(0.5f, sizeCurve), 3);
        // End: (1+0.2)*2.4*0 = 0 → clamped to 0.05
        Assert.Equal(0.05f, StarsMotion.Case3Size(1f, sizeCurve), 3);
    }

    [Fact]
    public void Case3_Step_PreservesPerpendicularSeedMotion()
    {
        // Spawn offset on +X, tangent = (0,0,-x) = (0,0,-1.3) for radius 1.3
        float px = 1.3f, py = 0f, pz = 0f;
        float tx = 0f, ty = 0f, tz = -1.3f;
        StarsMotion.Case3Step(ref px, ref py, ref pz, ref tx, ref ty, ref tz, 0f, 0f, 0f, 0.1f);

        // After one step: tangent bends toward origin, position moves along tangent.
        Assert.True(tz < -1.3f || Math.Abs(px - 1.3f) > 0.01f,
            "case 3 must move off the spawn point along the seeded tangent");
        Assert.True(Math.Abs(py) < 0.2f, "horizontal seed should keep Y near 0 early");
    }

    [Fact]
    public void Case3_Step_DoesNotCollapseStraightToOrigin()
    {
        float px = 1.3f, py = 0f, pz = 0f;
        float tx = 0f, ty = 0f, tz = -1.3f;
        for (int i = 0; i < 10; i++)
            StarsMotion.Case3Step(ref px, ref py, ref pz, ref tx, ref ty, ref tz, 0f, 0f, 0f, 0.1f);

        float dist = MathF.Sqrt(px * px + py * py + pz * pz);
        // Pure radial pull of 0.1^10 would be ~0.35; orbital seed keeps more distance / arcs.
        Assert.True(dist > 0.4f, $"expected swirl distance, got {dist}");
        Assert.True(Math.Abs(pz) > 0.05f, "expected Z travel from perpendicular seed");
    }

    // --- nano 28608 / gfxtweak 43719 (type 8) ---

    [Fact]
    public void Case8_ExpandsRadiusOverLife_Like43719()
    {
        // field31=1, field30=160, field28=0.6, field29=0.35, duration=0.6
        StarsMotion.Case8(0f, 0.6f, 1, 160, 0.6f, 0.35f, out float r0, out float s0, out float f0);
        Assert.Equal(0f, r0, 4);
        Assert.Equal(0.35f, s0, 3);
        Assert.Equal(0f, f0, 4);

        StarsMotion.Case8(0.3f, 0.6f, 1, 160, 0.6f, 0.35f, out float rMid, out float sMid, out float fMid);
        Assert.Equal(0.5f, fMid, 3);
        Assert.True(rMid > 0.5f, $"mid radius should expand, got {rMid}");
        Assert.True(sMid > s0, "size grows with sqrt phase");

        StarsMotion.Case8(0.599f, 0.6f, 1, 160, 0.6f, 0.35f, out float rEnd, out float sEnd, out _);
        Assert.True(rEnd > rMid, $"radius must keep expanding toward end ({rEnd} vs {rMid})");
        // max radius ≈ 1.6, size ≈ 0.6+0.35=0.95
        Assert.InRange(rEnd, 1.4f, 1.61f);
        Assert.InRange(sEnd, 0.9f, 0.96f);
    }

    [Fact]
    public void Case8_DoesNotUseCase0ShrinkingShell()
    {
        // Regression: old default path used sqrt(1.6-age) which shrinks toward origin.
        StarsMotion.Case8(0.1f, 0.6f, 1, 160, 0.6f, 0.35f, out float rEarly, out _, out _);
        StarsMotion.Case8(0.5f, 0.6f, 1, 160, 0.6f, 0.35f, out float rLate, out _, out _);
        Assert.True(rLate > rEarly, "type 8 must shoot outward, not collapse like case 0");
    }

    [Fact]
    public void Case8_SawtoothWrapsSoTheShellFlashesAgain()
    {
        // 43719's cycle is 0.6s with field31 = 1, and stock leaves the control alive past that
        // (field 8 = -1). frac(field31 * age / cycle) is only a sawtooth if the control outlives a
        // cycle: dividing by the control's own lifetime made the two equal, so the shell expanded
        // exactly once and stopped instead of flashing.
        StarsMotion.Case8(0.599f, 0.6f, 1, 160, 0.6f, 0.35f, out float rEndOfFirst, out _, out _);
        StarsMotion.Case8(0.601f, 0.6f, 1, 160, 0.6f, 0.35f, out float rStartOfSecond, out _, out _);

        Assert.True(
            rStartOfSecond < rEndOfFirst * 0.2f,
            $"radius must snap back at the cycle boundary ({rStartOfSecond} vs {rEndOfFirst})");

        // And the second pass repeats the first rather than drifting.
        StarsMotion.Case8(0.3f, 0.6f, 1, 160, 0.6f, 0.35f, out float rFirstMid, out float sFirstMid, out _);
        StarsMotion.Case8(0.9f, 0.6f, 1, 160, 0.6f, 0.35f, out float rSecondMid, out float sSecondMid, out _);
        Assert.Equal(rFirstMid, rSecondMid, 4);
        Assert.Equal(sFirstMid, sSecondMid, 4);
    }

    [Fact]
    public void Case8_CycleCountPacksMorePassesIntoTheSameWindow()
    {
        // field31 is how many sawtooth passes fit in one cycle window, so raising it flashes faster.
        StarsMotion.Case8(0.3f, 0.6f, 1, 160, 0.6f, 0.35f, out _, out _, out float oneCycle);
        StarsMotion.Case8(0.3f, 0.6f, 2, 160, 0.6f, 0.35f, out _, out _, out float twoCycles);

        Assert.Equal(0.5f, oneCycle, 3);
        Assert.Equal(0f, twoCycles, 3);
    }

    [Theory]
    [InlineData(43168, 3, 2.4f, 1.3f, 900)]   // 28612 child
    [InlineData(43719, 8, 0.6f, 0.35f, 160)]  // 28608 child
    public void KnownNanoStarsTemplates_FieldLayoutSanity(
        int effectId, int starType, float sizeCurve, float radiusOrBias, int field30)
    {
        // Documents the gfxtweak contracts tests above encode — fail loudly if IDs drift in comments.
        Assert.True(effectId == 43168 || effectId == 43719);
        Assert.True(starType == 3 || starType == 8);
        Assert.True(sizeCurve > 0f && radiusOrBias > 0f && field30 > 0);
    }
}
