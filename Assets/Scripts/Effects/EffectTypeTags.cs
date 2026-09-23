/// <summary>
/// Stock gfxtweak typeCodes. Dispatch on these, never on effect id.
/// </summary>
public static class EffectTypeTags
{
    // Billboard / sprite family
    public const int Cord = 0x3eb;       // 1003 — _GfxControlCord_t (hand cord / soft FX)
    public const int Fire = 0x3ec;       // 1004 — _GfxControlFire_t, Sprite2Type0 flames rising off a disc
    public const int Flare = 0x3ed;       // 1005 — _GfxControlFlare_t
    public const int FlareAlt = 0x3ee;    // 1006
    public const int Nano0 = 0x3ef;      // 1007 — _GfxControlNano0_t
    public const int Nano1 = 0x3f0;      // 1008 — _GfxControlNano1_t
    public const int Smoke = 0x3f1;      // 1009 — _GfxControlSmoke_t, Sprite2Type0 puffs off a disc
    public const int Spell1 = 0x3f2;     // 1010 — _GfxControlSpell1_t multi-locator
    public const int Nano3 = 0x3f3;      // 1011 — _GfxControlNano3_t
    public const int Sprite = 0x3f4;     // 1012 — _GfxControlSprite_t
    public const int Sparks = 0x3fa;     // 1018 — Sprite2Type0 (not particles)
    public const int Tracer1 = 0x3fb;    // 1019 — _GfxControlTracer1_t, the nano projectile
    public const int Plasma = 0x7d2;     // 2002 — _GfxControlPlasma_t, energy strip between two points
    public const int Tracer5 = 0x401;    // 1025 — _GfxControlTracer5_t, one flying streak
    public const int Tracer4 = 0x400;    // 1024 — _GfxControlTracer4_t, three twisting ribbons
    public const int Deformer = 0xbb9;   // 3001 — _GfxControlDeformer_t, moves the host's mesh vertices
    public const int Electra = 0x7d6;    // 2006 — _GfxControlElectra_t, shell of flat sparks
    public const int Suns = 0x7d5;       // 2005 — _GfxControlSuns_t, GfxVisualSol sparks
    public const int Spiral = 0x7d1;     // 2001 — _GfxControlSpiral_t, a double-helix ribbon round the locator
    public const int Spiral2 = 0xbd9;    // 3033 — GfxControlSpiral2_t, N helix ribbons on a spinning-up axis
    public const int Beam = 0xbce;       // 3022 — GfxControlBeam_t, a star of flat blades on a locator
    public const int CrazyCone = 0xbc1;  // 3009 — _GfxControlCrazyCone_t, a nest of cones driven by stages
    public const int GroundRing = 0xbc4; // 3012 — _GfxControlGroundRing_t, an annulus laid over the terrain
    public const int Mesh = 0xbc2;       // 3010 — _GfxControlMesh_t, one of four tower wrecks on the locator
    public const int BuffFsm = 0x3e9;    // 1001 — _GfxControlBPHFSM_t, re-spawns two effects on the host
    public const int BuffPlaceHolder = 0x3ea; // 1002 — _GfxControlBuffPlaceHolder_t, swings two children round an attach

    // Legacy aliases used by older call sites
    public const int NanoSprite = Nano0;

    // Composites / sequencing
    public const int Meta = 0x7d7;       // 2007 — _GfxControlMeta_t
    public const int Sequencer = 0xbbc;  // 3004 — _GfxControlSequencer_t
    public const int Delay = 0xbc5;      // 3013
    public const int Scatter = 0xbd5;    // 3029 — GfxControlScatter_t, N timed child spawns — _GfxControlDelay_t

    // Point cloud / particles
    public const int Stars = 0x7d4;      // 2004 — _GfxControlStars_t
    public const int TParticle = 0xbcc;  // 3020
    public const int TParticle2 = 0xbd7; // 3031 — GfxControlTParticle2_t, streaks from an emitter
    public const int BParticle2 = 0xbd4; // 3028
    public const int BParticle = 0xbd0;  // 3024 — GfxControlBParticle_t (particle mode 8 only)
    public const int GroundGrid = 0xbd6; // 3030 — GfxControlGroundGrid_t (visual mode 0 only)
    public const int EffectMesh = 0xbd1; // 3025 — GfxControlEffectMesh_t, one ABIFF model
    public const int MParticle = 0xbd3;  // 3027 — GfxControlMParticle_t, ABIFF debris

    // Shields
    public const int Shield = 0xbbb;     // 3003
    public const int Shield2 = 0xbda;    // 3034
    public const int GroundShake = 0xbd8; // 3032 — GfxControlGroundShake_t, a camera shake

    /// <summary>typeCode 2011 (0x7db) — _GfxControlHighlight_t mesh emissive/transparency tint.</summary>
    public const int Highlight = 0x7db;

    // Beams (stub)
    public const int BeamCylinder = 0x3f5;
    public const int BeamRibbon = 0x400;
    public const int BeamRibbonAlt = 0x401;
    public const int BeamRibbonWide = 0x402;

    /// <summary>1026 — _GfxControlTracer6_t: a widening sprite trail plus three Cord4 ribbons.</summary>
    public const int Tracer6 = 0x402;
    public const int Tracer3 = 0x3fe;        // 1022 — _GfxControlTracer3_t (carries a child along the hit line)
    public const int Tracer8 = 0xbd2;        // 3026 — GfxControlTracer8_t (carries a whole child effect along the hit line)
    public const int EnergyBall = 0xbcf;     // 3023 — GfxControlEnergyBall_t (three orthogonal fans of blades)
    public const int GlobalSmoke = 0xbc9;    // 3017 — _GfxControlGlobalSmoke_t (64 Sol sprites from a steady emitter)
    public const int GroundImpact = 0x1388;  // 5000 — _GfxControlGroundImpact_c (a jittering blue blade, GfxVisualForceSword_t)
    public const int ShockWave = 0xbb8;      // 3000 — _GfxControlShockWave_t (ground rings and cones)
    public const int VulcanRocks = 0x405;    // 1029 — _GfxControlVulcanRocks_t (rocks thrown up, bouncing to rest)
    public const int VolGrid = 0xbde;        // 3038 — GfxControlVolGrid_t (a box of crossed textured slices)
    public const int SkyFlash = 0xbbe;       // 3006 — _GfxControlSkyFlash_t (a column of nested cones of light)
    public const int Toggle = 0xbdc;         // 3036 — GfxControlToggle_t (runs one child while its conditions hold)
    public const int Trail2 = 0xbdf;         // 3039 — GfxControlTrail2_t (a ribbon trail of the locator's frame)

    public const int RejectedEffectId = 49999;

    // Backward-compat names
    public const int Spawner = Meta;
    public const int TimedSpawner = Sequencer;
    public const int HitSpawner = Tracer3;

    public static bool IsSpriteFamily(int typeCode)
        => typeCode == Cord || typeCode == Fire
           || typeCode == Flare || typeCode == FlareAlt
           || typeCode == Nano0 || typeCode == Nano1 || typeCode == Smoke || typeCode == Nano3
           || typeCode == Sprite || typeCode == Sparks;

    public static bool IsBillboard(int typeCode) => IsSpriteFamily(typeCode);

    public static bool IsComposite(int typeCode)
        => typeCode == Meta || typeCode == Sequencer || typeCode == Delay || typeCode == Scatter;

    public static bool IsSpawner(int typeCode) => IsComposite(typeCode);

    public static bool IsBeam(int typeCode)
        => typeCode == BeamCylinder || typeCode == BeamRibbon
           || typeCode == BeamRibbonAlt || typeCode == BeamRibbonWide;

    public static bool IsStars(int typeCode) => typeCode == Stars;

    public static bool IsParticle(int typeCode)
        => typeCode == TParticle || typeCode == TParticle2 || typeCode == BParticle2;

    public static bool IsHighlight(int typeCode) => typeCode == Highlight;
}
