using System;
using System.Collections.Generic;
using System.IO;
using AODB.Common.RDBObjects;
using UnityEngine;
using UnityEngine.Rendering;
using AoColor = AODB.Common.Structs.Color;

public static class CatMeshFactory
{
    public static Mesh CreateSkinnedMesh(
        CatMeshSubmeshSource source,
        Matrix4x4[] bindPoses,
        string name)
    {
        if (source == null || source.Positions == null || source.Positions.Length == 0)
            return null;

        int count = source.Positions.Length;
        var mesh = new Mesh
        {
            name = name,
            indexFormat = count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
        };

        mesh.SetVertices(source.Positions);
        mesh.SetNormals(source.Normals);
        mesh.SetUVs(0, source.UVs);
        mesh.SetTriangles(source.Triangles ?? Array.Empty<int>(), 0, calculateBounds: false);

        if (source.BoneWeights != null && source.BoneWeights.Length == count)
            mesh.boneWeights = source.BoneWeights;

        if (bindPoses != null && bindPoses.Length > 0)
            mesh.bindposes = bindPoses;

        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        mesh.UploadMeshData(markNoLongerReadable: true);
        return mesh;
    }
}

public sealed class CatMeshMaterialFactory
{
    readonly AbiffMaterialFactory _materials;

    public CatMeshMaterialFactory(AbiffMaterialFactory materials)
    {
        _materials = materials;
    }

    public Material Get(AbiffMaterialDesc desc) => _materials.Get(desc);

    public Material Get(RDBCatMesh.Material source, IReadOnlyDictionary<string, int> textureIds)
    {
        return _materials.Get(CreateDesc(source, textureIds));
    }

    public static Dictionary<string, int> BuildTextureLookup(IReadOnlyList<RDBCatMesh.Texture> textures)
    {
        var lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (textures == null)
            return lookup;

        for (int i = 0; i < textures.Count; i++)
        {
            RDBCatMesh.Texture texture = textures[i];
            if (texture == null || string.IsNullOrEmpty(texture.Name) || texture.Texture1 <= 0)
                continue;

            lookup.TryAdd(texture.Name, texture.Texture1);
        }

        return lookup;
    }

    public static AbiffMaterialDesc CreateDesc(
        RDBCatMesh.Material source,
        IReadOnlyDictionary<string, int> textureIds,
        string fallbackName = "CatMeshMat")
    {
        AbiffMaterialDesc desc = AbiffMaterialDesc.CreateDefault();
        desc.Name = string.IsNullOrEmpty(source?.Name) ? fallbackName : source.Name;

        if (source == null)
            return desc;

        float alpha = Mathf.Clamp01(source.SheenOpacity);
        desc.Diffuse = ToUnityColor(source.Diffuse, alpha);
        desc.Emissive = ToUnityColor(source.Emission, 1f);
        desc.Shininess = source.Sheen;
        desc.SpecularEnabled = source.Sheen > 0f;
        desc.ApplyAlpha = alpha < 0.99f;

        if (TryResolveTextureId(textureIds, source.Name, out int diffuseId))
            desc.DiffuseTextureId = diffuseId;
        else if (TryResolveTextureId(textureIds, source.TextureName, out diffuseId))
            desc.DiffuseTextureId = diffuseId;

        // Textured materials should not be tinted by the RDB diffuse color.
        if (desc.DiffuseTextureId > 0)
            desc.Diffuse = new Color(1f, 1f, 1f, alpha);

        if (TryResolveTextureId(textureIds, source.EnvTextureName, out int envId))
            desc.EmissionTextureId = envId;

        return desc;
    }

    static bool TryResolveTextureId(IReadOnlyDictionary<string, int> textureIds, string key, out int textureId)
    {
        textureId = 0;
        if (textureIds == null || string.IsNullOrEmpty(key))
            return false;

        if (textureIds.TryGetValue(key, out textureId))
            return textureId > 0;

        string fileName = Path.GetFileName(key);
        if (!string.Equals(fileName, key, StringComparison.OrdinalIgnoreCase)
            && textureIds.TryGetValue(fileName, out textureId))
        {
            return textureId > 0;
        }

        string withoutExtension = Path.GetFileNameWithoutExtension(key);
        if (!string.IsNullOrEmpty(withoutExtension)
            && textureIds.TryGetValue(withoutExtension, out textureId))
        {
            return textureId > 0;
        }

        return false;
    }

    static Color ToUnityColor(AoColor color, float alpha) =>
        new Color(color.R, color.G, color.B, alpha);
}
