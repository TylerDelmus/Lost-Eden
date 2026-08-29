using System;
using System.Collections.Generic;
using AODB.Common.RDBObjects;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Playfield-level shore wave pool: bakes slots from all Ocean water bodies, then
/// dynamically assigns a shared WaterDecal pool around the local player / camera.
/// </summary>
public sealed class ShoreWaveController : MonoBehaviour
{
    const int HdrpMaxDecals = 48;
    const float KphToMps = 1f / 3.6f;

    struct OceanBodyRegistration
    {
        public float WaterLevel;
        public Bounds WorldBounds;
        public ShoreWaveSettings Settings;
    }

    struct ShoreSlot
    {
        public Vector3 Position;
        public Quaternion Rotation;
    }

    readonly List<OceanBodyRegistration> _bodies = new List<OceanBodyRegistration>();
    ShoreSlot[] _slots = Array.Empty<ShoreSlot>();
    WaterDecal[] _pool = Array.Empty<WaterDecal>();
    int[] _assignedSlot = Array.Empty<int>();

    ShoreWaveSettings _poolSettings = ShoreWaveSettings.Defaults;
    Material _sharedMaterial;
    ResourceDatabase _database;
    int _playfieldId;
    bool _built;

    float _nextUpdateTime;
    Vector3 _lastFocusXz;
    bool _hasFocusSample;

    public void AddOceanBody(float waterLevel, Bounds worldBounds, ShoreWaveSettings settings)
    {
        if (!settings.Enabled)
            return;

        _bodies.Add(new OceanBodyRegistration
        {
            WaterLevel = waterLevel,
            WorldBounds = worldBounds,
            Settings = settings
        });
    }

    public void Build(int playfieldId, ResourceDatabase database, Material shoreWaveMaterialOverride)
    {
        if (_built || _bodies.Count == 0)
            return;

        _built = true;
        _playfieldId = playfieldId;
        _database = database;
        MergePoolSettings();
        _sharedMaterial = shoreWaveMaterialOverride != null
            ? new Material(shoreWaveMaterialOverride) { name = "ShoreWaveRuntime", hideFlags = HideFlags.HideAndDontSave }
            : CreateShoreWaveMaterial(_poolSettings);
        ApplyMaterialProperties(_sharedMaterial, _poolSettings, new Vector2(_poolSettings.RegionSizeX, _poolSettings.RegionSizeZ));

        BakeSlots();
        CreatePool();

        Debug.Log(
            $"[ShoreWave] playfield={playfieldId} bodies={_bodies.Count} slots={_slots.Length} pool={_pool.Length}");
    }

    void Update()
    {
        if (!_built || _slots.Length == 0 || _pool.Length == 0)
            return;

        if (Time.time < _nextUpdateTime)
            return;

        _nextUpdateTime = Time.time + _poolSettings.UpdateInterval;

        if (!TryGetFocusPosition(out Vector3 focus))
            return;

        Vector3 focusXz = new Vector3(focus.x, 0f, focus.z);
        if (_hasFocusSample)
        {
            float moved = Vector2.Distance(
                new Vector2(_lastFocusXz.x, _lastFocusXz.z),
                new Vector2(focusXz.x, focusXz.z));
            if (moved < _poolSettings.MoveThreshold)
                return;
        }

        _hasFocusSample = true;
        _lastFocusXz = focusXz;
        ReassignPool(focusXz);
    }

    void OnDestroy()
    {
        if (_sharedMaterial != null && _sharedMaterial.name.StartsWith("ShoreWaveRuntime", StringComparison.Ordinal))
            Destroy(_sharedMaterial);
    }

    void MergePoolSettings()
    {
        ShoreWaveSettings first = _bodies[0].Settings;
        _poolSettings = first;
        for (int i = 1; i < _bodies.Count; i++)
        {
            ShoreWaveSettings s = _bodies[i].Settings;
            _poolSettings.MaxActive = Mathf.Max(_poolSettings.MaxActive, s.MaxActive);
            _poolSettings.ActivationRadius = Mathf.Max(_poolSettings.ActivationRadius, s.ActivationRadius);
        }

        _poolSettings.MaxActive = Mathf.Clamp(_poolSettings.MaxActive, 1, HdrpMaxDecals);
    }

    void BakeSlots()
    {
        if (_database == null)
            return;

        Tilemap tilemap;
        try
        {
            tilemap = _database.Get<Tilemap>(_playfieldId);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ShoreWave] Tilemap load failed for {_playfieldId}: {ex.Message}");
            return;
        }

        if (tilemap?.Heightmap == null || tilemap.Heightmap.Count == 0)
            return;

        ComputePatchGrid(tilemap.MapWidth, tilemap.MapHeight, out int tileSize, out int cols, out int rows);
        var candidates = new List<ShoreSlot>(256);

        for (int b = 0; b < _bodies.Count; b++)
        {
            OceanBodyRegistration body = _bodies[b];
            BakeBodySlots(tilemap, tileSize, cols, rows, body, candidates);
        }

        _slots = candidates.ToArray();
    }

    void BakeBodySlots(
        Tilemap tilemap,
        int tileSize,
        int cols,
        int rows,
        OceanBodyRegistration body,
        List<ShoreSlot> output)
    {
        ShoreWaveSettings settings = body.Settings;
        float landThreshold = body.WaterLevel + settings.ShoreHeightEpsilon;
        float spacingSq = settings.Spacing * settings.Spacing;
        Vector3 lastKept = new Vector3(float.PositiveInfinity, 0f, float.PositiveInfinity);

        for (int cy = 0; cy < rows; cy++)
        {
            for (int cx = 0; cx < cols; cx++)
            {
                int idx = cols * cy + cx;
                if (idx >= tilemap.Heightmap.Count)
                    continue;

                ushort[,] heightmap = tilemap.Heightmap[idx];
                if (heightmap == null)
                    continue;

                int heightW = heightmap.GetLength(0);
                int heightH = heightmap.GetLength(1);
                int heightCellsX = Math.Max(1, heightW - 1);
                int heightCellsY = Math.Max(1, heightH - 1);
                Vector3 anchor = new Vector3(tileSize * tilemap.MapScale * cx, 0f, tileSize * tilemap.MapScale * cy);
                float worldPerSampleX = tileSize * tilemap.MapScale / heightCellsX;
                float worldPerSampleY = tileSize * tilemap.MapScale / heightCellsY;

                for (int sy = 0; sy < heightH; sy++)
                {
                    for (int sx = 0; sx < heightW; sx++)
                    {
                        bool land = heightmap[sx, sy] * tilemap.HeightMod > landThreshold;
                        if (!land)
                            continue;

                        // Right edge
                        if (sx + 1 < heightW)
                        {
                            bool neighborLand = heightmap[sx + 1, sy] * tilemap.HeightMod > landThreshold;
                            if (!neighborLand)
                            {
                                TryAddEdge(
                                    sx, sy, sx + 1, sy,
                                    worldPerSampleX, worldPerSampleY, anchor,
                                    body, settings, spacingSq, ref lastKept, output,
                                    intoWater: Vector3.right);
                            }
                        }

                        // Up (+Z) edge
                        if (sy + 1 < heightH)
                        {
                            bool neighborLand = heightmap[sx, sy + 1] * tilemap.HeightMod > landThreshold;
                            if (!neighborLand)
                            {
                                TryAddEdge(
                                    sx, sy, sx, sy + 1,
                                    worldPerSampleX, worldPerSampleY, anchor,
                                    body, settings, spacingSq, ref lastKept, output,
                                    intoWater: Vector3.forward);
                            }
                        }
                    }
                }

                // Water→land edges where land is on the +X / +Z side (catch opposite transitions)
                for (int sy = 0; sy < heightH; sy++)
                {
                    for (int sx = 0; sx < heightW; sx++)
                    {
                        bool water = heightmap[sx, sy] * tilemap.HeightMod <= landThreshold;
                        if (!water)
                            continue;

                        if (sx + 1 < heightW)
                        {
                            bool neighborLand = heightmap[sx + 1, sy] * tilemap.HeightMod > landThreshold;
                            if (neighborLand)
                            {
                                TryAddEdge(
                                    sx, sy, sx + 1, sy,
                                    worldPerSampleX, worldPerSampleY, anchor,
                                    body, settings, spacingSq, ref lastKept, output,
                                    intoWater: Vector3.left);
                            }
                        }

                        if (sy + 1 < heightH)
                        {
                            bool neighborLand = heightmap[sx, sy + 1] * tilemap.HeightMod > landThreshold;
                            if (neighborLand)
                            {
                                TryAddEdge(
                                    sx, sy, sx, sy + 1,
                                    worldPerSampleX, worldPerSampleY, anchor,
                                    body, settings, spacingSq, ref lastKept, output,
                                    intoWater: Vector3.back);
                            }
                        }
                    }
                }
            }
        }
    }

    void TryAddEdge(
        int sx0, int sy0, int sx1, int sy1,
        float worldPerSampleX, float worldPerSampleY, Vector3 anchor,
        OceanBodyRegistration body, ShoreWaveSettings settings, float spacingSq,
        ref Vector3 lastKept, List<ShoreSlot> output, Vector3 intoWater)
    {
        Vector3 p0 = SampleWorld(sx0, sy0, worldPerSampleX, worldPerSampleY, anchor, body.WaterLevel);
        Vector3 p1 = SampleWorld(sx1, sy1, worldPerSampleX, worldPerSampleY, anchor, body.WaterLevel);
        Vector3 shore = (p0 + p1) * 0.5f;

        Vector3 n = intoWater.normalized;
        Vector3 t = new Vector3(-n.z, 0f, n.x);
        Vector3 pos = shore + n * settings.DistanceFromLand + t * settings.AlongShoreOffset;

        if (!ContainsXZ(body.WorldBounds, pos, padding: settings.DistanceFromLand + settings.RegionSizeX))
            return;

        if ((pos - lastKept).sqrMagnitude < spacingSq)
            return;

        // WaterDecal shore waves travel along local +X; aim +X toward land (-n).
        Quaternion rot = Quaternion.LookRotation(Vector3.Cross(-n, Vector3.up), Vector3.up);

        lastKept = pos;
        output.Add(new ShoreSlot { Position = pos, Rotation = rot });
    }

    static Vector3 SampleWorld(int sx, int sy, float wpx, float wpy, Vector3 anchor, float waterLevel)
    {
        return new Vector3(sx * wpx, waterLevel, sy * wpy) + anchor;
    }

    static bool ContainsXZ(Bounds bounds, Vector3 point, float padding)
    {
        return point.x >= bounds.min.x - padding && point.x <= bounds.max.x + padding &&
               point.z >= bounds.min.z - padding && point.z <= bounds.max.z + padding;
    }

    void CreatePool()
    {
        int count = Mathf.Min(_poolSettings.MaxActive, HdrpMaxDecals);
        _pool = new WaterDecal[count];
        _assignedSlot = new int[count];
        for (int i = 0; i < count; i++)
            _assignedSlot[i] = -1;

        var poolRoot = new GameObject("ShoreWavePool");
        poolRoot.transform.SetParent(transform, false);

        for (int i = 0; i < count; i++)
        {
            var go = new GameObject($"ShoreWave_{i}");
            go.transform.SetParent(poolRoot.transform, false);
            var decal = go.AddComponent<WaterDecal>();
            decal.regionSize = new Vector2(_poolSettings.RegionSizeX, _poolSettings.RegionSizeZ);
            decal.amplitude = _poolSettings.Amplitude;
            decal.surfaceFoamDimmer = _poolSettings.SurfaceFoamDimmer;
            decal.deepFoamDimmer = _poolSettings.DeepFoamDimmer;
            // Keep resolution modest; shared material (no MPB) atlases as one slot.
            decal.resolution = new Vector2Int(128, 128);
            decal.updateMode = CustomRenderTextureUpdateMode.Realtime;
            decal.material = _sharedMaterial;
            go.SetActive(false);
            _pool[i] = decal;
        }
    }

    void ReassignPool(Vector3 focusXz)
    {
        float radiusSq = _poolSettings.ActivationRadius * _poolSettings.ActivationRadius;
        var scored = new List<(int index, float distSq)>(_slots.Length);
        for (int i = 0; i < _slots.Length; i++)
        {
            Vector3 p = _slots[i].Position;
            float dx = p.x - focusXz.x;
            float dz = p.z - focusXz.z;
            float d2 = dx * dx + dz * dz;
            if (d2 <= radiusSq)
                scored.Add((i, d2));
        }

        scored.Sort((a, b) => a.distSq.CompareTo(b.distSq));
        int take = Mathf.Min(_pool.Length, scored.Count);

        var selected = new HashSet<int>(take);
        for (int i = 0; i < take; i++)
            selected.Add(scored[i].index);

        // Keep stable assignments when possible.
        var usedPool = new bool[_pool.Length];
        for (int i = 0; i < _pool.Length; i++)
        {
            int slot = _assignedSlot[i];
            if (slot >= 0 && selected.Contains(slot))
            {
                usedPool[i] = true;
                selected.Remove(slot);
                ApplySlot(_pool[i], _slots[slot]);
            }
            else
            {
                _assignedSlot[i] = -1;
            }
        }

        int nextSelected = 0;
        int[] remaining = new int[selected.Count];
        foreach (int slot in selected)
            remaining[nextSelected++] = slot;
        Array.Sort(remaining, (a, b) =>
        {
            float da = DistSqXZ(_slots[a].Position, focusXz);
            float db = DistSqXZ(_slots[b].Position, focusXz);
            return da.CompareTo(db);
        });

        int remIdx = 0;
        for (int i = 0; i < _pool.Length; i++)
        {
            if (usedPool[i])
                continue;

            if (remIdx >= remaining.Length)
            {
                _pool[i].gameObject.SetActive(false);
                _assignedSlot[i] = -1;
                continue;
            }

            int slot = remaining[remIdx++];
            _assignedSlot[i] = slot;
            ApplySlot(_pool[i], _slots[slot]);
        }
    }

    static float DistSqXZ(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return dx * dx + dz * dz;
    }

    static void ApplySlot(WaterDecal decal, ShoreSlot slot)
    {
        Transform t = decal.transform;
        t.SetPositionAndRotation(slot.Position, slot.Rotation);
        if (!decal.gameObject.activeSelf)
            decal.gameObject.SetActive(true);
    }

    static bool TryGetFocusPosition(out Vector3 focus)
    {
        PlayerController playerController = FindFirstObjectByType<PlayerController>();
        if (playerController != null && playerController.TryGetLocalPlayer(out Character local) && local != null)
        {
            focus = local.transform.position;
            return true;
        }

        Camera cam = Camera.main;
        if (cam != null)
        {
            focus = cam.transform.position;
            return true;
        }

        focus = default;
        return false;
    }

    const string ShoreWaveResourcePath = "Materials/WaterDecalShoreWave";

    static Material CreateShoreWaveMaterial(ShoreWaveSettings settings)
    {
        Material template = Resources.Load<Material>(ShoreWaveResourcePath);
        if (template == null || template.shader == null)
        {
            Debug.LogError(
                $"[ShoreWave] Missing Resources/{ShoreWaveResourcePath}.mat (HDRP Water Decal Sample).");
            return new Material(Shader.Find("Hidden/InternalErrorShader") ?? Shader.Find("Hidden/InternalClear"));
        }

        var material = new Material(template)
        {
            name = "ShoreWaveRuntime",
            hideFlags = HideFlags.HideAndDontSave
        };
        material.SetFloat("_TYPE", 3f); // ShoreWave
        material.SetFloat("_AffectDeformation", 1f);
        material.SetFloat("_AffectFoam", 1f);
        ApplyMaterialProperties(material, settings, new Vector2(settings.RegionSizeX, settings.RegionSizeZ));
        return material;
    }

    static void ApplyMaterialProperties(Material material, ShoreWaveSettings settings, Vector2 regionSize)
    {
        float size = Mathf.Max(regionSize.x, regionSize.y, 0.01f);
        material.SetFloat("_Wave_Length", settings.Wavelength / size);
        material.SetFloat("_Skipped_Waves", Mathf.Max(1f, settings.SkippedWaves));
        material.SetFloat("_Wave_Speed", settings.Speed * KphToMps / size);
        material.SetFloat("_Wave_Offset", settings.WaveOffset / size);
        material.SetVector("_Wave_Blend", settings.BlendRange);
        material.SetVector("_Breaking_Range", settings.BreakingRange);
        material.SetVector("_Deep_Foam_Range", settings.DeepFoamRange);
        material.SetFloat("_AffectFoam", 1f);
        material.SetFloat("_AffectDeformation", 1f);
        material.SetFloat("_TYPE", 3f);
    }

    /// <summary>
    /// Matches <see cref="TerrainParser"/> patch grid: tile_size from ctz of (mapDim-1), capped at LOD 6.
    /// </summary>
    static void ComputePatchGrid(uint mapWidth, uint mapHeight, out int tileSize, out int cols, out int rows)
    {
        int w = Math.Max(1, (int)mapWidth);
        int h = Math.Max(1, (int)mapHeight);
        int lod = Math.Min(Math.Min(CountTrailingZeros(w - 1), CountTrailingZeros(h - 1)), 6);
        tileSize = 1 << lod;
        cols = Math.Max(1, (w - 1) / tileSize);
        rows = Math.Max(1, (h - 1) / tileSize);
    }

    static int CountTrailingZeros(int value)
    {
        if (value == 0)
            return 32;

        int count = 0;
        while ((value & 1) == 0)
        {
            value >>= 1;
            count++;
        }

        return count;
    }
}
