using System.Collections.Generic;
using AODB.Common.RDBObjects;
using UnityEngine;
using AoVector3 = AODB.Common.Structs.Vector3;

/// <summary>
/// Converts a decoded <see cref="SurfaceResource"/> into Unity mesh buffers (world-space verts).
/// </summary>
public static class SurfaceCollisionBuilder
{
    public sealed class MeshData
    {
        public Vector3[] Vertices;
        public int[] Triangles;
    }

    public static bool TryBuild(SurfaceResource resource, out MeshData meshData)
    {
        meshData = null;
        if (resource?.Surfaces == null || resource.Surfaces.Count == 0)
            return false;

        int vertexCount = 0;
        int triangleCount = 0;
        for (int i = 0; i < resource.Surfaces.Count; i++)
        {
            SurfaceMesh surface = resource.Surfaces[i];
            if (surface?.Vertices == null || surface.Triangles == null)
                continue;

            vertexCount += surface.Vertices.Count;
            triangleCount += surface.Triangles.Count;
        }

        if (vertexCount == 0 || triangleCount == 0)
            return false;

        var vertices = new Vector3[vertexCount];
        var triangles = new int[triangleCount * 3];
        int vBase = 0;
        int tWrite = 0;

        for (int i = 0; i < resource.Surfaces.Count; i++)
        {
            SurfaceMesh surface = resource.Surfaces[i];
            if (surface?.Vertices == null || surface.Triangles == null)
                continue;

            List<AoVector3> srcVerts = surface.Vertices;
            for (int v = 0; v < srcVerts.Count; v++)
            {
                AoVector3 src = srcVerts[v];
                vertices[vBase + v] = new Vector3(src.X, src.Y, src.Z);
            }

            List<Int3> srcTris = surface.Triangles;
            for (int t = 0; t < srcTris.Count; t++)
            {
                Int3 tri = srcTris[t];
                triangles[tWrite++] = vBase + tri.A;
                triangles[tWrite++] = vBase + tri.B;
                triangles[tWrite++] = vBase + tri.C;
            }

            vBase += srcVerts.Count;
        }

        if (tWrite != triangles.Length)
        {
            var trimmed = new int[tWrite];
            System.Array.Copy(triangles, trimmed, tWrite);
            triangles = trimmed;
        }

        meshData = new MeshData
        {
            Vertices = vertices,
            Triangles = triangles,
        };
        return true;
    }
}
