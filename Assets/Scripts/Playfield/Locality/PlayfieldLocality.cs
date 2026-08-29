using UnityEngine;

public sealed class PlayfieldLocality : MonoBehaviour
{
    static readonly Color ActiveSurfaceGizmoColor = new(0.15f, 0.95f, 0.55f, 0.9f);
    static readonly Color CachedSurfaceGizmoColor = new(0.55f, 0.55f, 0.55f, 0.35f);

    [SerializeField] bool _drawSurfaceGizmos = true;
    [SerializeField] bool _drawCachedSurfaces = true;

    PlayerController _playerController;
    IPlayfieldCellLayout _layout;
    CellLocalityMonitor _monitor;
    CellResourceHub _hub;
    SurfaceCellLoader _surfaceLoader;
    Transform _surfacesRoot;
    bool _indoorLogged;

    public void Initialize(
        IPlayfieldCellLayout layout,
        ResourceDatabase database,
        PlayerController playerController)
    {
        _layout = layout;
        _playerController = playerController;

        _surfacesRoot = new GameObject("Surfaces").transform;
        _surfacesRoot.SetParent(transform, false);
        if (GameLayers.Ground >= 0)
            _surfacesRoot.gameObject.layer = GameLayers.Ground;

        _monitor = new CellLocalityMonitor(layout);
        _hub = new CellResourceHub(_monitor);
        _surfaceLoader = new SurfaceCellLoader(database, layout, _surfacesRoot);
        _hub.AddLoader(_surfaceLoader);

        if (layout.IsIndoor && !_indoorLogged)
        {
            _indoorLogged = true;
            Debug.Log($"[PlayfieldLocality] Indoor playfield {layout.PlayfieldId}: surface streaming idle until rooms exist.");
        }
    }

    void Update()
    {
        if (_monitor == null || _hub == null)
            return;

        if (_playerController != null && _playerController.TryGetLocalPlayer(out Character localPlayer))
        {
            _monitor.Update(localPlayer.transform.position);
            _surfaceLoader?.SetReferenceCell(_monitor.CurrentCellId);
        }

        _hub.Tick();
    }

    void OnDrawGizmos()
    {
        if (!_drawSurfaceGizmos || _surfaceLoader == null)
            return;

        _surfaceLoader.DrawGizmos(
            ActiveSurfaceGizmoColor,
            _drawCachedSurfaces ? CachedSurfaceGizmoColor : Color.clear);
    }

    void OnDestroy()
    {
        _hub?.Dispose();
        _hub = null;
        _monitor = null;
        _surfaceLoader = null;
        _surfacesRoot = null;
    }
}
