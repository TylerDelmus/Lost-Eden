using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

/// <summary>
/// Locks the tracer template layout against the stock gfxtweak.bin. These read real records rather
/// than invented numbers, because the bug being guarded here was a plausible-looking but wrong
/// field mapping: the sprite family's colour block sits at field 16, and reading a tracer that way
/// pulls in field 17, which is an integer, and produces a near-black colour that renders nothing.
/// </summary>
public class TracerMathTests
{
    const int BeamCylinder = 1013;
    const int BeamRibbon = 1024;
    const int BeamRibbonAlt = 1025;
    const int BeamRibbonWide = 1026;

    static readonly int[] TracerTypeCodes =
        { BeamCylinder, BeamRibbon, BeamRibbonAlt, BeamRibbonWide };

    sealed class Tracer
    {
        public int Id;
        public int TypeCode;
        public float[] F = Array.Empty<float>();
        public int Count => F.Length;

        public float Alpha => F[TracerMath.FieldAlpha];
        public float Red => F[TracerMath.FieldRed];
        public float Green => F[TracerMath.FieldGreen];
        public float Blue => F[TracerMath.FieldBlue];
        public float Width => TracerMath.Width(F[TracerMath.FieldWidth]);
        public float Length => TracerMath.Length(F[TracerMath.FieldLength]);
    }

    static bool TryLoad(out Dictionary<int, Tracer> byId)
    {
        byId = new Dictionary<int, Tracer>();
        if (!GfxTweak.TryLoad(out List<GfxTweak.Record> records))
            return false; // stock install not present on this machine

        foreach (GfxTweak.Record r in records.Where(r => TracerTypeCodes.Contains(r.TypeCode)))
        {
            byId[r.Id] = new Tracer
            {
                Id = r.Id,
                TypeCode = r.TypeCode,
                F = r.Ints.Select(BitConverter.Int32BitsToSingle).ToArray(),
            };
        }
        return byId.Count > 0;
    }

    [Fact]
    public void The45879TracerIsAnOpaqueWarmWhiteStreak()
    {
        if (!TryLoad(out Dictionary<int, Tracer> byId))
            return;

        Tracer t = byId[45706];
        Assert.Equal(BeamRibbonAlt, t.TypeCode);

        // Opaque, and white with a faint warm cast - this is the tracer that visibly travels to the
        // target on nano 45879.
        Assert.Equal(1f, t.Alpha, 4);
        Assert.Equal(1f, t.Red, 4);
        Assert.Equal(0.8f, t.Green, 4);
        Assert.Equal(0.8f, t.Blue, 4);
        Assert.True(TracerMath.IsVisible(t.Width, t.Length, t.Alpha));

        // A long thin streak, not a square.
        Assert.Equal(0.125f, t.Width, 4);
        Assert.Equal(2.5f, t.Length, 4);
    }

    [Fact]
    public void ReadingTheColourAtTheSpriteFamilyOffsetWouldBlackOutTheTracer()
    {
        if (!TryLoad(out Dictionary<int, Tracer> byId))
            return;

        // Field 17 holds the integer 7, so a float getter sees a denormal ~1e-44. That is what made
        // the streak invisible under additive blending.
        Tracer t = byId[45706];
        Assert.Equal(7, BitConverter.SingleToInt32Bits(t.F[17]));
        Assert.True(t.F[17] < 1e-40f);
    }

    [Fact]
    public void ABlankTracerRecordStaysInvisible()
    {
        if (!TryLoad(out Dictionary<int, Tracer> byId))
            return;

        // 45705 is all zeroes on purpose; it must not be rescued into a visible white quad.
        Tracer t = byId[45705];
        Assert.False(TracerMath.IsVisible(t.Width, t.Length, t.Alpha));
    }

    [Fact]
    public void PaletteSwappedTracersShareTheirAlpha()
    {
        if (!TryLoad(out Dictionary<int, Tracer> byId))
            return;

        // 45500..45503 and 45504..45506 are the same tracer in two colours. Only the AARRGGBB
        // reading gives both of them full alpha; swapping the order would leave one at 0.2, which
        // is how you can tell field 13 is alpha rather than red.
        foreach (int id in new[] { 45500, 45501, 45502, 45504, 45505, 45506 })
            Assert.Equal(1f, byId[id].Alpha, 4);

        Assert.True(byId[45500].Blue > byId[45500].Red);   // blue variant
        Assert.True(byId[45504].Red > byId[45504].Blue);   // red variant
    }

    [Fact]
    public void EveryTracerRecordHasTheWholeColourBlock()
    {
        if (!TryLoad(out Dictionary<int, Tracer> byId))
            return;

        Assert.All(byId.Values, t => Assert.True(t.Count >= TracerMath.MinFieldCount));
    }

    [Fact]
    public void LengthAndWidthAreIndependent()
    {
        if (!TryLoad(out Dictionary<int, Tracer> byId))
            return;

        // Deliberately NOT asserting that every tracer is longer than it is wide. The 1024 family
        // really is stubbier than it is wide (2690 is 0.05 long by 0.125 across), so the two fields
        // must be carried separately rather than one derived from the other or from texture aspect.
        Assert.True(byId[45706].Length > byId[45706].Width);
        Assert.True(byId[2690].Length < byId[2690].Width);
    }

    [Fact]
    public void DegenerateSizesAreRejected()
    {
        Assert.Equal(0f, TracerMath.Width(0f));
        Assert.Equal(0f, TracerMath.Width(-1f));
        Assert.Equal(0f, TracerMath.Width(float.NaN));
        Assert.Equal(0f, TracerMath.Width(1000f));       // beyond a plausible width
        Assert.Equal(0.125f, TracerMath.Width(0.125f), 5);

        // Length has a far wider ceiling: record 70004 is a 100 metre beam.
        Assert.Equal(100f, TracerMath.Length(100f), 5);
        Assert.Equal(0f, TracerMath.Length(float.PositiveInfinity));
    }

    [Fact]
    public void AlphaBelowOneByteStepIsInvisible()
    {
        // Stock rounds alpha to a byte, so this is where "faint" really becomes "nothing".
        Assert.False(TracerMath.IsVisible(0.1f, 1f, 0f));
        Assert.False(TracerMath.IsVisible(0.1f, 1f, 0.001f));
        Assert.True(TracerMath.IsVisible(0.1f, 1f, 0.01f));
    }
}
