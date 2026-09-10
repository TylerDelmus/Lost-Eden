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
    static readonly int VariantParamsId = Shader.PropertyToID("_GrassVariantParams");
    static readonly int BaseColorMapId = Shader.PropertyToID("_BaseColorMap");

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

        ApplyGlobalShaderParams();

        // Main thread: decode every ground texture this playfield references and work out
        // from the pixels which ones are grass. AO has far too many ground textures to
        // enumerate by hand, and the ids mean different things in different playfields, so
        // classification is derived rather than configured. Immutable from here on, so the
        // parallel placement pass can read it without locking.
        var referencedIds = new HashSet<int>();
        for (int i = 0; i < tilemap.TextureIds.Length; i++)
            referencedIds.Add(tilemap.TextureIds[i]);

        Dictionary<int, GrassTextureInfo> classified = GrassMaskLibrary.Classify(
            _database,
            referencedIds,
            BuildClassifySettings(),
            _renderConfig.GrassLogClassification);

        int grassTextures = 0;
        foreach (GrassTextureInfo info in classified.Values)
        {
            if (info.Classification != GrassClassification.NotGrass)
                grassTextures++;
        }

        if (grassTextures == 0)
        {
            Debug.Log(
                $"PlayfieldGrassBuilder: playfield {playfieldId} has no ground textures that " +
                $"classify as grass ({classified.Count} texture(s) examined).");
            yield break;
        }

        List<GrassOccluder> occluders = CollectOccluders(playfieldId);

        List<ChunkSource> chunks = CollectChunks(tilemap, classified, occluders);
        if (chunks.Count == 0)
        {
            Debug.Log($"PlayfieldGrassBuilder: playfield {playfieldId} has no grass-classified tiles.");
            yield break;
        }

        var settings = PlacementSettings.From(_renderConfig, tilemap.MapScale);
        var results = new ChunkResult[chunks.Count];

        Task work = Task.Run(() =>
        {
            Parallel.For(0, chunks.Count, i => results[i] = PlaceChunk(chunks[i], settings));
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
                result.Tints,
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
            $"per tile), {grassTextures}/{classified.Count} ground texture(s) classified as grass, " +
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

        Shader.SetGlobalVector(VariantParamsId, new Vector4(
            ResolveVariantCount(),
            Mathf.Clamp01(_renderConfig.GrassNormalUpBlend),
            Mathf.Clamp01(_renderConfig.GrassGroundTintStrength),
            0f));
    }

    /// <summary>
    /// How many slices the grass Texture2DArray has. Read off the material's own texture
    /// when possible rather than trusting the inspector field - a count larger than the
    /// array clamps to the last slice, so one variant would silently appear far more often
    /// than the rest, which is a horrible thing to have to spot by eye.
    /// </summary>
    int ResolveVariantCount()
    {
        int configured = Mathf.Max(1, _renderConfig.GrassVariantCount);

        Material material = _renderConfig.GrassMaterial;
        if (material != null && material.HasProperty(BaseColorMapId) &&
            material.GetTexture(BaseColorMapId) is Texture2DArray array)
        {
            int actual = Mathf.Max(1, array.depth);
            if (actual != configured)
            {
                Debug.Log(
                    $"PlayfieldGrassBuilder: grass variant count taken from '{array.name}' " +
                    $"({actual} slice(s)); RenderConfig said {configured}.");
            }

            return actual;
        }

        return configured;
    }

    GrassClassifySettings BuildClassifySettings() => new GrassClassifySettings
    {
        MaskResolution = _renderConfig.GrassMaskResolution,
        CellGrassFraction = _renderConfig.GrassMaskThreshold,
        FullCoverage = _renderConfig.GrassFullCoverageThreshold,
        MinCoverage = _renderConfig.GrassMinCoverageThreshold,
        MinSaturation = _renderConfig.GrassMinSaturation,
        MinGreenDominance = _renderConfig.GrassMinGreenDominance,
        ForceGrass = ToSet(_renderConfig.GrassForceTextureIds),
        Exclude = ToSet(_renderConfig.GrassExcludeTextureIds)
    };

    static HashSet<int> ToSet(int[] ids)
        => ids == null || ids.Length == 0 ? null : new HashSet<int>(ids);

    /// <summary>
    /// Reads this playfield's hand-authored exclusion boxes into the oriented-box form the
    /// placement pass tests against.
    ///
    /// These used to be derived from statel renderers, which meant tagging every object
    /// with a layer and walking thousands of renderers at load - more expensive than
    /// building the grass. A zone needs a handful of boxes, so they are authored by hand in
    /// GrassExclusionAuthoring and stored per playfield.
    ///
    /// Oriented, not axis-aligned: a bridge running diagonally has an AABB many times its
    /// own footprint, which would clear grass well to either side of the deck.
    /// </summary>
    List<GrassOccluder> CollectOccluders(int playfieldId)
    {
        var occluders = new List<GrassOccluder>();

        GrassExclusionVolumes asset = _renderConfig.GrassExclusionVolumes;
        List<GrassExclusionVolumes.Volume> volumes = asset != null ? asset.GetVolumes(playfieldId) : null;
        if (volumes == null || volumes.Count == 0)
            return occluders;

        float padding = _renderConfig.GrassOccluderPadding;

        for (int i = 0; i < volumes.Count; i++)
        {
            GrassExclusionVolumes.Volume volume = volumes[i];

            Vector3 size = new Vector3(
                Mathf.Abs(volume.Size.x), Mathf.Abs(volume.Size.y), Mathf.Abs(volume.Size.z));
            if (size.x <= 0f || size.y <= 0f || size.z <= 0f)
                continue;

            Quaternion rotation = Quaternion.Euler(volume.EulerAngles);
            Matrix4x4 trs = Matrix4x4.TRS(volume.Center, rotation, Vector3.one);

            var local = new Bounds(Vector3.zero, size);
            local.Expand(padding * 2f);

            occluders.Add(new GrassOccluder
            {
                Shape = volume.Shape,
                WorldToLocal = trs.inverse,
                LocalBounds = local,

                // A cylinder is inscribed in its box, so the box's world AABB is a valid
                // superset for the broad phase either way.
                WorldBounds = WorldAabb(volume.Center, rotation, local.size)
            });
        }

        Debug.Log($"PlayfieldGrassBuilder: {occluders.Count} exclusion volume(s) for playfield {playfieldId}.");
        return occluders;
    }

    /// <summary>
    /// Axis-aligned bounds of a rotated box, used only as a broad-phase reject before the
    /// exact oriented test.
    /// </summary>
    static Bounds WorldAabb(Vector3 center, Quaternion rotation, Vector3 size)
    {
        Vector3 e = size * 0.5f;
        Matrix4x4 m = Matrix4x4.Rotate(rotation);

        // Project the box's extents onto each world axis: the absolute value of the
        // rotation matrix applied to the extent vector.
        var extent = new Vector3(
            Mathf.Abs(m.m00) * e.x + Mathf.Abs(m.m01) * e.y + Mathf.Abs(m.m02) * e.z,
            Mathf.Abs(m.m10) * e.x + Mathf.Abs(m.m11) * e.y + Mathf.Abs(m.m12) * e.z,
            Mathf.Abs(m.m20) * e.x + Mathf.Abs(m.m21) * e.y + Mathf.Abs(m.m22) * e.z);

        return new Bounds(center, extent * 2f);
    }

    static List<ChunkSource> CollectChunks(
        Tilemap tilemap,
        Dictionary<int, GrassTextureInfo> classified,
        List<GrassOccluder> occluders)
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
                Classified = classified,

                // Pre-filtered per chunk, so the inner placement loop usually iterates an
                // empty array. Statels cluster; most chunks are open ground and pay nothing.
                Occluders = FilterOccluders(
                    occluders,
                    ChunkWorldBounds(i % gridWidth, i / gridWidth, chunkSize, tilemap.MapScale, tilemap.HeightMod, heightmap))
            });
        }

        return chunks;
    }

    static Bounds ChunkWorldBounds(
        int chunkX, int chunkY, int chunkSize, float mapScale, float heightMod, ushort[,] heightmap)
    {
        int fullExtent = chunkSize - 1;
        float minH = float.MaxValue;
        float maxH = float.MinValue;

        for (int y = 0; y <= fullExtent; y++)
        {
            for (int x = 0; x <= fullExtent; x++)
            {
                float h = heightmap[x, y] * heightMod;
                if (h < minH) minH = h;
                if (h > maxH) maxH = h;
            }
        }

        var min = new Vector3(fullExtent * mapScale * chunkX, minH, fullExtent * mapScale * chunkY);
        var max = new Vector3(min.x + fullExtent * mapScale, maxH, min.z + fullExtent * mapScale);

        var bounds = new Bounds();
        bounds.SetMinMax(min, max);
        return bounds;
    }

    static GrassOccluder[] FilterOccluders(List<GrassOccluder> occluders, Bounds chunkBounds)
    {
        if (occluders == null || occluders.Count == 0)
            return Array.Empty<GrassOccluder>();

        List<GrassOccluder> hits = null;
        for (int i = 0; i < occluders.Count; i++)
        {
            if (!occluders[i].WorldBounds.Intersects(chunkBounds))
                continue;

            hits ??= new List<GrassOccluder>();
            hits.Add(occluders[i]);
        }

        return hits == null ? Array.Empty<GrassOccluder>() : hits.ToArray();
    }

    static ChunkResult PlaceChunk(ChunkSource c, PlacementSettings s)
    {
        int fullExtent = c.ChunkSize - 1;
        float mapScale = c.MapScale;

        // Identical to TerrainChunkBuilder.Build's anchorOffset.
        var anchor = new Vector3(fullExtent * mapScale * c.ChunkX, 0f, fullExtent * mapScale * c.ChunkY);

        int perAxis = s.SamplesPerTileAxis;
        float cellStep = 1f / perAxis;

        var instances = new List<Matrix4x4>(1024);
        var tints = new List<uint>(1024);
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

                if (!c.Classified.TryGetValue(rdbId, out GrassTextureInfo info) ||
                    info.Classification == GrassClassification.NotGrass)
                {
                    continue;
                }

                // Null for Full tiles - nothing to reject against, scatter over the lot.
                GrassMask mask = info.Mask;
                GrassPalette palette = info.Palette;

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

                        // Tile-local position in the texture's own un-rotated space, needed
                        // for both the mask and the ground colour.
                        RotateTileUv(fx, fy, rotation, out float ms, out float mt);

                        if (mask != null && !mask.Sample(ms, mt))
                            continue;

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

                        // Reject blades that would grow through static geometry. Tested as
                        // the blade's whole vertical span, not just its base: a base-only
                        // test keeps grass whose tip pokes through a bridge deck, and an
                        // overlap test leaves grass under a raised walkway alone, which a
                        // simple "is anything above me" test would wrongly remove.
                        if (c.Occluders.Length > 0 &&
                            IsOccluded(c.Occluders, position, s.BladeHeight * bladeHeight))
                        {
                            continue;
                        }

                        Quaternion align = Quaternion.Slerp(
                            Quaternion.identity,
                            Quaternion.FromToRotation(Vector3.up, normal),
                            s.NormalAlignment);

                        instances.Add(Matrix4x4.TRS(
                            position,
                            align * Quaternion.Euler(0f, yaw, 0f),
                            new Vector3(bladeWidth, bladeHeight, bladeWidth)));

                        tints.Add(PackTint(palette, ms, mt, s));

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
        {
            return new ChunkResult
            {
                Instances = Array.Empty<Matrix4x4>(),
                Tints = Array.Empty<uint>(),
                Bounds = new Bounds()
            };
        }

        Matrix4x4[] baked = instances.ToArray();
        uint[] bakedTints = tints.ToArray();

        // Shuffle so that ANY prefix of the array is a uniform random sample of the
        // whole chunk. That is what makes distance LOD work: the renderer thins a chunk
        // purely by shrinking instanceCount in the indirect args, with no per-frame
        // filtering and no extra buffers - the blades that drop out are scattered
        // evenly rather than being one contiguous patch that visibly vanishes.
        //
        // Both arrays get the SAME permutation: the shader indexes them with one instance
        // id, so letting them drift apart would tint every blade with some other blade's
        // ground colour.
        uint shuffleSeed = Hash((uint)c.ChunkIndex * 2654435761u + 0x5bd1e995u);
        for (int i = baked.Length - 1; i > 0; i--)
        {
            int j = (int)(Rand01(ref shuffleSeed) * (i + 1));
            j = j > i ? i : j;
            (baked[i], baked[j]) = (baked[j], baked[i]);
            (bakedTints[i], bakedTints[j]) = (bakedTints[j], bakedTints[i]);
        }

        // Pad so the chunk is not culled while a blade at its edge is still on screen:
        // upward by the tallest possible blade, sideways by the widest plus wind travel.
        float padXZ = s.MaxWidth + s.WindStrength + 1f;
        float padY = s.BladeHeight * s.MaxHeight + s.WindStrength + 1f;
        var bounds = new Bounds();
        bounds.SetMinMax(min - new Vector3(padXZ, 0f, padXZ), max + new Vector3(padXZ, padY, padXZ));

        return new ChunkResult { Instances = baked, Tints = bakedTints, Bounds = bounds, Truncated = truncated };
    }

    /// <summary>
    /// The colour of the ground each blade stands on, packed to 8 bits per channel.
    ///
    /// This is the ground's colour, full strength - a TARGET for the shader to shift the
    /// grass texture toward, not a multiplier. How far the blade actually moves toward it is
    /// GrassGroundTintStrength, applied in the shader, so the dial can be tuned live rather
    /// than requiring a rebake.
    ///
    /// The colour is boosted first: growing grass reads richer and slightly brighter than
    /// the dirt-and-grass average of the texture beneath it, so an unboosted target pulls
    /// the field toward mud.
    ///
    /// Stored sRGB-encoded, like a texture would be: 8 bits go further perceptually that way,
    /// and the shader converts to linear on read.
    /// </summary>
    static uint PackTint(GrassPalette palette, float s, float t, PlacementSettings settings)
    {
        Color colour = palette != null
            ? Boost(palette.Sample(s, t), settings)
            : settings.FallbackColour;

        return (uint)Mathf.RoundToInt(Mathf.Clamp01(colour.r) * 255f)
             | ((uint)Mathf.RoundToInt(Mathf.Clamp01(colour.g) * 255f) << 8)
             | ((uint)Mathf.RoundToInt(Mathf.Clamp01(colour.b) * 255f) << 16);
    }

    /// <summary>
    /// Pushes the sampled ground colour toward how grass growing on it should look: more
    /// saturated, a little brighter, and never so dark it reads as black silhouettes.
    /// Done in HSV so hue - the part that actually carries "this patch is yellowed and
    /// dead" - is left exactly as sampled.
    /// </summary>
    static Color Boost(Color ground, PlacementSettings settings)
    {
        Color.RGBToHSV(ground, out float h, out float sat, out float val);

        sat = Mathf.Clamp01(sat * settings.GroundTintSaturation);
        val = Mathf.Clamp01(Mathf.Max(val * settings.GroundTintBrightness, settings.GroundTintMinValue));

        return Color.HSVToRGB(h, sat, val);
    }

    static bool IsOccluded(GrassOccluder[] occluders, Vector3 basePosition, float height)
    {
        Vector3 tip = basePosition + new Vector3(0f, height, 0f);

        for (int i = 0; i < occluders.Length; i++)
        {
            GrassOccluder o = occluders[i];

            // Cheap world-AABB reject before the matrix multiply.
            if (basePosition.y > o.WorldBounds.max.y || tip.y < o.WorldBounds.min.y)
                continue;
            if (basePosition.x < o.WorldBounds.min.x || basePosition.x > o.WorldBounds.max.x ||
                basePosition.z < o.WorldBounds.min.z || basePosition.z > o.WorldBounds.max.z)
            {
                continue;
            }

            Vector3 p0 = o.WorldToLocal.MultiplyPoint3x4(basePosition);
            Vector3 p1 = o.WorldToLocal.MultiplyPoint3x4(tip);

            bool hit = o.Shape == GrassExclusionVolumes.Shape.Cylinder
                ? SegmentIntersectsCylinder(p0, p1, o.LocalBounds.extents)
                : SegmentIntersectsBounds(p0, p1, o.LocalBounds);

            if (hit)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Slab test for a segment against an axis-aligned box, run in the occluder's local
    /// space where its oriented box is axis-aligned. Exact, and cheaper than sampling
    /// points along the blade - which would also miss thin geometry such as a plank deck
    /// between two samples.
    /// </summary>
    static bool SegmentIntersectsBounds(Vector3 p0, Vector3 p1, Bounds box)
    {
        Vector3 d = p1 - p0;
        Vector3 min = box.min;
        Vector3 max = box.max;

        float tMin = 0f;
        float tMax = 1f;

        for (int axis = 0; axis < 3; axis++)
        {
            float origin = p0[axis];
            float delta = d[axis];

            if (Mathf.Abs(delta) < 1e-8f)
            {
                if (origin < min[axis] || origin > max[axis])
                    return false;
                continue;
            }

            float inv = 1f / delta;
            float t1 = (min[axis] - origin) * inv;
            float t2 = (max[axis] - origin) * inv;
            if (t1 > t2)
                (t1, t2) = (t2, t1);

            if (t1 > tMin) tMin = t1;
            if (t2 < tMax) tMax = t2;
            if (tMin > tMax)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Segment against an elliptical cylinder along local Y, in the volume's local space.
    ///
    /// Elliptical rather than circular so a non-uniformly scaled authoring child still
    /// behaves as drawn. Solved as the intersection of two intervals: the Y slab, and the
    /// quadratic where the segment crosses the ellipse - which, after dividing x and z by
    /// their radii, is just a unit circle.
    /// </summary>
    static bool SegmentIntersectsCylinder(Vector3 p0, Vector3 p1, Vector3 extents)
    {
        Vector3 d = p1 - p0;
        float tMin = 0f;
        float tMax = 1f;

        // Y slab.
        if (Mathf.Abs(d.y) < 1e-8f)
        {
            if (p0.y < -extents.y || p0.y > extents.y)
                return false;
        }
        else
        {
            float inv = 1f / d.y;
            float t1 = (-extents.y - p0.y) * inv;
            float t2 = (extents.y - p0.y) * inv;
            if (t1 > t2)
                (t1, t2) = (t2, t1);

            if (t1 > tMin) tMin = t1;
            if (t2 < tMax) tMax = t2;
            if (tMin > tMax)
                return false;
        }

        float rx = Mathf.Max(extents.x, 1e-6f);
        float rz = Mathf.Max(extents.z, 1e-6f);

        float ox = p0.x / rx;
        float oz = p0.z / rz;
        float dx = d.x / rx;
        float dz = d.z / rz;

        float a = dx * dx + dz * dz;
        float c = ox * ox + oz * oz - 1f;

        // Segment is vertical in local space (the common case for an unrotated volume):
        // no quadratic to solve, it is either inside the ellipse for its whole length or
        // outside it for all of it.
        if (a < 1e-12f)
            return c <= 0f;

        float b = 2f * (ox * dx + oz * dz);
        float discriminant = b * b - 4f * a * c;
        if (discriminant < 0f)
            return false;

        float root = Mathf.Sqrt(discriminant);
        float enter = (-b - root) / (2f * a);
        float exit = (-b + root) / (2f * a);

        if (enter > tMin) tMin = enter;
        if (exit < tMax) tMax = exit;
        return tMin <= tMax;
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
        public Color FallbackColour;
        public float GroundTintSaturation;
        public float GroundTintBrightness;
        public float GroundTintMinValue;

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
            MaxInstancesPerChunk = Mathf.Max(1, cfg.GrassMaxInstancesPerChunk),
            FallbackColour = cfg.GrassFallbackColour,
            GroundTintSaturation = Mathf.Max(0f, cfg.GrassGroundTintSaturation),
            GroundTintBrightness = Mathf.Max(0f, cfg.GrassGroundTintBrightness),
            GroundTintMinValue = Mathf.Clamp01(cfg.GrassGroundTintMinValue)
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
        public Dictionary<int, GrassTextureInfo> Classified;
        public GrassOccluder[] Occluders;
    }

    /// <summary>
    /// An oriented box of static geometry that grass must not grow through. Pure value
    /// type - no Unity object references - so worker threads can read it safely.
    /// </summary>
    struct GrassOccluder
    {
        public GrassExclusionVolumes.Shape Shape;
        public Matrix4x4 WorldToLocal;
        public Bounds LocalBounds;
        public Bounds WorldBounds;
    }

    struct ChunkResult
    {
        public Matrix4x4[] Instances;
        public uint[] Tints;
        public Bounds Bounds;
        public bool Truncated;
    }
}