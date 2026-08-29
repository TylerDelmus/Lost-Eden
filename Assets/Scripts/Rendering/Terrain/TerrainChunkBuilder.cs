using System;
using System.Collections.Generic;
using AODB.Common.RDBObjects;
using UnityEngine;

public static class TerrainChunkBuilder
{
    const float UvPadding = 0.01f;

    /// <summary>
    /// Height/geometry follows AODB Chunk.CreateMesh: physicalSize = chunkSize (height
    /// blob side), 1:1 height samples, chunk origin at (chunkSize-1)*mapScale*(x,y).
    /// Tile UVs still use game tile_size² indexing into the tile blob.
    /// </summary>
    public static TerrainChunkMeshData Build(
        int chunkX,
        int chunkY,
        int tileSize,
        float heightMod,
        float mapScale,
        ushort[,] heightmap,
        IReadOnlyList<Tilemap.TileMapData> tileData,
        Rect[] texBounds,
        int lod)
    {
        if (tileData == null || tileData.Count == 0 || texBounds == null || texBounds.Length == 0 || heightmap == null)
            return EmptyMesh();

        int chunkSize = heightmap.GetLength(0);
        if (chunkSize <= 1 || heightmap.GetLength(1) != chunkSize)
            return EmptyMesh();

        tileSize = Math.Max(1, tileSize);

        // AODB Chunk.CreateMesh LOD sizing
        int meshLod = Mathf.Max(0, lod);
        int sizeMultiplier = 1;
        int physicalSize = chunkSize / sizeMultiplier;
        for (int i = 0; i < meshLod; i++)
        {
            physicalSize /= 2;
            sizeMultiplier *= 2;
        }

        if (meshLod != 0)
            physicalSize++;

        if (physicalSize <= 1)
            return EmptyMesh();

        // Patch grid is game tile_size (7×7 on map 57). CreateMesh's
        // (chunkSize-1)*chunk origin assumes non-overlapping CS-1 strides and would
        // blow past the map with 7 columns — keep tile_size spacing, CreateMesh locals.
        Vector3 anchorOffset = new Vector3(
            tileSize * mapScale * chunkX,
            0f,
            tileSize * mapScale * chunkY);

        int segments = physicalSize - 1;
        int vertexCount = segments * segments * 4;
        var vertices = new Vector3[vertexCount];
        var uvs = new Vector2[vertexCount];
        var triangles = new int[segments * segments * 6];

        int vIdx = 0;
        int tIndex = 0;
        bool flip = true;

        // Quads over the CreateMesh grid. Positions/heights match:
        //   (x * sizeMultiplier) * mapScale, heightmap[x*sm, y*sm]
        for (int y = 0; y < segments; y++)
        {
            for (int x = 0; x < segments; x++)
            {
                int hx0 = x * sizeMultiplier;
                int hy0 = y * sizeMultiplier;
                int hx1 = (x + 1) * sizeMultiplier;
                int hy1 = (y + 1) * sizeMultiplier;

                if (hx1 >= chunkSize) hx1 = chunkSize - 1;
                if (hy1 >= chunkSize) hy1 = chunkSize - 1;

                float wx0 = hx0 * mapScale;
                float wz0 = hy0 * mapScale;
                float wx1 = hx1 * mapScale;
                float wz1 = hy1 * mapScale;

                vertices[vIdx] = new Vector3(wx0, heightmap[hx0, hy0] * heightMod, wz0) + anchorOffset;
                vertices[vIdx + 1] = new Vector3(wx1, heightmap[hx1, hy0] * heightMod, wz0) + anchorOffset;
                vertices[vIdx + 2] = new Vector3(wx0, heightmap[hx0, hy1] * heightMod, wz1) + anchorOffset;
                vertices[vIdx + 3] = new Vector3(wx1, heightmap[hx1, hy1] * heightMod, wz1) + anchorOffset;

                // Tile grid is game tile_size²; map CreateMesh cell → tile cell.
                int tileX = Mathf.Min(hx0 * tileSize / Math.Max(1, chunkSize - 1), tileSize - 1);
                int tileY = Mathf.Min(hy0 * tileSize / Math.Max(1, chunkSize - 1), tileSize - 1);
                int tileIndex = tileX + tileY * tileSize;
                if (tileIndex >= tileData.Count)
                    tileIndex = tileData.Count - 1;

                Tilemap.TileMapData tile = tileData[tileIndex];
                int texIndex = Mathf.Clamp(tile.TextureId, 0, texBounds.Length - 1);
                Rect texBound = texBounds[texIndex];

                float aoX = texBound.x + UvPadding;
                float aoX2 = texBound.x + texBound.width - UvPadding;
                float unityLowV = texBound.y + UvPadding;
                float unityHighV = texBound.y + texBound.height - UvPadding;
                float aoY = unityHighV;
                float aoY2 = unityLowV;

                int v0 = vIdx;
                int v1 = vIdx + 1;
                int v2 = vIdx + 2;
                int v3 = vIdx + 3;

                ApplyAodbRotationUvs(uvs, v0, v1, v2, v3, tile.Rotation, aoX, aoX2, aoY, aoY2);

                // CreateMesh winding (two tris per cell), with our established flip variant
                // for textured seams.
                if (flip)
                {
                    triangles[tIndex] = v0;
                    triangles[tIndex + 1] = v3;
                    triangles[tIndex + 2] = v1;
                    triangles[tIndex + 3] = v2;
                    triangles[tIndex + 4] = v3;
                    triangles[tIndex + 5] = v0;
                }
                else
                {
                    triangles[tIndex] = v0;
                    triangles[tIndex + 1] = v2;
                    triangles[tIndex + 2] = v1;
                    triangles[tIndex + 3] = v2;
                    triangles[tIndex + 4] = v3;
                    triangles[tIndex + 5] = v1;
                }

                flip = !flip;
                vIdx += 4;
                tIndex += 6;
            }

            flip = !flip;
        }

        return new TerrainChunkMeshData
        {
            Vertices = vertices,
            Normals = CalculateSmoothNormals(vertices, triangles),
            UVs = uvs,
            Triangles = triangles
        };
    }

    static TerrainChunkMeshData EmptyMesh()
    {
        return new TerrainChunkMeshData
        {
            Vertices = Array.Empty<Vector3>(),
            Normals = Array.Empty<Vector3>(),
            UVs = Array.Empty<Vector2>(),
            Triangles = Array.Empty<int>()
        };
    }

    public static Vector3[] CalculateSmoothNormals(Vector3[] vertices, int[] triangles)
    {
        var vertexGroups = new Dictionary<Vector3, List<int>>(vertices.Length / 2);
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 key = RoundVertex(vertices[i]);
            if (!vertexGroups.TryGetValue(key, out List<int> group))
            {
                group = new List<int>(4);
                vertexGroups[key] = group;
            }

            group.Add(i);
        }

        var normalAccum = new Vector3[vertices.Length];
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 v0 = vertices[triangles[i]];
            Vector3 v1 = vertices[triangles[i + 1]];
            Vector3 v2 = vertices[triangles[i + 2]];
            Vector3 faceNormal = Vector3.Cross(v1 - v0, v2 - v0);
            normalAccum[triangles[i]] += faceNormal;
            normalAccum[triangles[i + 1]] += faceNormal;
            normalAccum[triangles[i + 2]] += faceNormal;
        }

        var normals = new Vector3[vertices.Length];
        foreach (List<int> group in vertexGroups.Values)
        {
            Vector3 avg = Vector3.zero;
            for (int g = 0; g < group.Count; g++)
                avg += normalAccum[group[g]];

            if (avg.sqrMagnitude > 1e-12f)
                avg.Normalize();
            else
                avg = Vector3.up;

            for (int g = 0; g < group.Count; g++)
                normals[group[g]] = avg;
        }

        return normals;
    }

    public static void SmoothBoundary(TerrainChunkMeshData mesh1, TerrainChunkMeshData mesh2)
    {
        var posToIdx1 = BuildPositionMap(mesh1.Vertices);
        var posToIdx2 = BuildPositionMap(mesh2.Vertices);

        foreach (KeyValuePair<Vector3, List<int>> kvp in posToIdx1)
        {
            if (!posToIdx2.TryGetValue(kvp.Key, out List<int> indices2))
                continue;

            Vector3 avg = Vector3.zero;
            for (int i = 0; i < kvp.Value.Count; i++)
                avg += mesh1.Normals[kvp.Value[i]];
            for (int i = 0; i < indices2.Count; i++)
                avg += mesh2.Normals[indices2[i]];

            if (avg.sqrMagnitude > 1e-12f)
                avg.Normalize();
            else
                avg = Vector3.up;

            for (int i = 0; i < kvp.Value.Count; i++)
                mesh1.Normals[kvp.Value[i]] = avg;
            for (int i = 0; i < indices2.Count; i++)
                mesh2.Normals[indices2[i]] = avg;
        }
    }

    static Dictionary<Vector3, List<int>> BuildPositionMap(Vector3[] vertices)
    {
        var map = new Dictionary<Vector3, List<int>>();
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 key = RoundVertex(vertices[i]);
            if (!map.TryGetValue(key, out List<int> list))
            {
                list = new List<int>(4);
                map[key] = list;
            }

            list.Add(i);
        }

        return map;
    }

    static Vector3 RoundVertex(Vector3 v)
    {
        return new Vector3(
            (float)Math.Round(v.x, 4),
            (float)Math.Round(v.y, 4),
            (float)Math.Round(v.z, 4));
    }

    static void ApplyAodbRotationUvs(
        Vector2[] uvs,
        int v0,
        int v1,
        int v2,
        int v3,
        byte rotation,
        float x,
        float x2,
        float y,
        float y2)
    {
        switch (rotation)
        {
            case 64:
                uvs[v0] = new Vector2(x2, y2);
                uvs[v1] = new Vector2(x2, y);
                uvs[v2] = new Vector2(x, y2);
                uvs[v3] = new Vector2(x, y);
                break;
            case 128:
                uvs[v0] = new Vector2(x2, y);
                uvs[v1] = new Vector2(x, y);
                uvs[v2] = new Vector2(x2, y2);
                uvs[v3] = new Vector2(x, y2);
                break;
            case 192:
                uvs[v0] = new Vector2(x, y);
                uvs[v1] = new Vector2(x, y2);
                uvs[v2] = new Vector2(x2, y);
                uvs[v3] = new Vector2(x2, y2);
                break;
            default:
                uvs[v0] = new Vector2(x, y2);
                uvs[v1] = new Vector2(x2, y2);
                uvs[v2] = new Vector2(x, y);
                uvs[v3] = new Vector2(x2, y);
                break;
        }
    }
}
