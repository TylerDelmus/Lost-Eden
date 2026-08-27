using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using AODB.Common.RDBObjects;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class TerrainParser
{
    const int LodCount = 3;

    readonly ResourceDatabase _database;
    readonly RenderConfig _renderConfig;

    public TerrainParser(ResourceDatabase database, RenderConfig renderConfig)
    {
        _database = database;
        _renderConfig = renderConfig;
    }

    public IEnumerator BuildCoroutine(int playfieldId, Transform parent)
    {
        if (_renderConfig == null)
        {
            Debug.LogError("TerrainParser: RenderConfig is missing.");
            yield break;
        }

        var tilemap = _database.Get<Tilemap>(playfieldId);
        if (tilemap == null)
        {
            Debug.LogError($"TerrainParser: Tilemap {playfieldId} not found.");
            yield break;
        }

        if (!TryCreateAtlas(tilemap, out Texture2D atlas, out Rect[] texBounds))
        {
            Debug.LogError($"TerrainParser: Failed to build atlas for playfield {playfieldId}.");
            yield break;
        }

        Material material = CreateAtlasMaterial(atlas);
        int chunksX = (int)Math.Floor((double)tilemap.MapWidth / tilemap.ChunkSize) + 1;
        int chunksY = (int)Math.Floor((double)tilemap.MapHeight / tilemap.ChunkSize) + 1;

        var chunks = CollectChunks(tilemap, chunksX, chunksY);
        if (chunks.Count == 0)
        {
            Debug.LogError($"TerrainParser: No heightmap chunks for playfield {playfieldId}.");
            yield break;
        }

        var root = new GameObject($"Playfield_{playfieldId}");
        root.transform.SetParent(parent, false);

        var chunkViews = new ChunkView[chunks.Count];
        for (int i = 0; i < chunks.Count; i++)
        {
            chunkViews[i] = CreateChunkView(root.transform, chunks[i], material);
        }

        yield return null;

        TerrainChunkMeshData[] lod0 = null;
        yield return RunParallel(() =>
        {
            lod0 = BuildLodLevel(chunks, texBounds, lod: 0);
        });

        for (int i = 0; i < chunkViews.Length; i++)
        {
            ApplyMesh(chunkViews[i], lod: 0, lod0[i]);
            chunkViews[i].LodGroup.SetLODs(new[]
            {
                new LOD(_renderConfig.GetTerrainLodScreenHeight(0), new Renderer[] { chunkViews[i].Renderers[0] })
            });
            chunkViews[i].LodGroup.RecalculateBounds();
        }

        yield return null;

        TerrainChunkMeshData[] lod1 = null;
        TerrainChunkMeshData[] lod2 = null;
        yield return RunParallel(() =>
        {
            lod1 = BuildLodLevel(chunks, texBounds, lod: 1);
            lod2 = BuildLodLevel(chunks, texBounds, lod: 2);
        });

        for (int i = 0; i < chunkViews.Length; i++)
        {
            ApplyMesh(chunkViews[i], lod: 1, lod1[i]);
            ApplyMesh(chunkViews[i], lod: 2, lod2[i]);

            for (int lod = 0; lod < LodCount; lod++)
                chunkViews[i].Renderers[lod].gameObject.SetActive(true);

            chunkViews[i].LodGroup.SetLODs(new[]
            {
                new LOD(_renderConfig.GetTerrainLodScreenHeight(0), new Renderer[] { chunkViews[i].Renderers[0] }),
                new LOD(_renderConfig.GetTerrainLodScreenHeight(1), new Renderer[] { chunkViews[i].Renderers[1] }),
                new LOD(_renderConfig.GetTerrainLodScreenHeight(2), new Renderer[] { chunkViews[i].Renderers[2] })
            });
            chunkViews[i].LodGroup.RecalculateBounds();
            chunkViews[i].Root.isStatic = true;
        }
    }

    static List<ChunkSource> CollectChunks(Tilemap tilemap, int chunksX, int chunksY)
    {
        var chunks = new List<ChunkSource>(tilemap.Heightmap.Count);
        for (int x = 0; x < chunksX; x++)
        {
            for (int y = 0; y < chunksY; y++)
            {
                int idx = chunksX * y + x;
                if (idx >= tilemap.Heightmap.Count)
                    continue;

                if (!tilemap.TileMapDatas.TryGetValue(idx, out List<Tilemap.TileMapData> tileData) || tileData == null)
                    continue;

                chunks.Add(new ChunkSource
                {
                    ChunkX = x,
                    ChunkY = y,
                    ChunkSize = tilemap.ChunkSize,
                    HeightMod = tilemap.HeightMod,
                    MapScale = tilemap.MapScale,
                    Heightmap = tilemap.Heightmap[idx],
                    TileData = tileData
                });
            }
        }

        return chunks;
    }

    static TerrainChunkMeshData[] BuildLodLevel(List<ChunkSource> chunks, Rect[] texBounds, int lod)
    {
        var result = new TerrainChunkMeshData[chunks.Count];
        Parallel.For(0, chunks.Count, i =>
        {
            ChunkSource c = chunks[i];
            result[i] = TerrainChunkBuilder.Build(
                c.ChunkX,
                c.ChunkY,
                c.ChunkSize,
                c.HeightMod,
                c.MapScale,
                c.Heightmap,
                c.TileData,
                texBounds,
                lod);
        });

        SmoothChunkBoundaries(chunks, result);
        return result;
    }

    static void SmoothChunkBoundaries(List<ChunkSource> chunks, TerrainChunkMeshData[] meshes)
    {
        var indexByCoord = new Dictionary<(int x, int y), int>(chunks.Count);
        for (int i = 0; i < chunks.Count; i++)
            indexByCoord[(chunks[i].ChunkX, chunks[i].ChunkY)] = i;

        for (int i = 0; i < chunks.Count; i++)
        {
            int cx = chunks[i].ChunkX;
            int cy = chunks[i].ChunkY;

            if (indexByCoord.TryGetValue((cx + 1, cy), out int right))
                TerrainChunkBuilder.SmoothBoundary(meshes[i], meshes[right]);

            if (indexByCoord.TryGetValue((cx, cy + 1), out int bottom))
                TerrainChunkBuilder.SmoothBoundary(meshes[i], meshes[bottom]);
        }
    }

    static ChunkView CreateChunkView(Transform parent, ChunkSource source, Material material)
    {
        var root = new GameObject($"Chunk_{source.ChunkX}_{source.ChunkY}");
        root.transform.SetParent(parent, false);

        var lodGroup = root.AddComponent<LODGroup>();
        var filters = new MeshFilter[LodCount];
        var renderers = new MeshRenderer[LodCount];

        for (int lod = 0; lod < LodCount; lod++)
        {
            var lodGo = new GameObject($"LOD{lod}");
            lodGo.transform.SetParent(root.transform, false);
            filters[lod] = lodGo.AddComponent<MeshFilter>();
            renderers[lod] = lodGo.AddComponent<MeshRenderer>();
            renderers[lod].sharedMaterial = material;
            lodGo.SetActive(lod == 0);
        }

        var collider = root.AddComponent<MeshCollider>();

        return new ChunkView
        {
            Root = root,
            LodGroup = lodGroup,
            Filters = filters,
            Renderers = renderers,
            Collider = collider
        };
    }

    static void ApplyMesh(ChunkView view, int lod, TerrainChunkMeshData data)
    {
        if (data.Vertices == null || data.Vertices.Length == 0)
            return;

        var mesh = new Mesh
        {
            name = $"{view.Root.name}_LOD{lod}",
            indexFormat = data.Vertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
        };
        mesh.SetVertices(data.Vertices);
        mesh.SetNormals(data.Normals);
        mesh.SetUVs(0, data.UVs);
        mesh.SetTriangles(data.Triangles, 0, calculateBounds: false);
        mesh.RecalculateBounds();

        view.Filters[lod].sharedMesh = mesh;
        view.Renderers[lod].gameObject.SetActive(true);

        if (lod == 0)
            view.Collider.sharedMesh = mesh;
        else
            mesh.UploadMeshData(markNoLongerReadable: true);
    }

    bool TryCreateAtlas(Tilemap tilemap, out Texture2D atlas, out Rect[] texBounds)
    {
        atlas = null;
        texBounds = Array.Empty<Rect>();

        if (tilemap.TextureIds == null || tilemap.TextureIds.Length == 0)
            return false;

        var textures = new Texture2D[tilemap.TextureIds.Length];
        for (int i = 0; i < tilemap.TextureIds.Length; i++)
        {
            var ground = _database.Get<GroundTexture>(tilemap.TextureIds[i]);
            if (ground?.JpgData == null || ground.JpgData.Length == 0)
            {
                Debug.LogError($"TerrainParser: Missing GroundTexture {tilemap.TextureIds[i]}.");
                DestroyTextures(textures);
                return false;
            }

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
            if (!tex.LoadImage(ground.JpgData, markNonReadable: false))
            {
                Debug.LogError($"TerrainParser: Failed to decode GroundTexture {tilemap.TextureIds[i]}.");
                UnityEngine.Object.Destroy(tex);
                DestroyTextures(textures);
                return false;
            }

            textures[i] = tex;
        }

        atlas = new Texture2D(4, 4, TextureFormat.RGBA32, mipChain: true);
        texBounds = atlas.PackTextures(
            textures,
            _renderConfig.TerrainAtlasPadding,
            _renderConfig.TerrainAtlasMaxSize,
            makeNoLongerReadable: true);
        atlas.name = "PlayfieldAtlas";
        atlas.wrapMode = TextureWrapMode.Clamp;
        atlas.filterMode = FilterMode.Bilinear;

        DestroyTextures(textures);
        return texBounds != null && texBounds.Length > 0;
    }

    static Material CreateAtlasMaterial(Texture2D atlas)
    {
        Shader shader = Shader.Find("HDRP/Lit");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");

        var material = new Material(shader) { name = "PlayfieldTerrain" };
        if (material.HasProperty("_BaseColorMap"))
            material.SetTexture("_BaseColorMap", atlas);
        else if (material.HasProperty("_MainTex"))
            material.SetTexture("_MainTex", atlas);

        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", 0f);
        if (material.HasProperty("_Metallic"))
            material.SetFloat("_Metallic", 0f);

        return material;
    }

    static void DestroyTextures(Texture2D[] textures)
    {
        if (textures == null)
            return;

        for (int i = 0; i < textures.Length; i++)
        {
            if (textures[i] != null)
                UnityEngine.Object.Destroy(textures[i]);
        }
    }

    static IEnumerator RunParallel(Action work)
    {
        Task task = Task.Run(work);
        while (!task.IsCompleted)
            yield return null;

        if (task.IsFaulted)
            throw task.Exception?.InnerException ?? task.Exception;
    }

    sealed class ChunkSource
    {
        public int ChunkX;
        public int ChunkY;
        public int ChunkSize;
        public float HeightMod;
        public float MapScale;
        public ushort[,] Heightmap;
        public List<Tilemap.TileMapData> TileData;
    }

    sealed class ChunkView
    {
        public GameObject Root;
        public LODGroup LodGroup;
        public MeshFilter[] Filters;
        public MeshRenderer[] Renderers;
        public MeshCollider Collider;
    }
}
