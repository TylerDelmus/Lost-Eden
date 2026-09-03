using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using AODB.Common.RDBObjects;
using UnityEngine;

/// <summary>
/// Scatters grass instances over every tile whose ground texture is classified as grass,
/// then hands each chunk's baked transforms to a <see cref="GrassChunkRenderer"/>.
///
/// Mirrors <see cref="TerrainParser"/>'s chunk iteration and reuses its exact vertex maths,
/// so blades sit flush on the terrain mesh rather than on an approximation of it. Runs
/// after TerrainParser in PlayfieldFactory.LoadRoutine.
/// </summary>
public sealed class PlayfieldGrassBuilder
{
    const int ChunksPerFrame = 4;

    /// <summary>
    /// Upper bound on the per-tile jittered-grid resolution. 128x128 is 16k candidate
    /// points per tile, which is far past anything sane for single-blade meshes; it exists
    /// to stop a typo in the inspector from trying to allocate a playfield's worth of
    /// gigabytes. Density above what this allows is reported, not silently ignored.
    /// </summary>
    const int MaxSamplesPerTileAxis = 128;

    readonly ResourceDatabase _database;
    readonly RenderConfig _renderConfig;

    static readonly int WindParamsId = Shader.PropertyToID("_GrassWindParams");
    static readonly int WindParams2Id = Shader.PropertyToID("_GrassWindParams2");
    static readonly int FadeParamsId = Shader.PropertyToID("_GrassFadeParams");

    public PlayfieldGrassBuilder(ResourceDatabase database, RenderConfig renderConfig)
    {
        _database = database;
        _renderConfig = renderConfig;
    }

    public IEnumerator BuildCoroutine(int playfieldId, Transform parent)
    {
        if (_renderConfig == null)
        {
            Debug.LogError("PlayfieldGrassBuilder: RenderConfig is missing.");
            yield break;
        }

        if (!_renderConfig.GrassEnabled)
            yield break;

        if (_renderConfig.GrassMesh == null || _renderConfig.GrassMaterial == null)
        {
            Debug.LogWarning("PlayfieldGrassBuilder: GrassMesh or GrassMaterial not assigned on RenderConfig - skipping grass.");
            yield break;
        }

        var tilemap = _database.Get<Tilemap>(playfieldId);
        if (tilemap == null || tilemap.Heightmap == null || tilemap.Heightmap.Count == 0 || tilemap.ChunkSize <= 1)
            yield break;

        if (tilemap.GridWidth <= 0 || tilemap.Heightmap.Count % tilemap.GridWidth != 0)
            yield break;

        if (tilemap.TextureIds == null || tilemap.TextureIds.Length == 0)
            yield break;

        var fullIds = new HashSet<int>(_renderConfig.GrassFullTextureIds ?? Array.Empty<int>());
        var partialIds = new HashSet<int>(_renderConfig.GrassPartialTextureIds ?? Array.Empty<int>());
        if (fullIds.Count == 0 && partialIds.Count == 0)
        {
            Debug.LogWarning("PlayfieldGrassBuilder: no grass texture ids configured - nothing to place.");
            yield break;
        }

        ApplyGlobalShaderParams();

        // Main thread: decode the partial-tile textures into masks before any worker
        // thread touches them. Immutable from here on, so the parallel pass can read
        // the dictionary without locking.
        Dictionary<int, GrassMask> masks = GrassMaskLibrary.Build(
            _database,
            partialIds,
            _renderConfig.GrassMaskResolution,
            _renderConfig.GrassMaskThreshold,
            _renderConfig.GrassLogMaskCoverage);

        List<ChunkSource> chunks = CollectChunks(tilemap, fullIds, partialIds);
        if (chunks.Count == 0)
        {
            Debug.Log($"PlayfieldGrassBuilder: playfield {playfieldId} has no grass-classified tiles.");
            yield break;
        }

        var settings = PlacementSettings.From(_renderConfig, tilemap.MapScale);
        var results = new ChunkResult[chunks.Count];

        Task work = Task.Run(() =>
        {
            Parallel.For(0, chunks.Count, i => results[i] = PlaceChunk(chunks[i], masks, settings));
        });

        while (!work.IsCompleted)
            yield return null;

        if (work.IsFaulted)
        {
            Debug.LogError($"PlayfieldGrassBuilder: placement failed: {work.Exception?.InnerException ?? (Exception)work.Exception}");
            yield break;
        }

        var root = new GameObject($"PlayfieldGrass_{playfieldId}");
        root.transform.SetParent(parent, false);

        int totalInstances = 0;
        int createdChunks = 0;
        int truncatedChunks = 0;

        for (int i = 0; i < results.Length; i++)
        {
            ChunkResult result = results[i];
            if (result.Instances == null || result.Instances.Length == 0)
                continue;

            var go = new GameObject($"Grass_{chunks[i].ChunkX}_{chunks[i].ChunkY}");
            go.transform.SetParent(root.transform, false);

            var renderer = go.AddComponent<GrassChunkRenderer>();
            renderer.Mode = _renderConfig.GrassUseInstancedFallback
                ? GrassChunkRenderer.DrawMode.InstancedFallback
                : GrassChunkRenderer.DrawMode.Indirect;

            renderer.Initialize(
                _renderConfig.GrassMesh,
                _renderConfig.GrassMaterial,
                result.Instances,
                result.Bounds,
                _renderConfig.GrassCullDistance,
                _renderConfig.GrassRenderingLayerMask == 0 ? 1u : _renderConfig.GrassRenderingLayerMask,
                new GrassChunkRenderer.LodSettings
                {
                    StartDistance = _renderConfig.GrassLodStartDistance,
                    MinDensity = _renderConfig.GrassLodMinDensity,
                    Steps = _renderConfig.GrassLodSteps
                },
                _renderConfig.GrassFallbackMaterial);

            totalInstances += result.Instances.Length;
            createdChunks++;
            if (result.Truncated)
                truncatedChunks++;

            if (createdChunks % ChunksPerFrame == 0)
                yield return null;
        }

        Debug.Log(
            $"PlayfieldGrassBuilder: playfield {playfieldId} - {totalInstances} grass instances across " +
            $"{createdChunks} chunk(s) ({settings.SamplesPerTileAxis}x{settings.SamplesPerTileAxis} samples " +
            $"per tile), {masks.Count} partial-tile mask(s), " +
            $"mode={(_renderConfig.GrassUseInstancedFallback ? "InstancedFallback" : "Indirect")}.");

        if (truncatedChunks > 0)
        {
            Debug.LogWarning(
                $"PlayfieldGrassBuilder: {truncatedChunks} chunk(s) hit GrassMaxInstancesPerChunk " +
                $"({_renderConfig.GrassMaxInstancesPerChunk}) and were cut off part-way through, so " +
                $"their grass stops abruptly rather than thinning. Raise the limit or lower the density.");
        }
    }

    void ApplyGlobalShaderParams()
    {
        float rad = _renderConfig.GrassWindDirectionDegrees * Mathf.Deg2Rad;
        var dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)).normalized;

        // Globals rather than material properties: identical for every chunk, and they
        // must match the declarations in GrassInstancing.hlsl, which sit outside any
        // CBUFFER precisely so a single SetGlobal reaches every grass material.
        Shader.SetGlobalVector(WindParamsId, new Vector4(
            dir.x, dir.y, _renderConfig.GrassWindStrength, _renderConfig.GrassWindFrequency));

        Shader.SetGlobalVector(WindParams2Id, new Vector4(
            _renderConfig.GrassWindPhaseScale,
            _renderConfig.GrassWindGustScale,
            _renderConfig.GrassWindPhaseJitter,
            0f));

        Shader.SetGlobalVector(FadeParamsId, new Vector4(
            _renderConfig.GrassCullDistance,
            Mathf.Max(0.01f, _renderConfig.GrassFadeBand),
            Mathf.Max(0.0001f, _renderConfig.GrassBladeHeight),
            0f));
    }

    static List<ChunkSource> CollectChunks(Tilemap tilemap, HashSet<int> fullIds, HashSet<int> partialIds)
    {
        int chunkSize = tilemap.ChunkSize;
        int gridWidth = tilemap.GridWidth;
        var chunks = new List<ChunkSource>(tilemap.Heightmap.Count);

        for (int i = 0; i < tilemap.Heightmap.Count; i++)
        {
            ushort[,] heightmap = tilemap.Heightmap[i];
            if (heightmap == null ||
                heightmap.GetLength(0) != chunkSize ||
                heightmap.GetLength(1) != chunkSize)
            {
                continue;
            }

            if (!tilemap.TileMapDatas.TryGetValue(i, out List<Tilemap.TileMapData> tileData) ||
                tileData == null ||
                tileData.Count == 0)
            {
                continue;
            }

            chunks.Add(new ChunkSource
            {
                ChunkIndex = i,
                ChunkX = i % gridWidth,
                ChunkY = i / gridWidth,
                ChunkSize = chunkSize,
                HeightMod = tilemap.HeightMod,
                MapScale = tilemap.MapScale,
                Heightmap = heightmap,
                TileData = tileData,
                TextureIds = tilemap.TextureIds,
                FullIds = fullIds,
                PartialIds = partialIds
            });
        }

        return chunks;
    }

    static ChunkResult PlaceChunk(ChunkSource c, Dictionary<int, GrassMask> masks, PlacementSettings s)
    {
        int fullExtent = c.ChunkSize - 1;
        float mapScale = c.MapScale;

        // Identical to TerrainChunkBuilder.Build's anchorOffset.
        var anchor = new Vector3(fullExtent * mapScale * c.ChunkX, 0f, fullExtent * mapScale * c.ChunkY);

        int perAxis = s.SamplesPerTileAxis;
        float cellStep = 1f / perAxis;

        var instances = new List<Matrix4x4>(1024);
        Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        bool truncated = false;

        for (int tileY = 0; tileY < fullExtent; tileY++)
        {
            for (int tileX = 0; tileX < fullExtent; tileX++)
            {
                int tileIndex = TerrainChunkBuilder.ResolveTileIndex(c.TileData.Count, tileX, tileY, fullExtent);
                Tilemap.TileMapData tile = c.TileData[tileIndex];

                // TileMapData.TextureId is a LOCAL index into Tilemap.TextureIds, not the
                // GroundTexture RDB id. Classification is configured against RDB ids.
                int local = Mathf.Clamp(tile.TextureId, 0, c.TextureIds.Length - 1);
                int rdbId = c.TextureIds[local];

                bool isFull = c.FullIds.Contains(rdbId);
                bool isPartial = !isFull && c.PartialIds.Contains(rdbId);
                if (!isFull && !isPartial)
                    continue;

                GrassMask mask = null;
                if (isPartial && !masks.TryGetValue(rdbId, out mask))
                    mask = null; // decode failed earlier: fall back to full coverage

                byte rotation = NormalizeTileRotation(tile.Rotation);

                ushort h00 = c.Heightmap[tileX, tileY];
                ushort h10 = c.Heightmap[tileX + 1, tileY];
                ushort h01 = c.Heightmap[tileX, tileY + 1];
                ushort h11 = c.Heightmap[tileX + 1, tileY + 1];

                for (int sy = 0; sy < perAxis; sy++)
                {
                    for (int sx = 0; sx < perAxis; sx++)
                    {
                        uint seed = Hash(
                            (uint)c.ChunkIndex * 73856093u ^
                            (uint)(tileX + tileY * fullExtent) * 19349663u ^
                            (uint)(sx + sy * perAxis) * 83492791u);

                        // Jittered grid: even coverage without the visible lattice of a
                        // regular grid or the clumping of pure random.
                        float fx = (sx + Rand01(ref seed)) * cellStep;
                        float fy = (sy + Rand01(ref seed)) * cellStep;

                        if (Rand01(ref seed) > s.Coverage)
                            continue;

                        if (mask != null)
                        {
                            RotateTileUv(fx, fy, rotation, out float ms, out float mt);
                            if (!mask.Sample(ms, mt))
                                continue;
                        }

                        // Bilinear over the same four corner heights TerrainChunkBuilder
                        // uses for this quad's vertices.
                        float hx0 = Mathf.Lerp(h00, h10, fx);
                        float hx1 = Mathf.Lerp(h01, h11, fx);
                        float height = Mathf.Lerp(hx0, hx1, fy) * c.HeightMod;

                        float dhdx = (Mathf.Lerp(h10 - h00, h11 - h01, fy) * c.HeightMod) / mapScale;
                        float dhdz = (Mathf.Lerp(h01 - h00, h11 - h10, fx) * c.HeightMod) / mapScale;
                        Vector3 normal = new Vector3(-dhdx, 1f, -dhdz).normalized;

                        if (Vector3.Angle(normal, Vector3.up) > s.MaxSlopeDegrees)
                            continue;

                        var position = new Vector3(
                            (tileX + fx) * mapScale + anchor.x,
                            height + s.HeightOffset,
                            (tileY + fy) * mapScale + anchor.z);

                        float yaw = Rand01(ref seed) * 360f;

                        // Width and height vary independently: tying them together
                        // just scales the same silhouette up and down, which reads as
                        // one blade repeated rather than a field of them.
                        float bladeWidth = Mathf.Lerp(s.MinWidth, s.MaxWidth, Rand01(ref seed));
                        float bladeHeight = Mathf.Lerp(s.MinHeight, s.MaxHeight, Rand01(ref seed));

                        Quaternion align = Quaternion.Slerp(
                            Quaternion.identity,
                            Quaternion.FromToRotation(Vector3.up, normal),
                            s.NormalAlignment);

                        instances.Add(Matrix4x4.TRS(
                            position,
                            align * Quaternion.Euler(0f, yaw, 0f),
                            new Vector3(bladeWidth, bladeHeight, bladeWidth)));

                        min = Vector3.Min(min, position);
                        max = Vector3.Max(max, position);

                        if (instances.Count >= s.MaxInstancesPerChunk)
                        {
                            truncated = true;
                            goto done;
                        }
                    }
                }
            }
        }

    done:
        if (instances.Count == 0)
            return new ChunkResult { Instances = Array.Empty<Matrix4x4>(), Bounds = new Bounds() };

        Matrix4x4[] baked = instances.ToArray();

        // Shuffle so that ANY prefix of the array is a uniform random sample of the
        // whole chunk. That is what makes distance LOD work: the renderer thins a chunk
        // purely by shrinking instanceCount in the indirect args, with no per-frame
        // filtering and no extra buffers - the blades that drop out are scattered
        // evenly rather than being one contiguous patch that visibly vanishes.
        uint shuffleSeed = Hash((uint)c.ChunkIndex * 2654435761u + 0x5bd1e995u);
        for (int i = baked.Length - 1; i > 0; i--)
        {
            int j = (int)(Rand01(ref shuffleSeed) * (i + 1));
            j = j > i ? i : j;
            (baked[i], baked[j]) = (baked[j], baked[i]);
        }

        // Pad so the chunk is not culled while a blade at its edge is still on screen:
        // upward by the tallest possible blade, sideways by the widest plus wind travel.
        float padXZ = s.MaxWidth + s.WindStrength + 1f;
        float padY = s.BladeHeight * s.MaxHeight + s.WindStrength + 1f;
        var bounds = new Bounds();
        bounds.SetMinMax(min - new Vector3(padXZ, 0f, padXZ), max + new Vector3(padXZ, padY, padXZ));

        return new ChunkResult { Instances = baked, Bounds = bounds, Truncated = truncated };
    }

    /// <summary>
    /// Maps a tile-local position (u along +X, v along +Z) into the tile texture's own
    /// un-rotated space, with t = 0 at the bottom of the image.
    ///
    /// This is the exact inverse of TerrainChunkBuilder.ApplyAodbRotationUvs' corner
    /// table, so a rotated tile instance still samples its grass mask correctly. Derived
    /// from that table's four vertices, not guessed - if the terrain UV table ever
    /// changes, this must change with it.
    /// </summary>
    static void RotateTileUv(float u, float v, byte rotation, out float s, out float t)
    {
        switch (rotation)
        {
            case 64: s = 1f - v; t = u; break;  //  90 degrees
            case 128: s = 1f - u; t = 1f - v; break;  // 180 degrees
            case 192: s = v; t = 1f - u; break;  // 270 degrees
            default: s = u; t = v; break;  //   0 degrees
        }
    }

    /// <summary>Mirrors TerrainChunkBuilder.NormalizeTileRotation (private there).</summary>
    static byte NormalizeTileRotation(byte rotation)
        => rotation <= 3 ? (byte)(rotation * 64) : rotation;

    static uint Hash(uint x)
    {
        // Wang hash - cheap, deterministic and thread-safe (no shared System.Random),
        // so the same playfield always scatters identically.
        x = (x ^ 61u) ^ (x >> 16);
        x *= 9u;
        x ^= x >> 4;
        x *= 0x27d4eb2du;
        x ^= x >> 15;
        return x;
    }

    static float Rand01(ref uint state)
    {
        state = Hash(state + 0x9E3779B9u);
        return (state & 0x00FFFFFFu) / (float)0x01000000;
    }

    struct PlacementSettings
    {
        public int SamplesPerTileAxis;
        public float Coverage;
        public float MaxSlopeDegrees;
        public float MinWidth;
        public float MaxWidth;
        public float MinHeight;
        public float MaxHeight;
        public float NormalAlignment;
        public float HeightOffset;
        public float BladeHeight;
        public float WindStrength;
        public int MaxInstancesPerChunk;

        /// <summary>
        /// Grid resolution per tile. Worked out once on the main thread rather than per
        /// chunk on a worker, so hitting the cap can be reported instead of silently
        /// swallowing every density value above it.
        /// </summary>
        public static int ResolveSamplesPerTileAxis(RenderConfig cfg, float mapScale)
        {
            float density = Mathf.Max(0.0001f, cfg.GrassDensityPerSquareMetre);
            int wanted = Mathf.RoundToInt(mapScale * Mathf.Sqrt(density));
            int clamped = Mathf.Clamp(wanted, 1, MaxSamplesPerTileAxis);

            if (wanted > clamped)
            {
                float achievable = (clamped / mapScale) * (clamped / mapScale);
                Debug.LogWarning(
                    $"PlayfieldGrassBuilder: GrassDensityPerSquareMetre {density:F1} needs a " +
                    $"{wanted}x{wanted} sample grid per tile, capped at {clamped}x{clamped}. " +
                    $"Actual density will be about {achievable:F1}/m2 - raising the setting " +
                    $"further will do nothing. Raise MaxSamplesPerTileAxis, or use a grass mesh " +
                    $"with several blades per instance instead of one.");
            }

            return clamped;
        }

        public static PlacementSettings From(RenderConfig cfg, float mapScale) => new PlacementSettings
        {
            SamplesPerTileAxis = ResolveSamplesPerTileAxis(cfg, mapScale),
            Coverage = Mathf.Clamp01(cfg.GrassCoverage),
            MaxSlopeDegrees = Mathf.Clamp(cfg.GrassMaxSlopeDegrees, 0f, 90f),
            MinWidth = Mathf.Max(0.01f, cfg.GrassMinWidth),
            MaxWidth = Mathf.Max(0.01f, cfg.GrassMaxWidth),
            MinHeight = Mathf.Max(0.01f, cfg.GrassMinHeight),
            MaxHeight = Mathf.Max(0.01f, cfg.GrassMaxHeight),
            NormalAlignment = Mathf.Clamp01(cfg.GrassNormalAlignment),
            HeightOffset = cfg.GrassHeightOffset,
            BladeHeight = Mathf.Max(0.0001f, cfg.GrassBladeHeight),
            WindStrength = Mathf.Max(0f, cfg.GrassWindStrength),
            MaxInstancesPerChunk = Mathf.Max(1, cfg.GrassMaxInstancesPerChunk)
        };
    }

    sealed class ChunkSource
    {
        public int ChunkIndex;
        public int ChunkX;
        public int ChunkY;
        public int ChunkSize;
        public float HeightMod;
        public float MapScale;
        public ushort[,] Heightmap;
        public List<Tilemap.TileMapData> TileData;
        public short[] TextureIds;
        public HashSet<int> FullIds;
        public HashSet<int> PartialIds;
    }

    struct ChunkResult
    {
        public Matrix4x4[] Instances;
        public Bounds Bounds;
        public bool Truncated;
    }
}