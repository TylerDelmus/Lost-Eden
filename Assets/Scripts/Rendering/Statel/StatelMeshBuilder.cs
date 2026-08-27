using System;
using UnityEngine;

public static class StatelMeshBuilder
{
    /// <summary>
    /// When sheared, applies AO mesh-space slant: z' = z + shearFactor * x.
    /// Non-uniform scale for the shear path stays on the root transform (X only).
    /// Non-sheared path delegates to <see cref="AbiffMeshFactory.Bake"/>.
    /// </summary>
    public static AbiffMeshData Build(AbiffSubmeshSource source, bool applyShear, float shearFactor)
    {
        if (!applyShear)
            return AbiffMeshFactory.Bake(source);

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
            Vector3 pos = source.Positions[i];
            pos.z += shearFactor * pos.x;

            vertices[i] = pos;
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
}
