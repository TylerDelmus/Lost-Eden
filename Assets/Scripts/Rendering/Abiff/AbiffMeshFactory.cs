using System;
using UnityEngine;
using UnityEngine.Rendering;

public static class AbiffMeshFactory
{
    /// <summary>
    /// Bakes submesh source into Unity-ready mesh data (UV V flip). No shear.
    /// </summary>
    public static AbiffMeshData Bake(AbiffSubmeshSource source)
    {
        if (source == null || source.Positions == null || source.Positions.Length == 0)
        {
            return new AbiffMeshData
            {
                Vertices = Array.Empty<Vector3>(),
                Normals = Array.Empty<Vector3>(),
                UVs = Array.Empty<Vector2>(),
                Triangles = Array.Empty<int>()
            };
        }

        int count = source.Positions.Length;
        var vertices = new Vector3[count];
        var normals = new Vector3[count];
        var uvs = new Vector2[count];

        for (int i = 0; i < count; i++)
        {
            vertices[i] = source.Positions[i];
            normals[i] = source.Normals[i];
            uvs[i] = new Vector2(source.UVs[i].x, -source.UVs[i].y);
        }

        var triangles = source.Triangles != null
            ? (int[])source.Triangles.Clone()
            : Array.Empty<int>();

        return new AbiffMeshData
        {
            Vertices = vertices,
            Normals = normals,
            UVs = uvs,
            Triangles = triangles
        };
    }

    public static Mesh CreateUnityMesh(AbiffMeshData data, string name)
    {
        if (data == null || data.Vertices == null || data.Vertices.Length == 0)
            return null;

        var mesh = new Mesh
        {
            name = name,
            indexFormat = data.Vertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
        };
        mesh.SetVertices(data.Vertices);
        mesh.SetNormals(data.Normals);
        mesh.SetUVs(0, data.UVs);
        mesh.SetTriangles(data.Triangles, 0, calculateBounds: false);
        mesh.RecalculateBounds();
        mesh.UploadMeshData(markNoLongerReadable: true);
        return mesh;
    }
}
