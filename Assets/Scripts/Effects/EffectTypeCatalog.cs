using System.Collections.Generic;

/// <summary>How a stock typeCode presents itself, used to pick a fallback and to triage gaps.</summary>
public enum EffectCategory
{
    /// <summary>No stock control is constructible for this typeCode; drawing anything is a bug.</summary>
    None,
    /// <summary>Spawns child effects (Meta / Sequencer / Delay / MetaStatic).</summary>
    Composite,
    /// <summary>Fixed-size array of billboards driven by a closed-form curve (Stars / Suns).</summary>
    PointCloud,
    /// <summary>One or a few locator-anchored billboards (Flare / Sprite / Cord / Nano*).</summary>
    Sprite,
    /// <summary>Pooled emitter with per-particle integration (Sparks / *Particle*).</summary>
    Particle,
    /// <summary>Geometry attached to a dynel or the world (Mesh / Shield / GroundRing / *Grid).</summary>
    Mesh,
    /// <summary>Stretched geometry between two points, created only via CreateGfxControlTracer.</summary>
    Tracer,
    /// <summary>Full-screen or camera post effect (WhiteOut / NightVision / GroundShake).</summary>
    Screen,
    /// <summary>Sound only — never renders.</summary>
    Audio,
    /// <summary>Buff placeholder bookkeeping attached to a character.</summary>
    Buff,
    /// <summary>A stock control exists but we have not identified its class or behaviour yet.</summary>
    Unidentified,
}

/// <summary>Whether our client reproduces the stock control.</summary>
public enum EffectSupport
{
    /// <summary>Dedicated control ported from the stock Process().</summary>
    Ported,
    /// <summary>Rendered by a generic stand-in; mechanics are not stock-exact.</summary>
    Approximated,
    /// <summary>No control yet — effect is skipped.</summary>
    Missing,
    /// <summary>Intentionally not rendered (audio, buff bookkeeping, or no stock control).</summary>
    NotRendered,
}

public readonly struct EffectTypeInfo
{
    public readonly int TypeCode;
    /// <summary>Stock C++ class, or null when the class has not been identified yet.</summary>
    public readonly string StockClass;
    public readonly EffectCategory Category;
    public readonly EffectSupport Support;

    public EffectTypeInfo(int typeCode, string stockClass, EffectCategory category, EffectSupport support)
    {
        TypeCode = typeCode;
        StockClass = stockClass;
        Category = category;
        Support = support;
    }

    public string Describe()
        => $"typeCode={TypeCode} (0x{TypeCode:X}) {StockClass ?? "unidentified"} [{Category}/{Support}]";
}

/// <summary>
/// Stock gfxtweak typeCode inventory, recovered from the _EffectHandler_t::CreateGfxControl
/// overloads in Gamecode.dll. Class names are taken from the vftable each ctor installs;
/// entries with a null class name have a stock control we have not identified yet.
///
/// No Unity dependency so this can be asserted from plain unit tests.
/// </summary>
public static class EffectTypeCatalog
{
    static readonly Dictionary<int, EffectTypeInfo> Table = Build();

    public static bool TryGet(int typeCode, out EffectTypeInfo info) => Table.TryGetValue(typeCode, out info);

    public static IEnumerable<EffectTypeInfo> All => Table.Values;

    public static EffectCategory CategoryOf(int typeCode)
        => Table.TryGetValue(typeCode, out EffectTypeInfo info) ? info.Category : EffectCategory.None;

    public static EffectSupport SupportOf(int typeCode)
        => Table.TryGetValue(typeCode, out EffectTypeInfo info) ? info.Support : EffectSupport.Missing;

    /// <summary>True when skipping the effect is correct rather than a missing feature.</summary>
    public static bool IsIntentionallyNotRendered(int typeCode)
        => Table.TryGetValue(typeCode, out EffectTypeInfo info) && info.Support == EffectSupport.NotRendered;

    public static string Describe(int typeCode)
        => Table.TryGetValue(typeCode, out EffectTypeInfo info)
            ? info.Describe()
            : $"typeCode={typeCode} (0x{typeCode:X}) not in stock dispatch";

    static Dictionary<int, EffectTypeInfo> Build()
    {
        var t = new Dictionary<int, EffectTypeInfo>();

        void Add(int code, string cls, EffectCategory cat, EffectSupport sup)
            => t[code] = new EffectTypeInfo(code, cls, cat, sup);

        // ---- Composites -------------------------------------------------------
        Add(EffectTypeTags.Meta, "_GfxControlMeta_t", EffectCategory.Composite, EffectSupport.Ported);
        Add(EffectTypeTags.Sequencer, "_GfxControlSequencer_t", EffectCategory.Composite, EffectSupport.Ported);
        Add(EffectTypeTags.Delay, "_GfxControlDelay_t", EffectCategory.Composite, EffectSupport.Ported);
        Add(0xbc3, "_GfxControlMetaStatic_t", EffectCategory.Composite, EffectSupport.Missing);
        // Scatter is a spawner, not a particle system: N timed copies of the field-10 child.
        Add(EffectTypeTags.Scatter, "GfxControlScatter_t", EffectCategory.Composite, EffectSupport.Ported);

        // ---- Point clouds -----------------------------------------------------
        // Process is FUN_100f8491 (vftable 1016ddac slot 1), not FUN_100f826a. Only starType 3 is
        // recovered call-for-call (StarsCase3); every other starType is still an approximation.
        Add(EffectTypeTags.Stars, "_GfxControlStars_t", EffectCategory.PointCloud, EffectSupport.Ported);
        Add(EffectTypeTags.Suns, "_GfxControlSuns_t", EffectCategory.PointCloud, EffectSupport.Ported);

        // ---- Sprite family ----------------------------------------------------
        // Flare: GfxControlFlareType0 / FlareType0Sim, from Process 100ddc6d, spawn 100dd2eb and
        // DisplaySystem GfxVisualFlareType0 (NewSprite 100130a4, ProcessSprites 100131f3, quad 1001364b).
        Add(EffectTypeTags.Flare, "_GfxControlFlare_t", EffectCategory.Sprite, EffectSupport.Ported);
        // Flare1 (ctor 100dea3d) is a separate class still on the earlier emitter; not checked.
        Add(EffectTypeTags.FlareAlt, "_GfxControlFlare1_t", EffectCategory.Sprite, EffectSupport.Ported);
        // Cord: stock adds links only in slot 4 in local mode (field 0 bit 1), so a Cord without that
        // bit never draws — verified. The local-mode link model is still the earlier port's.
        Add(EffectTypeTags.Cord, "_GfxControlCord_t", EffectCategory.Sprite, EffectSupport.Ported);
        Add(EffectTypeTags.SpriteAlt, null, EffectCategory.Sprite, EffectSupport.Approximated);
        Add(EffectTypeTags.Nano0, "_GfxControlNano0_t", EffectCategory.Sprite, EffectSupport.Approximated);
        Add(EffectTypeTags.Nano1, "_GfxControlNano1_t", EffectCategory.Sprite, EffectSupport.Approximated);
        Add(EffectTypeTags.Nano2, "_GfxControlNano2_t", EffectCategory.Sprite, EffectSupport.Approximated);
        Add(EffectTypeTags.Nano3, "_GfxControlNano3_t", EffectCategory.Sprite, EffectSupport.Approximated);
        Add(EffectTypeTags.Sprite, "_GfxControlSprite_t", EffectCategory.Sprite, EffectSupport.Approximated);
        // Spell1: windows 1-2, NextState, SetDuration and terminate from 100f3f59 and friends; windows
        // 3-4 (field 33 = 0 only) are still the earlier model.
        Add(EffectTypeTags.Spell1, "_GfxControlSpell1_t", EffectCategory.Sprite, EffectSupport.Ported);

        // ---- Particle emitters ------------------------------------------------
        // Same pooled emitter as Flare (FUN_100f1997) plus gravity at field 40.
        Add(EffectTypeTags.Sparks, "_GfxControlSparks_t", EffectCategory.Particle, EffectSupport.Ported);
        // Both render a stand-in billboard set rather than the stock per-particle integration.
        Add(EffectTypeTags.TParticle, null, EffectCategory.Particle, EffectSupport.Approximated);
        Add(EffectTypeTags.BParticle2, null, EffectCategory.Particle, EffectSupport.Approximated);
        Add(0xbd0, "GfxControlBParticle_t", EffectCategory.Particle, EffectSupport.Missing);
        Add(0xbd3, "GfxControlMParticle_t", EffectCategory.Particle, EffectSupport.Missing);
        Add(0xbd7, "GfxControlTParticle2_t", EffectCategory.Particle, EffectSupport.Missing);
        Add(0xbdb, "GfxControlAParticle_t", EffectCategory.Particle, EffectSupport.Missing);
        Add(0xbc9, "_GfxControlGlobalSmoke_t", EffectCategory.Particle, EffectSupport.Missing);
        Add(0xbcb, "_GfxControlTrail_t", EffectCategory.Particle, EffectSupport.Missing);
        Add(0xbdf, "GfxControlTrail2_t", EffectCategory.Particle, EffectSupport.Missing);

        // ---- Mesh / geometry --------------------------------------------------
        Add(EffectTypeTags.Highlight, "_GfxControlHighlight_t", EffectCategory.Mesh, EffectSupport.Ported);
        Add(EffectTypeTags.Shield, "_GfxControlShield_t", EffectCategory.Mesh, EffectSupport.Ported);
        Add(EffectTypeTags.Shield2, "GfxControlShield2_t", EffectCategory.Mesh, EffectSupport.Missing);
        Add(0xbc2, "_GfxControlMesh_t", EffectCategory.Mesh, EffectSupport.Missing);
        Add(0xbd1, "GfxControlEffectMesh_t", EffectCategory.Mesh, EffectSupport.Missing);
        Add(0xbce, "GfxControlBeam_t", EffectCategory.Mesh, EffectSupport.Missing);
        Add(0xbcf, "GfxControlEnergyBall_t", EffectCategory.Mesh, EffectSupport.Missing);
        Add(0xbc1, "_GfxControlCrazyCone_t", EffectCategory.Mesh, EffectSupport.Missing);
        Add(0xbc4, "_GfxControlGroundRing_t", EffectCategory.Mesh, EffectSupport.Missing);
        Add(0xbd6, "GfxControlGroundGrid_t", EffectCategory.Mesh, EffectSupport.Missing);
        Add(0xbde, "GfxControlVolGrid_t", EffectCategory.Mesh, EffectSupport.Missing);
        Add(0xbb8, "_GfxControlShockWave_t", EffectCategory.Mesh, EffectSupport.Missing);
        Add(0xbba, "_GfxControlSplash_t", EffectCategory.Mesh, EffectSupport.Missing);
        Add(EffectTypeTags.Deformer, "_GfxControlDeformer_t", EffectCategory.Mesh, EffectSupport.Ported);
        Add(0x7d1, "_GfxControlSpiral_t", EffectCategory.Mesh, EffectSupport.Missing);
        Add(0xbd9, "GfxControlSpiral2_t", EffectCategory.Mesh, EffectSupport.Missing);
        Add(EffectTypeTags.Electra, "_GfxControlElectra_t", EffectCategory.Mesh, EffectSupport.Ported);
        Add(0xbca, "_GfxControlSkyRise_t", EffectCategory.Mesh, EffectSupport.Missing);
        Add(0xbbe, "_GfxControlSkyFlash_t", EffectCategory.Mesh, EffectSupport.Missing);
        Add(0x405, "_GfxControlVulcanRocks_t", EffectCategory.Mesh, EffectSupport.Missing);
        Add(0xbc0, "GfxControlShadow_c", EffectCategory.Mesh, EffectSupport.Missing);

        // ---- Screen / camera --------------------------------------------------
        Add(0xbd8, "GfxControlGroundShake_t", EffectCategory.Screen, EffectSupport.Missing);
        Add(0xbdc, "GfxControlToggle_t", EffectCategory.Screen, EffectSupport.Missing);

        // ---- Tracers: only reachable through CreateGfxControlTracer(id, from, to) ----
        Add(EffectTypeTags.BeamCylinder, null, EffectCategory.Tracer, EffectSupport.Approximated);
        Add(EffectTypeTags.Tracer4, "_GfxControlTracer4_t", EffectCategory.Tracer, EffectSupport.Ported);
        Add(EffectTypeTags.BeamRibbonAlt, null, EffectCategory.Tracer, EffectSupport.Approximated);
        Add(EffectTypeTags.BeamRibbonWide, null, EffectCategory.Tracer, EffectSupport.Approximated);
        Add(EffectTypeTags.Tracer1, "_GfxControlTracer1_t", EffectCategory.Tracer, EffectSupport.Ported);
        Add(0x3fd, null, EffectCategory.Tracer, EffectSupport.Missing);
        Add(EffectTypeTags.HitSpawner, null, EffectCategory.Tracer, EffectSupport.Missing);
        Add(0x403, null, EffectCategory.Tracer, EffectSupport.Missing);
        Add(0xbd2, null, EffectCategory.Tracer, EffectSupport.Missing);

        // ---- Constructed by a CreateGfxControl overload we have not mapped yet ----
        Add(EffectTypeTags.Plasma, "_GfxControlPlasma_t", EffectCategory.Tracer, EffectSupport.Ported);

        // ---- Never rendered ---------------------------------------------------
        Add(0xfa0, "_GfxControlAudio_t", EffectCategory.Audio, EffectSupport.NotRendered);
        Add(0x3e9, "_GfxControlBPHFSM_t", EffectCategory.Buff, EffectSupport.NotRendered);
        Add(0x3ea, "_GfxControlBuffPlaceHolder_t", EffectCategory.Buff, EffectSupport.NotRendered);
        // Stock CreateGfxControl explicitly returns null for typeCode 0.
        Add(0, null, EffectCategory.None, EffectSupport.NotRendered);

        return t;
    }
}
