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
        public SurfaceCollisionBuilder.MeshData MeshData;
        public Mesh UnityMesh;
        public GameObject Root;
        public MeshCollider Collider;
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
    readonly List<int> _queue = new();
    readonly List<int> _warmOrder = new();
    readonly List<int> _scratch = new();
    readonly Queue<PreparedSurface> _prepared = new();

    int _generation;
    int _referenceCellId = -1;

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

        foreach (var kv in _entries)
            DestroyCollider(kv.Value);

        _entries.Clear();
    }

    void RequestDesired(int cellId)
    {
        if (!_entries.TryGetValue(cellId, out CellEntry entry))
        {
            entry = new CellEntry();
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
                if (entry.Root != null)
                    entry.Root.SetActive(true);
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
                if (entry.Root != null)
                    entry.Root.SetActive(false);
                TouchWarm(cellId);
                break;
            case CellState.Cached:
                TouchWarm(cellId);
                break;
        }
    }

    void PumpLoads()
    {
        int loaded = 0;
        while (loaded < MaxLoadsPerFrame && _queue.Count > 0)
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
                _entries.Remove(cellId);
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
        int applied = 0;
        while (applied < MaxAppliesPerFrame && _prepared.Count > 0)
        {
            PreparedSurface prepared = _prepared.Dequeue();
            if (!_entries.TryGetValue(prepared.CellId, out CellEntry entry)
                || entry.Generation != prepared.Generation
                || !entry.Desired)
                continue;

            if (!TryCreateCollider(prepared.CellId, prepared.MeshData, entry))
            {
                _entries.Remove(prepared.CellId);
                applied++;
                continue;
            }

            entry.MeshData = prepared.MeshData;
            entry.State = CellState.Ready;
            applied++;
        }
    }

    bool TryCreateCollider(int cellId, SurfaceCollisionBuilder.MeshData data, CellEntry entry)
    {
        if (data?.Vertices == null || data.Triangles == null || data.Vertices.Length == 0 || data.Triangles.Length < 3)
            return false;

        _layout.GetCellCoords(cellId, out int ix, out int iz);

        var mesh = new Mesh
        {
            name = $"Surface_{_layout.PlayfieldId}_{cellId}",
            indexFormat = data.Vertices.Length > 65535
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16
        };
        mesh.SetVertices(data.Vertices);
        mesh.SetTriangles(data.Triangles, 0, calculateBounds: true);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var root = new GameObject($"Surface_{ix}_{iz}_{cellId}");
        root.transform.SetParent(_parent, false);
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one;

        var collider = root.AddComponent<MeshCollider>();
        collider.sharedMesh = mesh;
        GameLayers.SetLayerRecursively(root, GameLayers.Ground);

        entry.UnityMesh = mesh;
        entry.Root = root;
        entry.Collider = collider;
        return true;
    }

    void DestroyCollider(CellEntry entry)
    {
        if (entry == null)
            return;

        if (entry.Collider != null)
            entry.Collider.sharedMesh = null;

        if (entry.Root != null)
            UnityEngine.Object.Destroy(entry.Root);

        if (entry.UnityMesh != null)
            UnityEngine.Object.Destroy(entry.UnityMesh);

        entry.Root = null;
        entry.Collider = null;
        entry.UnityMesh = null;
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
            if (entry.UnityMesh == null)
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

            Gizmos.DrawWireMesh(entry.UnityMesh, Vector3.zero, Quaternion.identity, Vector3.one);
        }
    }
}