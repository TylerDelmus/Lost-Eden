using System;
using UnityEngine;
using UnityEngine.Rendering;

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
