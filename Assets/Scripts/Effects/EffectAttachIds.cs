/// <summary>
/// Stock AO attach ids used by gfx locators.
/// Attractor/bone string tables match Gamecode FUN_10105c44 .rdata (0x102c63f8 / 0x102c63a8).
/// Attach 0 = CAT mesh RRefFrame (FUN_10106368), not dynel feet — see VisualDynel.TryGetMeshFrameMatrix.
/// </summary>
public static class EffectAttachIds
{
    /// <summary>Stock default: keep mesh RRefFrame (mid-body). Not a bone/attractor id.</summary>
    public const int MeshFrame = 0;

    public const int BoneMin = 1000;
    public const int BoneMax = 1018;
    public const int AttractorMin = 2000;
    public const int AttractorMax = 2023;
    /// <summary>A weapon's muzzle (stock <c>10105ea1</c>): only a WeaponItem_t has one (<c>1009c337</c>); on a character the
    /// lookup fails and the locator keeps the mesh frame.</summary>
    public const int Muzzle = 3000;

    /// <summary>What a missing attractor or bone falls back to (stock <c>0x102c6400</c>).</summary>
    public const string HeadAttractorName = "Attractor01_head";

    // Stock attractor jump table (attachId - 2000).
    public const int RightHand = 2000; // Attractor02_righthand
    public const int LeftHand = 2001;  // Attractor03_lefthand
    public const int Head = 2002;      // Attractor01_head
    public const int Back = 2003;
    public const int LeftShoulder = 2004;
    public const int RightShoulder = 2005;

    // Stock Bip01 bone jump table (attachId - 1000).
    public const int BonePelvis = 1000;
    public const int BoneSpine = 1001;
    public const int BoneSpine1 = 1002;
    public const int BoneSpine2 = 1003; // chest — Spell1 locator C primary
    public const int BoneSpine3 = 1004;
    public const int BoneNeck = 1005;
    public const int BoneHead = 1006;
    public const int BoneLUpperArm = 1007;
    public const int BoneRUpperArm = 1008;
    public const int BoneLForearm = 1009;
    public const int BoneRForearm = 1010;
    public const int BoneLThigh = 1011;
    public const int BoneRThigh = 1012;
    public const int BoneLCalf = 1013;
    public const int BoneRCalf = 1014;
    public const int BoneLFoot = 1015;
    public const int BoneRFoot = 1016;
    public const int BoneLHand = 1017;
    public const int BoneRHand = 1018;

    static readonly string[] AttractorNames =
    {
        "Attractor02_righthand",
        "Attractor03_lefthand",
        "Attractor01_head",
        "Attractor06_back",
        "Attractor05_leftshoulder",
        "Attractor04_rightshoulder",
        "Attractor07_special",
        "Attractor08_special",
        "Attractor09_special",
        "Attractor10_special",
        "Attractor11_special",
        "Attractor12_attack1",
        "Attractor13_attack2",
        "Attractor14_destroyed",
        "Attractor15_flare1_flash",
        "Attractor16_flare2_flash",
        "Attractor17_smoke75",
        "Attractor18_smoke50",
        "Attractor19_smoke25",
        "Attractor20_sparks50",
        "Attractor21_flames15",
        "Attractor22_flare1",
        "Attractor23_flare2",
        "Attractor30_beam",
    };

    static readonly string[] BoneNames =
    {
        "Bip01 Pelvis_ac",
        "Bip01 Spine_ac",
        "Bip01 Spine1_ac",
        "Bip01 Spine2_ac",
        "Bip01 Spine3_ac",
        "Bip01 Neck_ac",
        "Bip01 Head_ac",
        "Bip01 L UpperArm_ac",
        "Bip01 R UpperArm_ac",
        "Bip01 L Forearm_ac",
        "Bip01 R Forearm_ac",
        "Bip01 L Thigh_ac",
        "Bip01 R Thigh_ac",
        "Bip01 L Calf_ac",
        "Bip01 R Calf_ac",
        "Bip01 L Foot_ac",
        "Bip01 R Foot_ac",
        "Bip01 L Hand_ac",
        "Bip01 R Hand_ac",
    };

    public static bool IsBone(int id) => id >= BoneMin && id <= BoneMax;
    public static bool IsAttractor(int id) => id >= AttractorMin && id <= AttractorMax;
    public static bool IsMuzzle(int id) => id == Muzzle;

    public static bool TryGetAttractorName(int attachId, out string name)
    {
        name = null;
        if (!IsAttractor(attachId))
            return false;
        int index = attachId - AttractorMin;
        if (index < 0 || index >= AttractorNames.Length)
            return false;
        name = AttractorNames[index];
        return true;
    }

    public static bool TryGetBoneName(int attachId, out string name)
    {
        name = null;
        if (!IsBone(attachId))
            return false;
        int index = attachId - BoneMin;
        if (index < 0 || index >= BoneNames.Length)
            return false;
        name = BoneNames[index];
        return true;
    }

}
