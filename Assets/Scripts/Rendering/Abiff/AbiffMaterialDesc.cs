using System;
using UnityEngine;

/// <summary>
/// Snapshot of an RDBMesh FAFMaterial (+ delta-state) for HDRP material creation.
/// Mirrors Abiff/Assimp BuildMaterial handling.
/// </summary>
public struct AbiffMaterialDesc : IEquatable<AbiffMaterialDesc>
{
    public string Name;
    public Color Diffuse;
    public Color Emissive;
    public float Shininess;
    public bool TwoSided;
    public bool ApplyAlpha;
    public bool SpecularEnabled;
    public int DiffuseTextureId;
    public int EmissionTextureId;

    public static AbiffMaterialDesc CreateDefault()
    {
        return new AbiffMaterialDesc
        {
            Name = "AbiffFallback",
            Diffuse = Color.white,
            Emissive = Color.black,
            Shininess = 0f,
            TwoSided = false,
            ApplyAlpha = false,
            SpecularEnabled = true,
            DiffuseTextureId = -1,
            EmissionTextureId = -1
        };
    }

    public AbiffMaterialDesc WithDiffuseTexture(int textureId)
    {
        AbiffMaterialDesc copy = this;
        copy.DiffuseTextureId = textureId;
        return copy;
    }

    public bool Equals(AbiffMaterialDesc other)
    {
        return DiffuseTextureId == other.DiffuseTextureId
            && EmissionTextureId == other.EmissionTextureId
            && TwoSided == other.TwoSided
            && ApplyAlpha == other.ApplyAlpha
            && SpecularEnabled == other.SpecularEnabled
            && Shininess.Equals(other.Shininess)
            && Diffuse == other.Diffuse
            && Emissive == other.Emissive
            && string.Equals(Name, other.Name, StringComparison.Ordinal);
    }

    public override bool Equals(object obj) => obj is AbiffMaterialDesc other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = DiffuseTextureId * 397;
            hash = (hash * 397) ^ EmissionTextureId;
            hash = (hash * 397) ^ (TwoSided ? 1 : 0);
            hash = (hash * 397) ^ (ApplyAlpha ? 1 : 0);
            hash = (hash * 397) ^ (SpecularEnabled ? 1 : 0);
            hash = (hash * 397) ^ Shininess.GetHashCode();
            hash = (hash * 397) ^ Diffuse.GetHashCode();
            hash = (hash * 397) ^ Emissive.GetHashCode();
            hash = (hash * 397) ^ (Name?.GetHashCode() ?? 0);
            return hash;
        }
    }
}
