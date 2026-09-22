using System;
using System.Collections.Generic;

/// <summary>How closely the port reproduces an effect, worst first.</summary>
public enum EffectStatus
{
    /// <summary>Some control in the tree has no port, so part of the effect is not drawn.</summary>
    Missing = 0,
    /// <summary>Drawn by a stand-in that is known not to be stock-exact.</summary>
    Approximated = 1,
    /// <summary>Claimed as a port but not yet rebuilt and checked against stock.</summary>
    Unverified = 2,
    /// <summary>Every control in the tree was rebuilt from Gamecode/DisplaySystem and checked.</summary>
    Verified = 3,
}

/// <summary>
/// Tells tooling which effects should already look like the game. A record is judged by its
/// control's support (<see cref="EffectTypeCatalog"/>) and, where only part of a control has been
/// rebuilt from stock, by the fields that select that part. Children are followed the way the
/// port's composite controls spawn them, and a tree is as good as its worst record.
/// </summary>
public sealed class EffectCoverage
{
    public readonly struct Result
    {
        public readonly EffectStatus Status;

        /// <summary>The records that hold the tree back, e.g. "Stars #7 unverified".</summary>
        public readonly string[] Gaps;

        public Result(EffectStatus status, string[] gaps)
        {
            Status = status;
            Gaps = gaps ?? Array.Empty<string>();
        }
    }

    readonly Func<int, GfxTweakRecord> _lookup;
    readonly Dictionary<int, Result> _memo = new Dictionary<int, Result>();

    /// <param name="lookup">Effect id to its gfxtweak record, or null when there is none.</param>
    public EffectCoverage(Func<int, GfxTweakRecord> lookup)
    {
        _lookup = lookup ?? throw new ArgumentNullException(nameof(lookup));
    }

    public Result Of(int effectId)
    {
        if (_memo.TryGetValue(effectId, out Result cached))
            return cached;

        var gaps = new SortedSet<string>(StringComparer.Ordinal);
        EffectStatus status = Walk(effectId, new HashSet<int>(), gaps);
        var result = new Result(status, new List<string>(gaps).ToArray());
        _memo[effectId] = result;
        return result;
    }

    EffectStatus Walk(int effectId, HashSet<int> visiting, SortedSet<string> gaps)
    {
        if (effectId <= 0 || !visiting.Add(effectId))
            return EffectStatus.Verified;

        GfxTweakRecord record = _lookup(effectId);
        if (record == null)
        {
            gaps.Add($"{effectId} not in gfxtweak");
            return EffectStatus.Missing;
        }

        EffectStatus worst = NodeStatus(record);
        if (worst != EffectStatus.Verified)
            gaps.Add($"{Describe(record)} {Label(worst)}");

        foreach (int child in Children(record))
        {
            EffectStatus childStatus = Walk(child, visiting, gaps);
            if (childStatus < worst)
                worst = childStatus;
        }

        visiting.Remove(effectId);
        return worst;
    }

    /// <summary>One record on its own, ignoring children.</summary>
    public static EffectStatus NodeStatus(GfxTweakRecord record)
    {
        switch (EffectTypeCatalog.SupportOf(record.TypeCode))
        {
            case EffectSupport.NotRendered:
                return EffectStatus.Verified; // stock draws nothing either
            case EffectSupport.Approximated:
                return EffectStatus.Approximated;
            case EffectSupport.Ported:
                // Only the Deformer's wobble mode is ported; its other modes draw nothing yet.
                if (record.TypeCode == EffectTypeTags.Deformer && record.FieldInt(10, 0) != DeformerSim.WobbleMode)
                    return EffectStatus.Missing;
                if (record.TypeCode == EffectTypeTags.Electra && record.FieldInt(10, 0) != ElectraSim.ShellMode)
                    return EffectStatus.Missing;
                return IsVerified(record) ? EffectStatus.Verified : EffectStatus.Unverified;
            default:
                return record.TypeCode == 0 ? EffectStatus.Verified : EffectStatus.Missing;
        }
    }

    /// <summary>
    /// The parts rebuilt from stock and checked live: Meta, Flare, Tracer1, Tracer4, Plasma, Deformer mode 1, Electra mode 1, Spell1 when field 33
    /// skips its late windows, Stars starTypes 3, 7, 8 and 19, and a Cord that cannot link (field 0 bit 1 clear,
    /// invisible in stock).
    /// </summary>
    static bool IsVerified(GfxTweakRecord record)
    {
        switch (record.TypeCode)
        {
            case EffectTypeTags.Meta:
            case EffectTypeTags.Flare:
            case EffectTypeTags.Tracer1:
            case EffectTypeTags.Plasma:
            case EffectTypeTags.Tracer4:
            case EffectTypeTags.Deformer:
            case EffectTypeTags.Electra:
                return true;
            case EffectTypeTags.Spell1:
                return record.FieldInt(33, 0) != 0;
            case EffectTypeTags.Stars:
                return record.FieldInt(10, 0) is 3 or 7 or 8 or 19;
            case EffectTypeTags.Cord:
                return (record.FieldInt(0, 0) & 2) == 0;
            default:
                return false;
        }
    }

    /// <summary>Child effect ids, as the port's composite controls read them.</summary>
    public static IEnumerable<int> Children(GfxTweakRecord record)
    {
        switch (record.TypeCode)
        {
            case EffectTypeTags.Meta:
                // GfxControlMeta: slots 0..9; slot 9 = -1 means "infinite", not a child.
                for (int i = 0; i < 10; i++)
                {
                    int id = record.FieldInt(i, 0);
                    if (id > 0 && id != record.Id)
                        yield return id;
                }
                break;
            case EffectTypeTags.Sequencer:
            {
                int n = Math.Min(Math.Max(0, record.FieldInt(1, 0)), 64);
                for (int i = 0; i < n; i++)
                {
                    int id = record.FieldInt(2 + 3 * i, 0);
                    if (id > 0)
                        yield return id;
                }
                break;
            }
            case EffectTypeTags.Delay:
                if (record.FieldInt(2, 0) > 0)
                    yield return record.FieldInt(2, 0);
                break;
            case EffectTypeTags.Scatter:
                if (record.FieldInt(10, 0) > 0)
                    yield return record.FieldInt(10, 0);
                break;
            case EffectTypeTags.Spell1:
                // Window 1 always; fields 31 and 32 only in the late windows (field 33 == 0).
                for (int f = 27; f <= 30; f++)
                {
                    if (record.FieldInt(f, 0) > 0)
                        yield return record.FieldInt(f, 0);
                }
                if (record.FieldInt(33, 0) == 0)
                {
                    if (record.FieldInt(31, 0) > 0)
                        yield return record.FieldInt(31, 0);
                    if (record.FieldInt(32, 0) > 0)
                        yield return record.FieldInt(32, 0);
                }
                break;
        }
    }

    public static string Label(EffectStatus status) => status switch
    {
        EffectStatus.Verified => "verified",
        EffectStatus.Unverified => "unverified",
        EffectStatus.Approximated => "approx",
        _ => "missing",
    };

    static string Describe(GfxTweakRecord record)
    {
        if (record.TypeCode == EffectTypeTags.Stars)
            return $"Stars #{record.FieldInt(10, 0)}";

        if (EffectTypeCatalog.TryGet(record.TypeCode, out EffectTypeInfo info) && info.StockClass != null)
        {
            string name = info.StockClass.TrimStart('_');
            if (name.StartsWith("GfxControl", StringComparison.Ordinal))
                name = name.Substring("GfxControl".Length);
            int suffix = name.LastIndexOf('_');
            if (suffix > 0)
                name = name.Substring(0, suffix);
            return name;
        }

        return $"0x{record.TypeCode:X}";
    }
}
