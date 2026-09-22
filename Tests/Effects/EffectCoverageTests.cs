using System;
using System.Collections.Generic;
using Xunit;

public class EffectCoverageTests
{
    static float Bits(int v) => BitConverter.Int32BitsToSingle(v);

    static GfxTweakRecord Rec(int id, int typeCode, params (int Index, int Value)[] ints)
    {
        var fields = new float[40];
        foreach (var (index, value) in ints)
            fields[index] = Bits(value);
        return new GfxTweakRecord { Id = id, TypeCode = typeCode, Fields = fields };
    }

    static EffectCoverage Coverage(params GfxTweakRecord[] records)
    {
        var map = new Dictionary<int, GfxTweakRecord>();
        foreach (GfxTweakRecord r in records)
            map[r.Id] = r;
        return new EffectCoverage(id => map.TryGetValue(id, out GfxTweakRecord r) ? r : null);
    }

    [Fact]
    public void MetaOverStarsCase3_IsVerified()
    {
        // 47299: Meta -> Stars 43168 (starType 3).
        EffectCoverage c = Coverage(
            Rec(47299, EffectTypeTags.Meta, (0, 43168)),
            Rec(43168, EffectTypeTags.Stars, (10, 3)));
        Assert.Equal(EffectStatus.Verified, c.Of(47299).Status);
        Assert.Empty(c.Of(47299).Gaps);
    }

    [Fact]
    public void OtherStarTypes_AreUnverified_AndNamed()
    {
        EffectCoverage c = Coverage(Rec(45001, EffectTypeTags.Stars, (10, 5), (30, 2000)), Rec(2000, EffectTypeTags.FlareAlt));
        EffectCoverage.Result r = c.Of(45001);
        Assert.Equal(EffectStatus.Unverified, r.Status);
        Assert.Equal(new[] { "Stars #5 unverified" }, r.Gaps); // field 30 is not a child
    }

    [Fact]
    public void UnportedControl_MakesTheTreeMissing()
    {
        EffectCoverage c = Coverage(Rec(1, 0xbd9));
        Assert.Equal(EffectStatus.Missing, c.Of(1).Status);
        Assert.Equal(new[] { "Spiral2 missing" }, c.Of(1).Gaps);
    }

    [Fact]
    public void Spell1_FollowsFields31And32_OnlyWithTheLateWindows()
    {
        GfxTweakRecord nano1 = Rec(8011, EffectTypeTags.Nano1);
        EffectCoverage skip = Coverage(Rec(46237, EffectTypeTags.Spell1, (29, 46014), (31, 8011), (33, 1)),
            Rec(46014, EffectTypeTags.Flare), nano1);
        Assert.Equal(EffectStatus.Verified, skip.Of(46237).Status);

        EffectCoverage late = Coverage(Rec(46237, EffectTypeTags.Spell1, (29, 46014), (31, 8011)),
            Rec(46014, EffectTypeTags.Flare), nano1);
        EffectCoverage.Result r = late.Of(46237);
        Assert.Equal(EffectStatus.Approximated, r.Status);
        Assert.Contains("Spell1 unverified", r.Gaps);
        Assert.Contains("Nano1 approx", r.Gaps);
    }

    [Fact]
    public void Cord_IsVerifiedWhetherOrNotItCanLink()
    {
        Assert.Equal(EffectStatus.Verified, Coverage(Rec(8000, EffectTypeTags.Cord, (0, 5))).Of(8000).Status);
        Assert.Equal(EffectStatus.Verified, Coverage(Rec(8002, EffectTypeTags.Cord, (0, 7))).Of(8002).Status);
    }

    [Fact]
    public void MissingRecordsAndCycles_AreHandled()
    {
        EffectCoverage c = Coverage(
            Rec(1, EffectTypeTags.Meta, (0, 2), (1, 999)),
            Rec(2, EffectTypeTags.Meta, (0, 1)));
        EffectCoverage.Result r = c.Of(1);
        Assert.Equal(EffectStatus.Missing, r.Status);
        Assert.Equal(new[] { "999 not in gfxtweak" }, r.Gaps);
    }

    [Fact]
    public void SilentControls_DoNotCountAgainstTheTree()
    {
        EffectCoverage c = Coverage(Rec(10, EffectTypeTags.Meta, (0, 11)), Rec(11, 0xfa0));
        Assert.Equal(EffectStatus.Verified, c.Of(10).Status);
    }
}
