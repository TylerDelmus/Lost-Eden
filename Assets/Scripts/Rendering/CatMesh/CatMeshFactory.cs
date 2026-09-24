using System;
using System.Collections.Generic;
using System.IO;
using AODB.Common.RDBObjects;
using UnityEngine;
using UnityEngine.Rendering;
using AoColor = AODB.Common.Structs.Color;

public static class CatMeshFactory
{
    /// <summary>
    /// The subset of <paramref name="skeleton"/> that <paramref name="weights"/> actually reference
    /// (any non-zero weight), with the weights re-indexed into that subset and the bind poses picked
    /// to match. Joints keep their skeleton order. When the inputs do not line up — no weights, or
    /// bind poses that are not one per joint — everything is passed through unchanged.
    /// </summary>
    public static void CompactBones(
        BoneWeight[] weights,
        Transform[] skeleton,
        Matrix4x4[] bindPoses,
        out BoneWeight[] compactWeights,
        out Transform[] compactBones,
        out Matrix4x4[] compactBindPoses)
    {
        compactWeights = weights;
        compactBones = skeleton;
        compactBindPoses = bindPoses;

        if (weights == null || weights.Length == 0 || skeleton == null || bindPoses == null
            || bindPoses.Length != skeleton.Length)
            return;

        int jointCount = skeleton.Length;
        var remap = new int[jointCount];
        for (int j = 0; j < jointCount; j++)
            remap[j] = -1;

        bool outOfRange = false;
        for (int v = 0; v < weights.Length; v++)
        {
            BoneWeight w = weights[v];
            if (w.weight0 > 0f) Mark(w.boneIndex0);
            if (w.weight1 > 0f) Mark(w.boneIndex1);
            if (w.weight2 > 0f) Mark(w.boneIndex2);
            if (w.weight3 > 0f) Mark(w.boneIndex3);
        }

        int used = 0;
        for (int j = 0; j < jointCount; j++)
        {
            if (remap[j] >= 0)
                remap[j] = used++;
        }

        // A weight index outside the skeleton means the data does not describe this skeleton; leave
        // it exactly as it was rather than guess.
        if (outOfRange || used == 0 || used == jointCount)
            return;

        compactBones = new Transform[used];
        compactBindPoses = new Matrix4x4[used];
        for (int j = 0; j < jointCount; j++)
        {
            if (remap[j] < 0)
                continue;
            compactBones[remap[j]] = skeleton[j];
            compactBindPoses[remap[j]] = bindPoses[j];
        }

        compactWeights = new BoneWeight[weights.Length];
        for (int v = 0; v < weights.Length; v++)
        {
            BoneWeight w = weights[v];
            // A zero-weight slot may point at a joint that was dropped; any valid index will do.
            w.boneIndex0 = w.weight0 > 0f ? remap[w.boneIndex0] : 0;
            w.boneIndex1 = w.weight1 > 0f ? remap[w.boneIndex1] : 0;
            w.boneIndex2 = w.weight2 > 0f ? remap[w.boneIndex2] : 0;
            w.boneIndex3 = w.weight3 > 0f ? remap[w.boneIndex3] : 0;
            compactWeights[v] = w;
        }

        void Mark(int joint)
        {
            if (joint >= 0 && joint < jointCount)
                remap[joint] = 0;
            else
                outOfRange = true;
        }
    }

    public static Mesh CreateSkinnedMesh(
        CatMeshSubmeshSource source,
        BoneWeight[] boneWeights,
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

        if (boneWeights != null && boneWeights.Length == count)
            mesh.boneWeights = boneWeights;

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
