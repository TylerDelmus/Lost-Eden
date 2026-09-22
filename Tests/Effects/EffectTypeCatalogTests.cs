using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

/// <summary>
/// Locks the typeCode inventory recovered from _EffectHandler_t::CreateGfxControl so a future
/// edit cannot silently re-introduce a wrong classification (e.g. drawing audio as a billboard).
/// </summary>
public class EffectTypeCatalogTests
{
    /// <summary>
    /// typeCodes reachable only through CreateGfxControlTracer(int, Vector3&amp;, Vector3&amp;)
    /// at 0x100d1546. These are stretched between two points, never locator-anchored.
    /// </summary>
    public static readonly int[] StockTracerTypeCodes =
        { 0x3f5, 0x3fb, 0x3fd, 0x3fe, 0x400, 0x401, 0x402, 0x403, 0xbd2 };

    [Theory]
    [InlineData(0x3f5)]
    [InlineData(0x3fb)]
    [InlineData(0x3fd)]
    [InlineData(0x3fe)]
    [InlineData(0x400)]
    [InlineData(0x401)]
    [InlineData(0x402)]
    [InlineData(0x403)]
    [InlineData(0xbd2)]
    public void TracerTypeCodes_AreClassifiedAsTracers(int typeCode)
    {
        Assert.True(EffectTypeCatalog.TryGet(typeCode, out EffectTypeInfo info),
            $"0x{typeCode:X} is in the stock tracer dispatch but missing from the catalog.");
        Assert.Equal(EffectCategory.Tracer, info.Category);
    }

    [Fact]
    public void OnlyAudioBuffAndNullTypeAreIntentionallyNotRendered()
    {
        int[] expected = { 0, 0x3e9, 0x3ea, 0xfa0 };
        int[] actual = EffectTypeCatalog.All
            .Where(i => i.Support == EffectSupport.NotRendered)
            .Select(i => i.TypeCode)
            .OrderBy(c => c)
            .ToArray();
        Assert.Equal(expected.OrderBy(c => c).ToArray(), actual);
    }

    [Fact]
    public void AudioTypeIsNeverDrawn()
    {
        // 4000 is _GfxControlAudio_t. It is referenced by 30 composite children, so a
        // misclassification here would paint sprites for every sound cue.
        Assert.Equal(EffectCategory.Audio, EffectTypeCatalog.CategoryOf(0xfa0));
        Assert.True(EffectTypeCatalog.IsIntentionallyNotRendered(0xfa0));
        Assert.False(EffectTypeTags.IsSpriteFamily(0xfa0));
        Assert.False(EffectTypeTags.IsBeam(0xfa0));
    }

    [Fact]
    public void SpriteFamilyTagsAreCataloguedAsSprites()
    {
        int[] spriteFamily =
        {
            EffectTypeTags.Cord, EffectTypeTags.SpriteAlt, EffectTypeTags.Flare, EffectTypeTags.FlareAlt,
            EffectTypeTags.Nano0, EffectTypeTags.Nano1, EffectTypeTags.Nano2, EffectTypeTags.Nano3,
            EffectTypeTags.Sprite, EffectTypeTags.Sparks,
        };

        foreach (int typeCode in spriteFamily)
        {
            Assert.True(EffectTypeCatalog.TryGet(typeCode, out EffectTypeInfo info),
                $"sprite-family 0x{typeCode:X} missing from catalog");
            // Sparks is an emitter even though the tag list groups it with sprites.
            EffectCategory expected = typeCode == EffectTypeTags.Sparks
                ? EffectCategory.Particle
                : EffectCategory.Sprite;
            Assert.Equal(expected, info.Category);
        }
    }

    [Fact]
    public void AnythingWeDrawHasARenderableCategory()
    {
        foreach (EffectTypeInfo info in EffectTypeCatalog.All)
        {
            if (!EffectTypeTags.IsSpriteFamily(info.TypeCode) && !EffectTypeTags.IsBeam(info.TypeCode))
                continue;

            Assert.NotEqual(EffectCategory.Audio, info.Category);
            Assert.NotEqual(EffectCategory.Buff, info.Category);
            Assert.NotEqual(EffectCategory.None, info.Category);
        }
    }

    [Fact]
    public void PortedSetMatchesTheControlsWeActuallyImplement()
    {
        // Promote a typeCode to Ported only once its Process() is a faithful port, not a stand-in.
        int[] expected =
        {
            EffectTypeTags.Meta, EffectTypeTags.Sequencer, EffectTypeTags.Delay,
            EffectTypeTags.Stars, EffectTypeTags.Highlight, EffectTypeTags.Spell1,
            EffectTypeTags.Flare, EffectTypeTags.FlareAlt, EffectTypeTags.Sparks,
            EffectTypeTags.Scatter, EffectTypeTags.Cord, EffectTypeTags.Tracer1, EffectTypeTags.Plasma,
            EffectTypeTags.Tracer4, EffectTypeTags.Deformer, EffectTypeTags.Electra, EffectTypeTags.Suns, EffectTypeTags.Shield,
        };
        int[] actual = EffectTypeCatalog.All
            .Where(i => i.Support == EffectSupport.Ported)
            .Select(i => i.TypeCode)
            .OrderBy(c => c)
            .ToArray();
        Assert.Equal(expected.OrderBy(c => c).ToArray(), actual);
    }

    [Fact]
    public void EveryCompositeChildTypeCodeIsCatalogued()
    {
        if (!GfxTweak.TryLoad(out List<GfxTweak.Record> records))
            return; // stock install not present on this machine

        var byId = records.ToDictionary(r => r.Id);
        var childTypeCodes = new SortedSet<int>();

        foreach (GfxTweak.Record root in records.Where(r => GfxTweak.IsComposite(r.TypeCode)))
        {
            var seen = new HashSet<int>();
            var stack = new Stack<int>(GfxTweak.ChildIds(root));
            while (stack.Count > 0)
            {
                int id = stack.Pop();
                if (!seen.Add(id) || !byId.TryGetValue(id, out GfxTweak.Record child))
                    continue;
                childTypeCodes.Add(child.TypeCode);
                foreach (int next in GfxTweak.ChildIds(child))
                    stack.Push(next);
            }
        }

        int[] uncatalogued = childTypeCodes
            .Where(c => !EffectTypeCatalog.TryGet(c, out _))
            .ToArray();

        Assert.True(uncatalogued.Length == 0,
            "typeCodes reachable from composite effect trees but absent from EffectTypeCatalog: "
            + string.Join(", ", uncatalogued.Select(c => $"0x{c:X}")));
    }
}

/// <summary>Minimal gfxtweak.bin reader for data-driven tests against the stock install.</summary>
static class GfxTweak
{
    public sealed class Record
    {
        public int Id;
        public int TypeCode;
        public int[] Ints = Array.Empty<int>();
    }

    const int Meta = 0x7d7;
    const int Sequencer = 0xbbc;
    const int Delay = 0xbc5;

    public static bool IsComposite(int typeCode)
        => typeCode == Meta || typeCode == Sequencer || typeCode == Delay;

    public static IEnumerable<int> ChildIds(Record r)
    {
        switch (r.TypeCode)
        {
            case Meta:
                for (int i = 0; i < Math.Min(10, r.Ints.Length); i++)
                    if (r.Ints[i] > 0)
                        yield return r.Ints[i];
                break;
            case Sequencer:
                int n = r.Ints.Length > 1 ? r.Ints[1] : 0;
                if (n < 0 || n > 64)
                    break;
                for (int i = 0; i < n; i++)
                {
                    int k = 2 + 3 * i;
                    if (k < r.Ints.Length && r.Ints[k] > 0)
                        yield return r.Ints[k];
                }
                break;
            case Delay:
                if (r.Ints.Length > 2 && r.Ints[2] > 0)
                    yield return r.Ints[2];
                break;
        }
    }

    public static bool TryLoad(out List<Record> records)
    {
        records = new List<Record>();
        string path = Environment.ExpandEnvironmentVariables(
            @"%USERPROFILE%\OneDrive\Desktop\Anarchy Online\Setupf\gfxtweak.bin");
        if (!File.Exists(path))
            return false;

        byte[] b = File.ReadAllBytes(path);
        int total = BitConverter.ToInt32(b, 0);
        int o = 4;
        for (int i = 0; i < total; i++)
        {
            if (o + 12 > b.Length)
                return false;
            var r = new Record
            {
                Id = BitConverter.ToInt32(b, o),
                TypeCode = BitConverter.ToInt32(b, o + 4),
            };
            int count = BitConverter.ToInt32(b, o + 8);
            o += 12;
            if (count < 0 || o + count * 4 > b.Length)
                return false;
            r.Ints = new int[count];
            for (int j = 0; j < count; j++)
                r.Ints[j] = BitConverter.ToInt32(b, o + j * 4);
            o += count * 4;
            records.Add(r);
        }
        return records.Count > 0;
    }
}
