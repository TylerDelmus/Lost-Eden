using AOSharp.Common.GameData;

/// <summary>
/// Stock's per-body tables for effects that sit round a character's head. A BuffPlaceHolder's field 26
/// (dynel ctor <c>Gamecode 100d5d8e</c>) and a Cord's field 36 (<c>100d6803</c>) can name a type-0 record
/// of (offset z, radius) pairs. When the host is a character, the pair at
/// <c>(Breed - 1) * 12 + (Sex == 3 ? 6 : 0) + Fatness * 2</c> (stats 4, 59 and 47; Breed 1-4 only)
/// replaces the template's field 3 in the locator. The BuffPlaceHolder also takes the radius (its
/// field 21) and the host's Scale (stat 360) / 100; the Cord takes only the offset.
/// </summary>
public static class EffectBodyTable
{
    public struct Entry
    {
        public bool HasOffset;
        public float OffsetZ;
        public bool HasRadius;
        public float Radius;
        /// <summary>Host Scale / 100, or 1 without a table or a character (stock's default at +0x94).</summary>
        public float Scale;
    }

    public static Entry Read(GfxTweakCatalog catalog, GfxTweakRecord record, EffectLocator locator)
    {
        var entry = new Entry { Scale = 1f };
        if (record == null || catalog == null || locator == null)
            return entry;

        int field;
        if (record.TypeCode == EffectTypeTags.BuffPlaceHolder)
            field = 26;
        else if (record.TypeCode == EffectTypeTags.Cord)
            field = 36;
        else
            return entry;

        int tableId = record.FieldInt(field, 0);
        if (tableId <= 0 || !catalog.TryGet(tableId, out GfxTweakRecord table) || table == null)
            return entry;
        if (!TryGetHostStats(locator, out StatCollection stats))
            return entry;

        int breed = stats.Get(Stat.Breed);
        int sex = stats.Get(Stat.Sex);
        int fatness = stats.Get(Stat.Fatness);

        if (record.TypeCode == EffectTypeTags.BuffPlaceHolder)
        {
            // Port-only: an unset Scale reads 0 here; VisualDynel.ApplyScale draws that as 1.
            int scale = stats.Get(Stat.Scale);
            entry.Scale = scale > 0 ? scale * 0.01f : 1f;
        }

        if (breed - 1 < 0 || breed - 1 > 3)
            return entry;

        int index = breed * 12 - 12 + (sex == 3 ? 6 : 0) + fatness * 2;
        if (index < 0 || index >= table.FieldCount)
            return entry;

        entry.HasOffset = true;
        entry.OffsetZ = table.Field(index, 0f);
        if (record.TypeCode == EffectTypeTags.BuffPlaceHolder && index + 1 < table.FieldCount)
        {
            entry.HasRadius = true;
            entry.Radius = table.Field(index + 1, 0f);
        }
        return entry;
    }

    static bool TryGetHostStats(EffectLocator locator, out StatCollection stats)
    {
        stats = null;
        if (locator.TryGetSourceDynel(out Dynel dynel) && dynel is Character)
            stats = dynel.Stats;
        else if (locator.TryGetVisual(out VisualDynel visual) && visual != null)
            stats = visual.Stats;
        return stats != null;
    }
}
