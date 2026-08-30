using System;
using System.Collections.Generic;
using AODB.Common.RDBObjects;
using UnityEngine;

public static class TerrainChunkBuilder
{
    // Match commit 30d85f7: inset UVs enough to stay inside dilated atlas gutters.
    const float UvPadding = 0.01f;

    public static TerrainChunkMeshData Build(
        int chunkX,
        int chunkY,
        int chunkSize,
        float heightMod,
        float mapScale,
        ushort[,] heightmap,
        IReadOnlyList<Tilemap.TileMapData> tileData,
        Rect[] texBounds,
        int lod)
    {
        // Every LOD must span the same world footprint: (chunkSize - 1) * mapScale.
        int fullExtent = chunkSize - 1;
        int step = 1 << Mathf.Max(0, lod);
        int segments = (fullExtent + step - 1) / step;
        if (segments <= 0)
        {
            return new TerrainChunkMeshData
            {
                Vertices = Array.Empty<Vector3>(),
                Normals = Array.Empty<Vector3>(),
                UVs = Array.Empty<Vector2>(),
                Triangles = Array.Empty<int>()
            };
        }

        int heightW = heightmap.GetLength(0);
        int heightH = heightmap.GetLength(1);
        if (tileData == null || tileData.Count == 0 || texBounds == null || texBounds.Length == 0)
        {
            return new TerrainChunkMeshData
            {
                Vertices = Array.Empty<Vector3>(),
                Normals = Array.Empty<Vector3>(),
                UVs = Array.Empty<Vector2>(),
                Triangles = Array.Empty<int>()
            };
        }

        // ChunkedGround.Compose: origin = Grid * (Size - 1) in sample space.
        Vector3 anchorOffset = new Vector3(
            fullExtent * mapScale * chunkX,
            0f,
            fullExtent * mapScale * chunkY);

        int vertexCount = segments * segments * 4;
        var vertices = new Vector3[vertexCount];
        var uvs = new Vector2[vertexCount];
        var triangles = new int[segments * segments * 6];

        int vIdx = 0;
        int tIndex = 0;
        bool flip = true;

        for (int y = 0; y < segments; y++)
        {
            for (int x = 0; x < segments; x++)
            {
                int hx0 = Mathf.Min(x * step, fullExtent);
                int hy0 = Mathf.Min(y * step, fullExtent);
                int hx1 = Mathf.Min((x + 1) * step, fullExtent);
                int hy1 = Mathf.Min((y + 1) * step, fullExtent);

                int sx0 = Mathf.Min(hx0, heightW - 1);
                int sy0 = Mathf.Min(hy0, heightH - 1);
                int sx1 = Mathf.Min(hx1, heightW - 1);
                int sy1 = Mathf.Min(hy1, heightH - 1);

                vertices[vIdx] = new Vector3(hx0 * mapScale, heightmap[sx0, sy0] * heightMod, hy0 * mapScale) + anchorOffset;
                vertices[vIdx + 1] = new Vector3(hx1 * mapScale, heightmap[sx1, sy0] * heightMod, hy0 * mapScale) + anchorOffset;
                vertices[vIdx + 2] = new Vector3(hx0 * mapScale, heightmap[sx0, sy1] * heightMod, hy1 * mapScale) + anchorOffset;
                vertices[vIdx + 3] = new Vector3(hx1 * mapScale, heightmap[sx1, sy1] * heightMod, hy1 * mapScale) + anchorOffset;

                int tileX = Mathf.Min(hx0, fullExtent - 1);
                int tileY = Mathf.Min(hy0, fullExtent - 1);
                int tileIndex = ResolveTileIndex(tileData.Count, tileX, tileY, fullExtent);
                Tilemap.TileMapData tile = tileData[tileIndex];
                int texIndex = Mathf.Clamp(tile.TextureId, 0, texBounds.Length - 1);
                Rect texBound = texBounds[texIndex];

                // AODB atlas packs JPEG row0 (image top) at low V; Unity PackTextures is
                // upright (image bottom at low V). Remap y/y2 so the original AO corner
                // table still samples the same texels. Do NOT flip U — that breaks 90/270.
                float aoX = texBound.x + UvPadding;
                float aoX2 = texBound.x + texBound.width - UvPadding;
                float unityLowV = texBound.y + UvPadding;
                float unityHighV = texBound.y + texBound.height - UvPadding;
                float aoY = unityHighV;  // AODB y  = top of image
                float aoY2 = unityLowV; // AODB y2 = bottom of image

                int v0 = vIdx;
                int v1 = vIdx + 1;
                int v2 = vIdx + 2;
                int v3 = vIdx + 3;

                // AODB FlattenTiles stores 2-bit rotation (0-3); older paths used 0/64/128/192.
                byte rotation = NormalizeTileRotation(tile.Rotation);
                ApplyAodbRotationUvs(uvs, v0, v1, v2, v3, rotation, aoX, aoX2, aoY, aoY2);

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

    /// <summary>
    /// Tiles cover faces: (chunkSize-1)². Index as x + z * tileSide when the blob matches;
    /// otherwise clamp into whatever AODB supplied.
    /// </summary>
    static int ResolveTileIndex(int tileCount, int tileX, int tileY, int tileSide)
    {
        if (tileCount <= 0)
            return 0;

        int expected = tileSide * tileSide;
        if (tileCount == expected)
            return tileX + tileY * tileSide;

        int index = tileX + tileY * tileSide;
        if (index >= 0 && index < tileCount)
            return index;

        // Non-square / short blobs (e.g. 128 on a 15² grid): map into available range.
        int stride = tileSide;
        while (stride > 1 && tileCount % stride != 0)
            stride--;
        int rows = Math.Max(1, tileCount / Math.Max(1, stride));
        int x = Mathf.Clamp(tileX, 0, stride - 1);
        int y = Mathf.Clamp(tileY, 0, rows - 1);
        return Mathf.Clamp(x + y * stride, 0, tileCount - 1);
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

    static byte NormalizeTileRotation(byte rotation)
    {
        if (rotation <= 3)
            return (byte)(rotation * 64);
        return rotation;
    }

    /// <summary>
    /// Exact UV corner table from AODB.Chunk.CreateMeshTextured.
    /// Vertex layout: v0=(minX,minZ), v1=(maxX,minZ), v2=(minX,maxZ), v3=(maxX,maxZ).
    /// Rotation bytes: 0=0°, 64=90°, 128=180°, 192=270°.
    /// x/x2/y/y2 must already be in the same content space AODB used (see call site V remap).
    /// </summary>
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
