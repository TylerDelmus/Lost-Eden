using System;
using System.Collections.Generic;
using AODB.Common.RDBObjects;
using UnityEngine;
using AoVector2 = AODB.Common.Structs.Vector2;
using AoVector3 = AODB.Common.Structs.Vector3;

public sealed class CatMeshSubmeshSource
{
    public string GroupName;
    public Vector3[] Positions;
    public Vector3[] Normals;
    public Vector2[] UVs;
    public int[] Triangles;
    public BoneWeight[] BoneWeights;
    public AbiffMaterialDesc Material;
}

public static class CatMeshSnapshot
{
    public static CatMeshSubmeshSource[] FromRdbCatMesh(RDBCatMesh catMesh)
    {
        return FromRdbCatMesh(catMesh, bones: null, meshRoot: null);
    }

    /// <summary>
    /// When <paramref name="bones"/> is provided, rebuild vertex positions from RelToJoint
    /// (CirLoader GetVertexSkeletonPos) so skinning matches the animation rest pose.
    /// </summary>
    public static CatMeshSubmeshSource[] FromRdbCatMesh(
        RDBCatMesh catMesh,
        Transform[] bones,
        Transform meshRoot)
    {
        if (catMesh?.MeshGroups == null || catMesh.MeshGroups.Count == 0)
            return Array.Empty<CatMeshSubmeshSource>();

        Dictionary<string, int> textureIds = CatMeshMaterialFactory.BuildTextureLookup(catMesh.Textures);
        var submeshes = new List<CatMeshSubmeshSource>();

        for (int g = 0; g < catMesh.MeshGroups.Count; g++)
        {
            RDBCatMesh.MeshGroup group = catMesh.MeshGroups[g];
            if (group?.Meshes == null)
                continue;

            string groupName = string.IsNullOrEmpty(group.Name) ? $"Group_{g}" : group.Name;
            for (int m = 0; m < group.Meshes.Count; m++)
                submeshes.Add(SnapshotMesh(group.Meshes[m], catMesh.Materials, textureIds, groupName, g, m, bones, meshRoot, boneWorlds: null));
        }

        return submeshes.ToArray();
    }

    /// <summary>
    /// Thread-safe rest-pose snapshot using precomputed bone world matrices (no Unity Transforms).
    /// </summary>
    public static CatMeshSubmeshSource[] FromRdbCatMesh(
        RDBCatMesh catMesh,
        Matrix4x4[] boneWorldMatrices)
    {
        if (catMesh?.MeshGroups == null || catMesh.MeshGroups.Count == 0)
            return Array.Empty<CatMeshSubmeshSource>();

        Dictionary<string, int> textureIds = CatMeshMaterialFactory.BuildTextureLookup(catMesh.Textures);
        var submeshes = new List<CatMeshSubmeshSource>();

        for (int g = 0; g < catMesh.MeshGroups.Count; g++)
        {
            RDBCatMesh.MeshGroup group = catMesh.MeshGroups[g];
            if (group?.Meshes == null)
                continue;

            string groupName = string.IsNullOrEmpty(group.Name) ? $"Group_{g}" : group.Name;
            for (int m = 0; m < group.Meshes.Count; m++)
                submeshes.Add(SnapshotMesh(group.Meshes[m], catMesh.Materials, textureIds, groupName, g, m, bones: null, meshRoot: null, boneWorlds: boneWorldMatrices));
        }

        return submeshes.ToArray();
    }

    static CatMeshSubmeshSource SnapshotMesh(
        RDBCatMesh.Mesh mesh,
        List<RDBCatMesh.Material> materials,
        Dictionary<string, int> textureIds,
        string groupName,
        int groupIndex,
        int meshIndex,
        Transform[] bones,
        Transform meshRoot,
        Matrix4x4[] boneWorlds)
    {
        AbiffMaterialDesc material = ResolveMaterial(mesh?.MaterialId ?? -1, materials, textureIds, groupIndex, meshIndex);
        if (mesh?.Vertices == null || mesh.Vertices.Count == 0)
        {
            return new CatMeshSubmeshSource
            {
                GroupName = groupName,
                Positions = Array.Empty<Vector3>(),
                Normals = Array.Empty<Vector3>(),
                UVs = Array.Empty<Vector2>(),
                Triangles = Array.Empty<int>(),
                BoneWeights = Array.Empty<BoneWeight>(),
                Material = material
            };
        }

        int count = mesh.Vertices.Count;
        var positions = new Vector3[count];
        var normals = new Vector3[count];
        var uvs = new Vector2[count];
        var boneWeights = new BoneWeight[count];
        bool useMatrixVerts = boneWorlds != null && boneWorlds.Length > 0;
        bool useSkeletonVerts = !useMatrixVerts && bones != null && bones.Length > 0;

        for (int i = 0; i < count; i++)
        {
            RDBCatMesh.Vertex vertex = mesh.Vertices[i];

            if (useMatrixVerts)
            {
                positions[i] = GetVertexSkeletonPos(vertex, boneWorlds);
            }
            else if (useSkeletonVerts)
            {
                Vector3 world = GetVertexSkeletonPos(vertex, bones);
                positions[i] = meshRoot != null ? meshRoot.InverseTransformPoint(world) : world;
            }
            else
            {
                AoVector3 pos = vertex.Position;
                positions[i] = new Vector3(pos.X, pos.Y, pos.Z);
            }

            AoVector3 nrm = vertex.Normal;
            normals[i] = new Vector3(nrm.X, nrm.Y, nrm.Z);

            AoVector2 uv = vertex.Uvs;
            uvs[i] = new Vector2(uv.X, uv.Y);

            float w0 = Mathf.Clamp01(vertex.Joint1Weight);
            boneWeights[i] = new BoneWeight
            {
                boneIndex0 = vertex.Joint1,
                boneIndex1 = vertex.Joint2,
                weight0 = w0,
                weight1 = 1f - w0
            };
        }

        int[] triangles = mesh.Triangles != null
            ? (int[])mesh.Triangles.Clone()
            : Array.Empty<int>();

        return new CatMeshSubmeshSource
        {
            GroupName = groupName,
            Positions = positions,
            Normals = normals,
            UVs = uvs,
            Triangles = triangles,
            BoneWeights = boneWeights,
            Material = material
        };
    }

    static Vector3 GetVertexSkeletonPos(RDBCatMesh.Vertex vertex, Transform[] bones)
    {
        AoVector3 rel1 = vertex.RelToJoint1;
        AoVector3 rel2 = vertex.RelToJoint2;
        Vector3 local1 = new Vector3(rel1.X, rel1.Y, rel1.Z);
        Vector3 local2 = new Vector3(rel2.X, rel2.Y, rel2.Z);

        Vector3 world1 = TransformBonePoint(bones, vertex.Joint1, local1);
        Vector3 world2 = TransformBonePoint(bones, vertex.Joint2, local2);
        return Vector3.Lerp(world2, world1, Mathf.Clamp01(vertex.Joint1Weight));
    }

    static Vector3 GetVertexSkeletonPos(RDBCatMesh.Vertex vertex, Matrix4x4[] boneWorlds)
    {
        AoVector3 rel1 = vertex.RelToJoint1;
        AoVector3 rel2 = vertex.RelToJoint2;
        Vector3 local1 = new Vector3(rel1.X, rel1.Y, rel1.Z);
        Vector3 local2 = new Vector3(rel2.X, rel2.Y, rel2.Z);

        Vector3 world1 = TransformBonePoint(boneWorlds, vertex.Joint1, local1);
        Vector3 world2 = TransformBonePoint(boneWorlds, vertex.Joint2, local2);
        return Vector3.Lerp(world2, world1, Mathf.Clamp01(vertex.Joint1Weight));
    }

    static Vector3 TransformBonePoint(Transform[] bones, int boneIndex, Vector3 localPoint)
    {
        if (bones == null || boneIndex < 0 || boneIndex >= bones.Length || bones[boneIndex] == null)
            return localPoint;

        Transform bone = bones[boneIndex];
        return bone.position + bone.rotation * localPoint;
    }

    static Vector3 TransformBonePoint(Matrix4x4[] boneWorlds, int boneIndex, Vector3 localPoint)
    {
        if (boneWorlds == null || boneIndex < 0 || boneIndex >= boneWorlds.Length)
            return localPoint;

        return boneWorlds[boneIndex].MultiplyPoint3x4(localPoint);
    }

    static AbiffMaterialDesc ResolveMaterial(
        int materialId,
        List<RDBCatMesh.Material> materials,
        Dictionary<string, int> textureIds,
        int groupIndex,
        int meshIndex)
    {
        if (materials == null || materialId < 0 || materialId >= materials.Count)
            return CatMeshMaterialFactory.CreateDesc(null, textureIds, $"CatMeshMat_{groupIndex}_{meshIndex}");

        return CatMeshMaterialFactory.CreateDesc(materials[materialId], textureIds, $"CatMeshMat_{groupIndex}_{meshIndex}");
    }
}
