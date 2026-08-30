using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Evaluated AO playfield environment data for HDRP (fog/sky meshes).
/// Removable with the rest of <c>Playfield/AoTweak</c>.
/// Lost Eden JSON overlays (primary/companion sun, etc.) load separately via
/// <see cref="LostEdenTweakCatalog"/>.
/// </summary>
public sealed class PlayfieldEnvironmentTweak
{
    public bool HasLighting;
    public float DayTimeFactor;
    public Quaternion SunRotation = Quaternion.identity;
    public Color GroundLightColor = Color.white;
    public Color AmbientLightColor = new Color(0.2f, 0.2f, 0.2f);
    public bool HasFog;
    public Color FogTint = Color.gray;
    public float FogDensityHint = 0.025f;
    /// <summary>Resolved GAME.* float variables (e.g. ThickCloudsIntensity).</summary>
    public readonly Dictionary<string, float> GameVariables = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
    public readonly List<AoSkyMeshPlacement> SkyMeshes = new List<AoSkyMeshPlacement>();
}

public struct AoSkyMeshPlacement
{
    public string ObjectName;
    public string MeshName;
    /// <summary>AO tweak Position offset (e_LockToCamera).</summary>
    public Vector3 PositionOffset;
    public Quaternion LocalRotation;
    public float Scale;
    /// <summary>Evaluated opacity/intensity from tweak TFACTOR / Intensity / GAME vars.</summary>
    public float Intensity;
    /// <summary>Whether to spawn; from AO Use, overridable by LE objects.*.enabled.</summary>
    public bool Enabled;
}
