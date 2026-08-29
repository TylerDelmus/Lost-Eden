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
        public float AmplitudeScale;
    }

    readonly List<OceanBodyRegistration> _bodies = new List<OceanBodyRegistration>();
    ShoreSlot[] _slots = Array.Empty<ShoreSlot>();
    WaterDecal[] _pool = Array.Empty<WaterDecal>();
    int[] _assignedSlot = Array.Empty<int>();

    ShoreWaveSettings _poolSettings = ShoreWaveSettings.Defaults;
    Material _sharedMaterial;
    readonly List<Material> _instanceMaterials = new List<Material>();
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

        Vector2 regionSize = _poolSettings.ResolveRegionSize();
        _poolSettings.RegionSizeX = regionSize.x;
        _poolSettings.RegionSizeZ = regionSize.y;
        _poolSettings.Wavelength = _poolSettings.ResolveWavelength(regionSize);
        _poolSettings.ResolveBreaking(regionSize, out Vector2 breaking, out Vector2 deepFoam);
        _poolSettings.BreakingRange = breaking;
        _poolSettings.DeepFoamRange = deepFoam;
        _poolSettings.WaveOffset = _poolSettings.ResolveWaveOffset(regionSize);

        _sharedMaterial = shoreWaveMaterialOverride != null
            ? new Material(shoreWaveMaterialOverride) { name = "ShoreWaveRuntime", hideFlags = HideFlags.HideAndDontSave }
            : CreateShoreWaveMaterial(_poolSettings);
        ApplyMaterialProperties(_sharedMaterial, _poolSettings, regionSize);

        BakeSlots();
        CreatePool();

        Debug.Log(
            $"[ShoreWave] playfield={playfieldId} bodies={_bodies.Count} slots={_slots.Length} pool={_pool.Length} " +
            $"region=({regionSize.x:F1},{regionSize.y:F1}) wavelength={_poolSettings.Wavelength:F1}");
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
        for (int i = 0; i < _instanceMaterials.Count; i++)
        {
            if (_instanceMaterials[i] != null)
                Destroy(_instanceMaterials[i]);
        }

        _instanceMaterials.Clear();

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

        if (!TryGetChunkGrid(tilemap, out int tileSize, out int cols, out int rows))
            return;

        var candidates = new List<ShoreSlot>(256);

        for (int b = 0; b < _bodies.Count; b++)
        {
            OceanBodyRegistration body = _bodies[b];
            BakeBodySlots(tilemap, tileSize, cols, rows, body, candidates);
        }

        _slots = candidates.ToArray();
    }

    struct ShoreSeed
    {
        public Vector2 Position;
        public Vector2 OutwardNormal;
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
        var heightField = new HeightField(tilemap, tileSize, cols, rows, landThreshold);

        var seeds = new List<ShoreSeed>(512);
        CollectShoreSeeds(tilemap, tileSize, cols, rows, landThreshold, seeds);
        if (seeds.Count == 0)
            return;

        // Average outward normals along the coast so cove walls don't yank waves inland.
        float smoothRadius = Mathf.Max(settings.Spacing * 2.5f, settings.DistanceFromLand * 0.75f);
        SmoothShoreNormals(seeds, smoothRadius);

        var landEdgePoints = new List<Vector2>(seeds.Count);
        for (int i = 0; i < seeds.Count; i++)
            landEdgePoints.Add(seeds[i].Position);

        var rng = new System.Random(unchecked(_playfieldId * 73856093 ^ seeds.Count * 19349663));
        Vector2 regionSize = settings.ResolveRegionSize();
        float pad = regionSize.x * 0.5f + settings.RegionSizeZ;

        var candidates = new List<ShoreSlot>(seeds.Count);
        for (int i = 0; i < seeds.Count; i++)
        {
            if (rng.NextDouble() < settings.SpawnSkipChance)
                continue;

            ShoreSeed seed = seeds[i];
            Vector2 n = seed.OutwardNormal;
            if (n.sqrMagnitude < 0.01f)
                continue;

            float angleRad = (float)((rng.NextDouble() * 2.0 - 1.0) * settings.SpawnJitterAngleDeg * Mathf.Deg2Rad);
            float cos = Mathf.Cos(angleRad);
            float sin = Mathf.Sin(angleRad);
            Vector2 outward = new Vector2(n.x * cos - n.y * sin, n.x * sin + n.y * cos).normalized;
            Vector2 tangent = new Vector2(-outward.y, outward.x);

            float dist = settings.DistanceFromLand * (1f + (float)(rng.NextDouble() * 2.0 - 1.0) * settings.SpawnJitterDistance);
            dist = Mathf.Max(1f, dist);
            float along = settings.AlongShoreOffset
                + (float)(rng.NextDouble() * 2.0 - 1.0) * settings.Spacing * settings.SpawnJitterAlong;

            Vector2 waveXz = seed.Position + outward * dist + tangent * along;

            if (heightField.IsLand(waveXz.x, waveXz.y))
                continue;

            Vector3 wavePos = new Vector3(waveXz.x, body.WaterLevel, waveXz.y);
            if (!ContainsXZ(body.WorldBounds, wavePos, padding: pad))
                continue;

            if (!TryFindClosest(landEdgePoints, waveXz, out Vector2 closestLand, out float clearance))
                continue;

            if (clearance < dist * 0.7f)
                continue;

            Vector2 toLand = (closestLand - waveXz).normalized;
            if (Vector2.Dot(toLand, outward) > -0.15f)
                continue;

            Vector3 towardLand3 = new Vector3(toLand.x, 0f, toLand.y);
            Quaternion rot = Quaternion.LookRotation(Vector3.Cross(towardLand3, Vector3.up), Vector3.up);
            float ampScale = 0.75f + (float)rng.NextDouble() * 0.5f;
            candidates.Add(new ShoreSlot
            {
                Position = wavePos,
                Rotation = rot,
                AmplitudeScale = ampScale
            });
        }

        // Euclidean min-distance uses Spacing so coves cannot pack; keep Z-overlap allowed
        // so a long coast still gets generators within alongCoastRange of the camera.
        float minSeparation = Mathf.Max(settings.Spacing, regionSize.y * 0.35f);
        AppendEvenlySpaced(candidates, minSeparation, settings.SpacingJitter, rng, output);
    }

    /// <summary>
    /// Orders candidates around their centroid (coast loop) and keeps points at ~spacing
    /// along that path so generators are evenly distributed instead of scan-order clumps.
    /// Also enforces euclidean distance to every accepted slot so cove collapses cannot pack.
    /// </summary>
    static void AppendEvenlySpaced(
        List<ShoreSlot> candidates,
        float spacing,
        float spacingJitter,
        System.Random rng,
        List<ShoreSlot> output)
    {
        if (candidates == null || candidates.Count == 0)
            return;

        spacing = Mathf.Max(1f, spacing);
        if (candidates.Count == 1)
        {
            if (IsFarFromAccepted(output, candidates[0].Position, spacing))
                output.Add(candidates[0]);
            return;
        }

        Vector2 centroid = Vector2.zero;
        for (int i = 0; i < candidates.Count; i++)
            centroid += new Vector2(candidates[i].Position.x, candidates[i].Position.z);
        centroid /= candidates.Count;

        candidates.Sort((a, b) =>
        {
            float aa = Mathf.Atan2(a.Position.z - centroid.y, a.Position.x - centroid.x);
            float bb = Mathf.Atan2(b.Position.z - centroid.y, b.Position.x - centroid.x);
            return aa.CompareTo(bb);
        });

        // Rotate starting index so spacing phase isn't identical every load.
        int rotate = rng.Next(0, candidates.Count);
        if (rotate > 0)
        {
            var rotated = new List<ShoreSlot>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++)
                rotated.Add(candidates[(i + rotate) % candidates.Count]);
            candidates = rotated;
        }

        float dedupeSq = (spacing * 0.25f) * (spacing * 0.25f);
        var ordered = new List<ShoreSlot>(candidates.Count) { candidates[0] };
        for (int i = 1; i < candidates.Count; i++)
        {
            Vector3 prev = ordered[ordered.Count - 1].Position;
            Vector3 cur = candidates[i].Position;
            float dx = cur.x - prev.x;
            float dz = cur.z - prev.z;
            if (dx * dx + dz * dz >= dedupeSq)
                ordered.Add(candidates[i]);
        }

        if (ordered.Count == 0)
            return;

        int startOutput = output.Count;
        if (!TryAcceptSpaced(output, ordered[0], spacing))
            return;

        float traveled = 0f;
        Vector3 lastPos = ordered[0].Position;
        float nextGap = spacing * (1f + (float)(rng.NextDouble() * 2.0 - 1.0) * spacingJitter);
        nextGap = Mathf.Max(spacing * 0.5f, nextGap);

        for (int i = 1; i < ordered.Count; i++)
        {
            Vector3 cur = ordered[i].Position;
            float dx = cur.x - lastPos.x;
            float dz = cur.z - lastPos.z;
            traveled += Mathf.Sqrt(dx * dx + dz * dz);
            if (traveled < nextGap)
                continue;

            if (!TryAcceptSpaced(output, ordered[i], spacing))
            {
                // Still advance along the path so we don't stall on a packed cove cluster.
                lastPos = cur;
                traveled = 0f;
                continue;
            }

            lastPos = cur;
            traveled = 0f;
            nextGap = spacing * (1f + (float)(rng.NextDouble() * 2.0 - 1.0) * spacingJitter);
            nextGap = Mathf.Max(spacing * 0.5f, nextGap);
        }

        if (output.Count - startOutput >= 2)
        {
            Vector3 first = output[startOutput].Position;
            Vector3 last = output[output.Count - 1].Position;
            float closeDx = last.x - first.x;
            float closeDz = last.z - first.z;
            if (closeDx * closeDx + closeDz * closeDz < spacing * spacing)
                output.RemoveAt(output.Count - 1);
        }
    }

    static bool TryAcceptSpaced(List<ShoreSlot> output, ShoreSlot slot, float minDist)
    {
        if (!IsFarFromAccepted(output, slot.Position, minDist))
            return false;
        output.Add(slot);
        return true;
    }

    static bool IsFarFromAccepted(List<ShoreSlot> output, Vector3 pos, float minDist)
    {
        float minSq = minDist * minDist;
        for (int i = 0; i < output.Count; i++)
        {
            float dx = output[i].Position.x - pos.x;
            float dz = output[i].Position.z - pos.z;
            if (dx * dx + dz * dz < minSq)
                return false;
        }

        return true;
    }

    static void CollectShoreSeeds(
        Tilemap tilemap,
        int tileSize,
        int cols,
        int rows,
        float landThreshold,
        List<ShoreSeed> seeds)
    {
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
                float wpx = tileSize * tilemap.MapScale / heightCellsX;
                float wpz = tileSize * tilemap.MapScale / heightCellsY;

                for (int sy = 0; sy < heightH; sy++)
                {
                    for (int sx = 0; sx < heightW; sx++)
                    {
                        if (heightmap[sx, sy] * tilemap.HeightMod <= landThreshold)
                            continue;

                        Vector2 landXz = new Vector2(sx * wpx + anchor.x, sy * wpz + anchor.z);
                        Vector2 outward = Vector2.zero;
                        int waterNeighbors = 0;

                        void Accrue(int nx, int nz)
                        {
                            if (nx < 0 || nz < 0 || nx >= heightW || nz >= heightH)
                                return;
                            if (heightmap[nx, nz] * tilemap.HeightMod > landThreshold)
                                return;

                            Vector2 waterXz = new Vector2(nx * wpx + anchor.x, nz * wpz + anchor.z);
                            Vector2 dir = waterXz - landXz;
                            if (dir.sqrMagnitude < 1e-8f)
                                return;
                            outward += dir.normalized;
                            waterNeighbors++;
                        }

                        Accrue(sx + 1, sy);
                        Accrue(sx - 1, sy);
                        Accrue(sx, sy + 1);
                        Accrue(sx, sy - 1);
                        Accrue(sx + 1, sy + 1);
                        Accrue(sx - 1, sy + 1);
                        Accrue(sx + 1, sy - 1);
                        Accrue(sx - 1, sy - 1);

                        if (waterNeighbors == 0 || outward.sqrMagnitude < 1e-8f)
                            continue;

                        outward.Normalize();
                        // Seed sits just into water from the land sample along the terrain normal.
                        float step = Mathf.Max(wpx, wpz) * 0.5f;
                        seeds.Add(new ShoreSeed
                        {
                            Position = landXz + outward * step,
                            OutwardNormal = outward
                        });
                    }
                }
            }
        }
    }

    static void SmoothShoreNormals(List<ShoreSeed> seeds, float radius)
    {
        float radiusSq = radius * radius;
        var smoothed = new Vector2[seeds.Count];
        for (int i = 0; i < seeds.Count; i++)
        {
            Vector2 p = seeds[i].Position;
            Vector2 sum = Vector2.zero;
            int count = 0;
            for (int j = 0; j < seeds.Count; j++)
            {
                if ((seeds[j].Position - p).sqrMagnitude > radiusSq)
                    continue;
                sum += seeds[j].OutwardNormal;
                count++;
            }

            Vector2 n = count > 0 ? sum : seeds[i].OutwardNormal;
            if (n.sqrMagnitude > 1e-8f)
                n.Normalize();
            else
                n = seeds[i].OutwardNormal;
            smoothed[i] = n;
        }

        for (int i = 0; i < seeds.Count; i++)
        {
            ShoreSeed seed = seeds[i];
            seed.OutwardNormal = smoothed[i];
            seeds[i] = seed;
        }
    }

    sealed class HeightField
    {
        readonly Tilemap _tilemap;
        readonly int _tileSize;
        readonly int _cols;
        readonly int _rows;
        readonly float _landThreshold;

        public HeightField(Tilemap tilemap, int tileSize, int cols, int rows, float landThreshold)
        {
            _tilemap = tilemap;
            _tileSize = tileSize;
            _cols = cols;
            _rows = rows;
            _landThreshold = landThreshold;
        }

        public bool IsLand(float worldX, float worldZ)
        {
            float mapScale = _tilemap.MapScale;
            if (mapScale <= 0f)
                return false;

            float cell = _tileSize * mapScale;
            int cx = Mathf.FloorToInt(worldX / cell);
            int cy = Mathf.FloorToInt(worldZ / cell);
            if (cx < 0 || cy < 0 || cx >= _cols || cy >= _rows)
                return false;

            int idx = _cols * cy + cx;
            if (idx < 0 || idx >= _tilemap.Heightmap.Count)
                return false;

            ushort[,] heightmap = _tilemap.Heightmap[idx];
            if (heightmap == null)
                return false;

            int heightW = heightmap.GetLength(0);
            int heightH = heightmap.GetLength(1);
            int heightCellsX = Math.Max(1, heightW - 1);
            int heightCellsY = Math.Max(1, heightH - 1);
            float localX = worldX - cx * cell;
            float localZ = worldZ - cy * cell;
            float wpx = cell / heightCellsX;
            float wpz = cell / heightCellsY;
            int sx = Mathf.Clamp(Mathf.RoundToInt(localX / wpx), 0, heightW - 1);
            int sy = Mathf.Clamp(Mathf.RoundToInt(localZ / wpz), 0, heightH - 1);
            return heightmap[sx, sy] * _tilemap.HeightMod > _landThreshold;
        }
    }

    static bool TryFindClosest(List<Vector2> points, Vector2 query, out Vector2 closest, out float distance)
    {
        closest = default;
        distance = float.PositiveInfinity;
        if (points == null || points.Count == 0)
            return false;

        for (int i = 0; i < points.Count; i++)
        {
            float d = (points[i] - query).magnitude;
            if (d < distance)
            {
                distance = d;
                closest = points[i];
            }
        }

        return distance < float.PositiveInfinity;
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
        var rng = new System.Random(unchecked(_playfieldId * 83492791 ^ count * 19349663));

        Vector2 regionSize = new Vector2(_poolSettings.RegionSizeX, _poolSettings.RegionSizeZ);
        int res = Mathf.Clamp(Mathf.RoundToInt(_poolSettings.RegionSizeX * 2f), 64, 256);

        for (int i = 0; i < count; i++)
        {
            var go = new GameObject($"ShoreWave_{i}");
            go.transform.SetParent(poolRoot.transform, false);
            var decal = go.AddComponent<WaterDecal>();
            decal.regionSize = regionSize;
            decal.amplitude = _poolSettings.Amplitude;
            decal.surfaceFoamDimmer = _poolSettings.SurfaceFoamDimmer;
            decal.deepFoamDimmer = _poolSettings.DeepFoamDimmer;
            decal.resolution = new Vector2Int(res, Mathf.Max(64, res / 2));
            decal.updateMode = CustomRenderTextureUpdateMode.Realtime;

            // Unique mat so each generator can roll its own skip count around the average.
            var mat = new Material(_sharedMaterial)
            {
                name = $"ShoreWaveRuntime_{i}",
                hideFlags = HideFlags.HideAndDontSave
            };
            int skipped = _poolSettings.RollSkippedWaves(rng);
            mat.SetFloat("_Skipped_Waves", skipped);
            _instanceMaterials.Add(mat);
            decal.material = mat;

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

    void ApplySlot(WaterDecal decal, ShoreSlot slot)
    {
        Transform t = decal.transform;
        t.SetPositionAndRotation(slot.Position, slot.Rotation);
        decal.amplitude = _poolSettings.Amplitude * Mathf.Max(0.1f, slot.AmplitudeScale);
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
    /// Outdoor chunk grid from AODB Tilemap (ChunkedGround): step = ChunkSize-1, cols = GridWidth.
    /// </summary>
    static bool TryGetChunkGrid(Tilemap tilemap, out int tileSize, out int cols, out int rows)
    {
        tileSize = 0;
        cols = 0;
        rows = 0;

        if (tilemap == null || tilemap.ChunkSize <= 1 || tilemap.GridWidth <= 0)
            return false;

        tileSize = tilemap.ChunkSize - 1;
        cols = tilemap.GridWidth;
        if (tilemap.Heightmap.Count % cols != 0)
            return false;

        rows = tilemap.Heightmap.Count / cols;
        return rows > 0;
    }
}
