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
        // Not in gfxtweak.bin: stock's factory (100d0656) creates nothing and neither does the port, so it
        // matches stock exactly (user's decision, 2026-09-22; Docs/Effects.md §8).
        if (record == null)
            return EffectStatus.Verified;

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
                if (record.TypeCode == EffectTypeTags.Suns
                    && record.FieldInt(10, 0) is not (SunsSim.SparkLineType or SunsSim.RingType or SunsSim.HaloType))
                    return EffectStatus.Missing;
                if (record.TypeCode == EffectTypeTags.BParticle && record.FieldInt(10, 0) is not (8 or 1))
                    return EffectStatus.Missing;
                if (record.TypeCode == EffectTypeTags.GroundGrid && record.FieldInt(11, 0) != 0)
                    return EffectStatus.Missing;
                if (record.TypeCode == EffectTypeTags.EffectMesh && !EffectMeshSim.IsModelled(record.Fields))
                    return EffectStatus.Approximated;
                // Sprite's unblended draw (0x1000) and Sprite3's multiply blends aren't drawn as stock does.
                if (record.TypeCode == EffectTypeTags.Sprite
                    && ((record.FieldInt(10, 0) & SpriteSim.FlagOpaque) != 0
                        || ((record.FieldInt(10, 0) & 3) == 3 && (record.FieldInt(10, 0) & SpriteSim.FlagAlphaBlend) != 0)))
                    return EffectStatus.Approximated;
                // Toggle's 0x1000 (a child with no locator, 100ce912) is made on the Toggle's locator instead.
                if (record.TypeCode == EffectTypeTags.Toggle && (record.FieldInt(0, 0) & ToggleSim.FlagNoLocator) != 0)
                    return EffectStatus.Approximated;
                // Highlight mode 3 skips the body's own node in stock and filters the attachments it
                // tints; the port tints everything from the root. No record with mode 3 reaches a nano.
                if (record.TypeCode == EffectTypeTags.Highlight
                    && record.FieldInt(1, 0) == HighlightSim.ModeSpecular)
                    return EffectStatus.Approximated;
                // Shield2's field 20 (0..9) puts the shell on an EP03 mech .abiff instead of the host.
                if (record.TypeCode == EffectTypeTags.Shield2
                    && record.FieldInt(20, -1) >= 0 && record.FieldInt(20, -1) <= 9)
                    return EffectStatus.Approximated;
                // A negative field 18 is scaled by the CAT mesh's torso sphere, which the port doesn't read.
                if (record.TypeCode == EffectTypeTags.SkyFlash && record.Field(18, 0f) < 0f)
                    return EffectStatus.Approximated;
                return IsVerified(record) ? EffectStatus.Verified : EffectStatus.Unverified;
            default:
                return record.TypeCode == 0 ? EffectStatus.Verified : EffectStatus.Missing;
        }
    }

    /// <summary>
    /// The parts rebuilt from stock and checked live: Meta, Flare, Tracer1, Tracer4, Plasma, Deformer mode 1, Electra mode 1, Suns sunTypes 0, 1 and 4, Shield, Shield2, Scatter, Highlight (modes 0-2), GroundShake, Sequencer, Spell1 when field 33
    /// skips its late windows, Stars starTypes 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 22 and 28, BPHFSM, BuffPlaceHolder, BParticle2, TParticle, TParticle2, BParticle modes 8 and 1, GroundGrid mode 0, EffectMesh, MParticle, Sparks, Fire, Smoke, Spiral, Tracer5, Tracer3, Nano0, ShockWave, VulcanRocks, VolGrid, Sprite, SkyFlash, Trail2, Toggle, Spiral2, Tracer6, Beam, CrazyCone, Delay, GroundRing, Mesh and Cord (a Cord
    /// without field 0 bit 1 never links, so it is invisible in stock and here).
    /// </summary>
    static bool IsVerified(GfxTweakRecord record)
    {
        switch (record.TypeCode)
        {
            case EffectTypeTags.Meta:
            case EffectTypeTags.Delay:
            case EffectTypeTags.GroundRing:
            case EffectTypeTags.Mesh:
            case EffectTypeTags.Flare:
            case EffectTypeTags.Tracer1:
            case EffectTypeTags.Plasma:
            case EffectTypeTags.Tracer4:
            case EffectTypeTags.Deformer:
            case EffectTypeTags.Electra:
            case EffectTypeTags.Suns:
            case EffectTypeTags.GroundShake:
            case EffectTypeTags.Highlight:
            case EffectTypeTags.Scatter:
            case EffectTypeTags.Shield:
            case EffectTypeTags.Shield2:
            case EffectTypeTags.Sequencer:
            case EffectTypeTags.BuffFsm:
            case EffectTypeTags.BuffPlaceHolder:
            case EffectTypeTags.Cord:
            case EffectTypeTags.BParticle2:
            case EffectTypeTags.TParticle:
            case EffectTypeTags.TParticle2:
            case EffectTypeTags.BParticle:
            case EffectTypeTags.GroundGrid:
            case EffectTypeTags.EffectMesh:
            case EffectTypeTags.MParticle:
            case EffectTypeTags.Sparks:
            case EffectTypeTags.Fire:
            case EffectTypeTags.Smoke:
            case EffectTypeTags.Spiral:
            case EffectTypeTags.Spiral2:
            case EffectTypeTags.Beam:
            case EffectTypeTags.CrazyCone:
            case EffectTypeTags.Tracer5:
            case EffectTypeTags.Tracer6:
            case EffectTypeTags.Tracer3:
            case EffectTypeTags.Nano0:
            case EffectTypeTags.ShockWave:
            case EffectTypeTags.VulcanRocks:
            case EffectTypeTags.VolGrid:
            case EffectTypeTags.Sprite:
            case EffectTypeTags.SkyFlash:
            case EffectTypeTags.Trail2:
            case EffectTypeTags.Toggle:
                return true;
            case EffectTypeTags.Spell1:
                return record.FieldInt(33, 0) != 0;
            case EffectTypeTags.Stars:
                return record.FieldInt(10, 0) is 2 or 3 or 4 or 5 or 6 or 7 or 8 or 9 or 10 or 11 or 12 or 13 or 14 or 15 or 16 or 17 or 18 or 19 or 20 or 22 or 28;
            default:
                return false;
        }
    }

    /// <summary>Child effect ids, as the port's composite controls read them.</summary>
    public static IEnumerable<int> Children(GfxTweakRecord record)
    {
        switch (record.TypeCode)
        {
            case EffectTypeTags.BuffFsm:
            case EffectTypeTags.BuffPlaceHolder:
                // Both spawn fields 10 and 11 (100d411d / 100d58f8).
                for (int i = 10; i <= 11; i++)
                {
                    int id = record.FieldInt(i, 0);
                    if (id > 0 && id != record.Id)
                        yield return id;
                }
                break;
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
            case EffectTypeTags.Toggle:
                // Both run field 2 (Toggle: 1011265a).
                if (record.FieldInt(2, 0) > 0)
                    yield return record.FieldInt(2, 0);
                break;
            case EffectTypeTags.Scatter:
                if (record.FieldInt(10, 0) > 0)
                    yield return record.FieldInt(10, 0);
                break;
            case EffectTypeTags.Tracer3:
                // 100ff6e2: the child it carries.
                if (record.FieldInt(15, 0) > 0)
                    yield return record.FieldInt(15, 0);
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
