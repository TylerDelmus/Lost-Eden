using System;
using System.Collections.Generic;
using AODB.Common.RDBObjects;
using UnityEngine;

/// <summary>
/// Streams per-cell collision surfaces (RDB 1000013 / <see cref="SurfaceResource"/>)
/// for the locality desired set. Warm-caches colliders just outside the neighborhood.
/// </summary>
public sealed class SurfaceCellLoader : ICellResourceLoader
{
    public const int WarmCacheCap = 64;
    public const int MaxAppliesPerFrame = 1;
    public const int MaxLoadsPerFrame = 2;
    public const int PriorityBurstLoads = 8;
    public const int PriorityBurstApplies = 8;
    public const int PriorityNeighborRadius = 1;

    enum CellState
    {
        Absent,
        Queued,
        Loading,
        Ready,
        Cached,
    }

    sealed class CellEntry
    {
        public CellState State;
        public int Generation;
        public bool Desired;
        public int CellId;
        public SurfaceCollisionBuilder.MeshData MeshData;
        public LostEden.Vehicles.Surfaces.TriangleMeshSurface Surface;
    }

    sealed class PreparedSurface
    {
        public int CellId;
        public int Generation;
        public SurfaceCollisionBuilder.MeshData MeshData;
    }

    readonly ResourceDatabase _database;
    readonly IPlayfieldCellLayout _layout;
    readonly Transform _parent;
    readonly Dictionary<int, CellEntry> _entries = new();
    readonly HashSet<int> _unavailable = new();
    readonly List<int> _queue = new();
    readonly List<int> _warmOrder = new();
    readonly List<int> _scratch = new();
    readonly List<int> _priorityNeighbors = new();
    readonly Queue<PreparedSurface> _prepared = new();

    int _generation;
    int _referenceCellId = -1;
    int _burstLoads;
    int _burstApplies;

    /// <summary>
    /// The movement collision grid this streams into — <c>CellSurface_t</c>. Set by
    /// <c>PlayfieldFactory</c> once the playfield's <c>TilemapSurface</c> exists. Null means the cells
    /// are decoded and then dropped, which is what happens on an indoor playfield.
    ///
    /// <para>
    /// This replaces the <c>MeshCollider</c> per cell that used to be created here. Character movement
    /// no longer goes through Unity physics at all — it goes through <c>Surface_i</c>, so the colliders
    /// were a second, parallel copy of the same RDB 1000013 geometry. Streaming into
    /// <c>SetSurfaceForCell</c>/<c>RemoveSurfaceForCell</c> is also what stock does: <c>n3Zone_t</c> has
    /// both <c>LoadSurface</c> and <c>UnLoadSurface</c>. See Docs/Movement.md §8.
    /// </para>
    /// </summary>
    public LostEden.Vehicles.Surfaces.CellSurface CollisionSurface { get; set; }

    public SurfaceCellLoader(ResourceDatabase database, IPlayfieldCellLayout layout, Transform parent)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _parent = parent ?? throw new ArgumentNullException(nameof(parent));
    }

    public void SetReferenceCell(int cellId)
    {
        if (cellId == _referenceCellId)
            return;

        _referenceCellId = cellId;
        ResortQueue();
    }

    public SurfaceCollisionState GetCollisionState(Vector3 worldPosition)
    {
        if (_layout.IsIndoor)
            return SurfaceCollisionState.Unavailable;

        if (!_layout.TryGetCellId(worldPosition, out int cellId))
            return SurfaceCollisionState.Unavailable;

        return GetCollisionState(cellId);
    }

    public SurfaceCollisionState GetCollisionState(int cellId)
    {
        if (_layout.IsIndoor)
            return SurfaceCollisionState.Unavailable;

        if (_unavailable.Contains(cellId))
            return SurfaceCollisionState.Unavailable;

        if (!_entries.TryGetValue(cellId, out CellEntry entry))
            return SurfaceCollisionState.Pending;

        switch (entry.State)
        {
            case CellState.Ready:
                return entry.Surface != null
                    ? SurfaceCollisionState.Ready
                    : SurfaceCollisionState.Pending;
            case CellState.Cached:
            case CellState.Queued:
            case CellState.Loading:
                return SurfaceCollisionState.Pending;
            default:
                return SurfaceCollisionState.Pending;
        }
    }

    /// <summary>
    /// Request the standing cell (and near neighbors), bump them to the front of the
    /// load queue, and grant a one-shot burst so zone-enter clears quickly.
    /// </summary>
    public void PrioritizeAround(Vector3 worldPosition)
    {
        if (_layout.IsIndoor)
            return;

        if (!_layout.TryGetCellId(worldPosition, out int cellId))
            return;

        _referenceCellId = cellId;
        _unavailable.Remove(cellId);

        _layout.CollectNeighbors(cellId, PriorityNeighborRadius, _priorityNeighbors);
        for (int i = 0; i < _priorityNeighbors.Count; i++)
        {
            int id = _priorityNeighbors[i];
            _unavailable.Remove(id);
            RequestDesired(id);
        }

        BumpQueueFront(cellId);
        for (int i = 0; i < _priorityNeighbors.Count; i++)
        {
            int id = _priorityNeighbors[i];
            if (id != cellId)
                BumpQueueFront(id);
        }

        // Standing cell first after neighbor bumps.
        BumpQueueFront(cellId);

        _burstLoads = Math.Max(_burstLoads, PriorityBurstLoads);
        _burstApplies = Math.Max(_burstApplies, PriorityBurstApplies);
        ResortQueue();
    }

    public void OnCellsFound(IReadOnlyList<int> cellIds)
    {
        for (int i = 0; i < cellIds.Count; i++)
            RequestDesired(cellIds[i]);

        ResortQueue();
    }

    public void OnCellsLost(IReadOnlyList<int> cellIds)
    {
        for (int i = 0; i < cellIds.Count; i++)
            LeaveDesired(cellIds[i]);

        TrimWarmCache();
    }

    public void Tick()
    {
        PumpLoads();
        PumpApplies();
    }

    public void Clear()
    {
        _generation++;
        _queue.Clear();
        _warmOrder.Clear();
        _prepared.Clear();
        _unavailable.Clear();
        _burstLoads = 0;
        _burstApplies = 0;

        foreach (var kv in _entries)
            DestroyCollider(kv.Value);

        _entries.Clear();
    }

    void RequestDesired(int cellId)
    {
        if (!_entries.TryGetValue(cellId, out CellEntry entry))
        {
            entry = new CellEntry { CellId = cellId };
            _entries[cellId] = entry;
        }

        entry.Desired = true;
        _warmOrder.Remove(cellId);

        switch (entry.State)
        {
            case CellState.Ready:
            case CellState.Loading:
                return;
            case CellState.Cached:
                entry.State = CellState.Ready;
                if (entry.Surface != null)
                    CollisionSurface?.SetSurfaceForCell(entry.CellId, entry.Surface);
                return;
            case CellState.Queued:
                return;
            default:
                entry.State = CellState.Queued;
                entry.Generation = ++_generation;
                if (!_queue.Contains(cellId))
                    _queue.Add(cellId);
                break;
        }
    }

    void LeaveDesired(int cellId)
    {
        if (!_entries.TryGetValue(cellId, out CellEntry entry))
            return;

        entry.Desired = false;
        entry.Generation = ++_generation;

        switch (entry.State)
        {
            case CellState.Queued:
                _queue.Remove(cellId);
                _entries.Remove(cellId);
                break;
            case CellState.Loading:
                _entries.Remove(cellId);
                break;
            case CellState.Ready:
                entry.State = CellState.Cached;
                if (entry.Surface != null)
                    CollisionSurface?.RemoveSurfaceForCell(entry.CellId, entry.Surface);
                TouchWarm(cellId);
                break;
            case CellState.Cached:
                TouchWarm(cellId);
                break;
        }
    }

    void PumpLoads()
    {
        int budget = MaxLoadsPerFrame + _burstLoads;
        _burstLoads = 0;
        int loaded = 0;
        while (loaded < budget && _queue.Count > 0)
        {
            int cellId = _queue[0];
            _queue.RemoveAt(0);

            if (!_entries.TryGetValue(cellId, out CellEntry entry) || entry.State != CellState.Queued || !entry.Desired)
                continue;

            entry.State = CellState.Loading;
            int generation = entry.Generation;
            int instanceId = SurfaceInstanceId(cellId);
            _layout.GetCellCoords(cellId, out int ix, out int iz);

            Debug.Log(
                $"[Surface] LoadSurface cell={cellId} ix={ix} iz={iz} " +
                $"rdb=({(int)ResourceTypeId.SurfaceResource}, {instanceId})");

            SurfaceResource resource = null;
            try
            {
                resource = _database.Get<SurfaceResource>(instanceId);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Surface] Get SurfaceResource failed for cell {cellId}: {ex.Message}");
            }

            if (!_entries.TryGetValue(cellId, out entry) || entry.Generation != generation || !entry.Desired)
                continue;

            if (resource == null || !SurfaceCollisionBuilder.TryBuild(resource, out SurfaceCollisionBuilder.MeshData meshData))
            {
                Debug.Log($"[Surface] Skip missing/empty surface cell={cellId}");
                MarkUnavailable(cellId);
                loaded++;
                continue;
            }

            _prepared.Enqueue(new PreparedSurface
            {
                CellId = cellId,
                Generation = generation,
                MeshData = meshData,
            });
            loaded++;
        }
    }

    void PumpApplies()
    {
        int budget = MaxAppliesPerFrame + _burstApplies;
        _burstApplies = 0;
        int applied = 0;
        while (applied < budget && _prepared.Count > 0)
        {
            PreparedSurface prepared = _prepared.Dequeue();
            if (!_entries.TryGetValue(prepared.CellId, out CellEntry entry)
                || entry.Generation != prepared.Generation
                || !entry.Desired)
                continue;

            if (!TryCreateCollider(prepared.CellId, prepared.MeshData, entry))
            {
                MarkUnavailable(prepared.CellId);
                applied++;
                continue;
            }

            entry.MeshData = prepared.MeshData;
            entry.State = CellState.Ready;
            applied++;
        }
    }

    void MarkUnavailable(int cellId)
    {
        if (_entries.TryGetValue(cellId, out CellEntry entry))
        {
            DestroyCollider(entry);
            _entries.Remove(cellId);
        }

        _queue.Remove(cellId);
        _unavailable.Add(cellId);
    }

    void BumpQueueFront(int cellId)
    {
        if (!_queue.Contains(cellId))
            return;

        _queue.Remove(cellId);
        _queue.Insert(0, cellId);
    }

    /// <summary>
    /// Registers a decoded cell as a <c>Surface_i</c> in the playfield's <c>CellSurface_t</c>. The
    /// loader's cell id is stock's cell index already: it builds the record instance as
    /// <c>(playfieldId &lt;&lt; 16) | cellId</c> and lays the grid out row-major as
    /// <c>ix = id % NumZonesX</c>, both identical to what <c>n3Zone_t::LoadSurface</c> does — so the id
    /// passes straight through with no remapping.
    /// </summary>
    bool TryCreateCollider(int cellId, SurfaceCollisionBuilder.MeshData data, CellEntry entry)
    {
        if (data?.Vertices == null || data.Triangles == null || data.Vertices.Length == 0 || data.Triangles.Length < 3)
            return false;

        if (CollisionSurface == null)
            return false;

        var vertices = new LostEden.Vehicles.Vec3[data.Vertices.Length];
        for (int i = 0; i < data.Vertices.Length; i++)
            vertices[i] = new LostEden.Vehicles.Vec3(
                data.Vertices[i].x, data.Vertices[i].y, data.Vertices[i].z);

        LostEden.Vehicles.Surfaces.TriangleMeshSurface mesh =
            LostEden.Vehicles.PlayfieldCellSurface.BuildCell(vertices, data.Triangles);
        if (mesh == null)
            return false;

        CollisionSurface.SetSurfaceForCell(cellId, mesh);
        entry.Surface = mesh;
        return true;
    }

    void DestroyCollider(CellEntry entry)
    {
        if (entry?.Surface == null)
        {
            if (entry != null)
                entry.MeshData = null;
            return;
        }

        CollisionSurface?.RemoveSurfaceForCell(entry.CellId, entry.Surface);
        entry.Surface = null;
        entry.MeshData = null;
    }

    void TouchWarm(int cellId)
    {
        _warmOrder.Remove(cellId);
        _warmOrder.Add(cellId);
    }

    void TrimWarmCache()
    {
        _scratch.Clear();
        for (int i = 0; i < _warmOrder.Count; i++)
        {
            int id = _warmOrder[i];
            if (!_entries.TryGetValue(id, out CellEntry entry) || entry.Desired || entry.State != CellState.Cached)
                _scratch.Add(id);
        }

        for (int i = 0; i < _scratch.Count; i++)
            _warmOrder.Remove(_scratch[i]);

        while (_warmOrder.Count > WarmCacheCap)
        {
            int evictId = _warmOrder[0];
            _warmOrder.RemoveAt(0);
            if (_entries.TryGetValue(evictId, out CellEntry entry) && entry.State == CellState.Cached && !entry.Desired)
            {
                DestroyCollider(entry);
                _entries.Remove(evictId);
            }
        }
    }

    void ResortQueue()
    {
        if (_queue.Count <= 1)
            return;

        int refCell = _referenceCellId >= 0 ? _referenceCellId : FindReferenceCell();
        if (refCell < 0)
            return;

        _layout.GetCellCoords(refCell, out int rx, out int rz);
        _queue.Sort((a, b) =>
        {
            _layout.GetCellCoords(a, out int ax, out int az);
            _layout.GetCellCoords(b, out int bx, out int bz);
            int da = Chebyshev(ax, az, rx, rz);
            int db = Chebyshev(bx, bz, rx, rz);
            int cmp = da.CompareTo(db);
            return cmp != 0 ? cmp : a.CompareTo(b);
        });
    }

    int FindReferenceCell()
    {
        foreach (var kv in _entries)
        {
            if (kv.Value.Desired)
                return kv.Key;
        }

        return _queue.Count > 0 ? _queue[0] : -1;
    }

    static int Chebyshev(int ax, int az, int bx, int bz)
    {
        int dx = Math.Abs(ax - bx);
        int dz = Math.Abs(az - bz);
        return dx > dz ? dx : dz;
    }

    int SurfaceInstanceId(int cellId) => (_layout.PlayfieldId << 16) | (cellId & 0xFFFF);

    public void DrawGizmos(Color activeColor, Color cachedColor)
    {
        foreach (var kv in _entries)
        {
            CellEntry entry = kv.Value;
            if (entry.Surface == null)
                continue;

            if (entry.State == CellState.Ready)
            {
                if (activeColor.a <= 0f)
                    continue;
                Gizmos.color = activeColor;
            }
            else if (entry.State == CellState.Cached)
            {
                if (cachedColor.a <= 0f)
                    continue;
                Gizmos.color = cachedColor;
            }
            else
            {
                continue;
            }

            // The cell's world bounds. There is no Unity Mesh any more -- the geometry lives in a
            // TriangleMeshSurface inside CellSurface_t -- so the box is what is left to show, and it is
            // what the gizmo was for: which cells are loaded and which are warm-cached.
            entry.Surface.GetBounds(out LostEden.Vehicles.Vec3 min, out LostEden.Vehicles.Vec3 max);
            var lo = new Vector3(min.X, min.Y, min.Z);
            var hi = new Vector3(max.X, max.Y, max.Z);
            Gizmos.DrawWireCube((lo + hi) * 0.5f, hi - lo);
        }
    }
}