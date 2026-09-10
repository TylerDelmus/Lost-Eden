using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// DEV scene host: open AO DB, load a playfield by id, fly-cam, find/frame sheared statels.
/// </summary>
public sealed class PlayfieldTest_DEV : MonoBehaviour
{
    const string DefaultAoPath = @"C:\Program Files (x86)\Steam\steamapps\common\Anarchy Online";

    [Header("Load")]
    [SerializeField] string _aoPath = DefaultAoPath;
    [SerializeField] int _playfieldId = 4310;
    [SerializeField] RenderConfig _renderConfig;
    [SerializeField] bool _loadOnStart = true;
    [SerializeField] bool _loadTerrain = true;
    [SerializeField] bool _loadWater = true;
    [SerializeField] bool _loadStatels = true;
    [SerializeField] bool _loadGrass = true;
    [SerializeField] bool _applyAoTweaks = true;
    [SerializeField] bool _attachLocality = true;
    [SerializeField] bool _attachStatelDebug = true;

    [Header("Camera")]
    [SerializeField] float _moveSpeed = 40f;
    [SerializeField] float _fastMultiplier = 4f;
    [SerializeField] float _lookSensitivity = 2.5f;
    [SerializeField] bool _lockCursorOnRightMouse = true;

    ResourceDatabase _database;
    Transform _playfieldRoot;
    HdrpEnvironmentApplicator _aoEnvironment;
    Coroutine _loadRoutine;
    string _status = "Set AO path / playfield id, then Load.";
    string _nameFilter = "brink";
    Vector2 _hitScroll;
    readonly List<StatelDebugInfo> _filterHits = new List<StatelDebugInfo>();
    float _yaw;
    float _pitch;
    bool _looking;

    void Awake()
    {
        EnsureSceneBasics();
        SyncCameraAngles();

        if (string.IsNullOrWhiteSpace(_aoPath))
        {
            string prefs = LoginPreferences.GetAoPath();
            if (AoInstallPath.IsValid(prefs))
                _aoPath = AoInstallPath.Normalize(prefs);
        }

        if (_renderConfig == null)
            _renderConfig = Resources.Load<RenderConfig>("RenderConfig");
    }

    void Start()
    {
        if (_loadOnStart)
            LoadPlayfield();
    }

    void OnDestroy()
    {
        StopLoad();
        ClearPlayfield();
        _database?.Rdb?.Dispose();
        _database = null;
    }

    void Update()
    {
        UpdateFlyCamera();

        if (_attachLocality && _playfieldRoot != null && Camera.main != null)
        {
            PlayfieldLocality locality = _playfieldRoot.GetComponent<PlayfieldLocality>();
            locality?.PrioritizeAround(Camera.main.transform.position);
        }
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(12f, 12f, 420f, Screen.height - 24f), GUI.skin.box);
        GUILayout.Label("Playfield Test");

        GUILayout.Label("AO Path");
        _aoPath = GUILayout.TextField(_aoPath ?? string.Empty);

        GUILayout.BeginHorizontal();
        GUILayout.Label("Playfield Id", GUILayout.Width(90f));
        string idText = GUILayout.TextField(_playfieldId.ToString());
        if (int.TryParse(idText, out int parsedId))
            _playfieldId = parsedId;
        GUILayout.EndHorizontal();

        _loadTerrain = GUILayout.Toggle(_loadTerrain, "Terrain");
        _loadWater = GUILayout.Toggle(_loadWater, "Water");
        _loadStatels = GUILayout.Toggle(_loadStatels, "Statels");
        _loadGrass = GUILayout.Toggle(_loadGrass, "Grass");
        _applyAoTweaks = GUILayout.Toggle(_applyAoTweaks, "AO environment tweaks");
        _attachLocality = GUILayout.Toggle(_attachLocality, "Cell locality");
        _attachStatelDebug = GUILayout.Toggle(_attachStatelDebug, "StatelDebugInfo");

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Load", GUILayout.Height(28f)))
            LoadPlayfield();
        if (GUILayout.Button("Unload", GUILayout.Height(28f)))
            UnloadPlayfield();
        GUILayout.EndHorizontal();

        GUILayout.Label(_database?.IsInitialized == true ? "DB: open" : "DB: closed");
        GUILayout.Label(_status ?? string.Empty);

        GUILayout.Space(8f);
        GUILayout.Label("Find statel (name contains)");
        _nameFilter = GUILayout.TextField(_nameFilter ?? string.Empty);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Search"))
            RefreshFilterHits();
        if (GUILayout.Button("Frame first"))
            FrameHit(0);
        if (GUILayout.Button("Log sheared"))
            LogAllSheared();
        GUILayout.EndHorizontal();

        GUILayout.Label($"Hits: {_filterHits.Count}");
        _hitScroll = GUILayout.BeginScrollView(_hitScroll, GUILayout.Height(220f));
        for (int i = 0; i < _filterHits.Count; i++)
        {
            StatelDebugInfo hit = _filterHits[i];
            if (hit == null)
                continue;

            string label = $"#{hit.PlacementIndex} {hit.MeshName} shear={hit.Sheared} ({hit.ShearFactor:F3})";
            if (GUILayout.Button(label, GUI.skin.label))
                FrameHit(i);
        }
        GUILayout.EndScrollView();

        GUILayout.Space(8f);
        GUILayout.Label("RMB look · WASD move · Shift fast · Q/E up/down");
        GUILayout.EndArea();
    }

    public void LoadPlayfield()
    {
        StopLoad();
        _loadRoutine = StartCoroutine(LoadRoutine());
    }

    public void UnloadPlayfield()
    {
        StopLoad();
        ClearPlayfield();
        _status = "Unloaded.";
    }

    void StopLoad()
    {
        if (_loadRoutine == null)
            return;

        StopCoroutine(_loadRoutine);
        _loadRoutine = null;
    }

    IEnumerator LoadRoutine()
    {
        _status = "Opening DB...";
        if (!TryOpenDatabase(out string error))
        {
            _status = error;
            _loadRoutine = null;
            yield break;
        }

        ClearPlayfield();
        StatelParser.AttachDebugInfo = _attachStatelDebug;

        var holder = new GameObject($"PlayfieldLoad_{_playfieldId}");
        holder.transform.SetParent(transform, false);
        _playfieldRoot = holder.transform;

        if (_loadTerrain)
        {
            _status = $"Loading terrain {_playfieldId}...";
            var terrain = new TerrainParser(_database, _renderConfig);
            yield return terrain.BuildCoroutine(_playfieldId, _playfieldRoot);
        }

        if (_loadWater)
        {
            _status = $"Loading water {_playfieldId}...";
            var water = new PlayfieldWaterBuilder(_database, _renderConfig);
            yield return water.BuildCoroutine(_playfieldId, _playfieldRoot);
        }

        if (_loadStatels)
        {
            _status = $"Loading statels {_playfieldId}...";
            var materials = new AbiffMaterialFactory(_database);
            var statels = new StatelParser(_database, _renderConfig, materials);
            yield return statels.BuildCoroutine(_playfieldId, _playfieldRoot);
        }

        if (_loadGrass)
        {
            _status = $"Loading grass {_playfieldId}...";
            var grass = new PlayfieldGrassBuilder(_database, _renderConfig);
            yield return grass.BuildCoroutine(_playfieldId, _playfieldRoot);
        }

        if (_applyAoTweaks)
            TryApplyAoEnvironment(_playfieldId);

        if (_attachLocality)
            AttachLocality(_playfieldId);

        FramePlayfieldBounds();
        RefreshFilterHits();

        _status = $"Ready: playfield {_playfieldId} (statel debug={_attachStatelDebug}).";
        Debug.Log($"[PlayfieldTest] {_status}");
        _loadRoutine = null;
    }

    bool TryOpenDatabase(out string error)
    {
        error = null;
        string path = AoInstallPath.Normalize(_aoPath);
        if (!AoInstallPath.IsValid(path))
        {
            error = $"Invalid AO path: '{_aoPath}'";
            return false;
        }

        try
        {
            _database ??= new ResourceDatabase();
            _database.Initialize(path);
            _aoPath = path;
            return true;
        }
        catch (Exception ex)
        {
            error = $"DB open failed: {ex.Message}";
            return false;
        }
    }

    void ClearPlayfield()
    {
        _aoEnvironment?.Clear();
        _aoEnvironment = null;
        _filterHits.Clear();
        PlayfieldTweakCatalog.ClearCache();
        SkippedStatelsCatalog.ClearCache();
        StatelLightsCatalog.ClearCache();

        if (_playfieldRoot != null)
        {
            Destroy(_playfieldRoot.gameObject);
            _playfieldRoot = null;
        }
    }

    void TryApplyAoEnvironment(int playfieldId)
    {
        if (_renderConfig == null || !_renderConfig.ApplyAoPlayfieldTweaks)
            return;

        try
        {
            _aoEnvironment?.Clear();
            var materials = new AbiffMaterialFactory(_database);
            var abiffLoader = new AbiffLoader(_database, materials);
            _aoEnvironment = new HdrpEnvironmentApplicator(_database, abiffLoader);
            _aoEnvironment.Apply(playfieldId, _renderConfig.ApplyAoSkyMeshes);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PlayfieldTest] AO tweaks failed: {ex.Message}");
            _aoEnvironment?.Clear();
            _aoEnvironment = null;
        }
    }

    void AttachLocality(int playfieldId)
    {
        if (_playfieldRoot == null)
            return;

        if (!PlayfieldLayoutFactory.TryCreate(_database, playfieldId, out IPlayfieldCellLayout layout))
        {
            Debug.LogWarning($"[PlayfieldTest] Cell locality not attached for {playfieldId}.");
            return;
        }

        var locality = _playfieldRoot.gameObject.AddComponent<PlayfieldLocality>();
        locality.Initialize(layout, _database, playerController: null);
        if (Camera.main != null)
            locality.PrioritizeAround(Camera.main.transform.position);
    }

    void RefreshFilterHits()
    {
        _filterHits.Clear();
        StatelDebugInfo[] all = FindObjectsByType<StatelDebugInfo>(FindObjectsSortMode.None);
        string filter = (_nameFilter ?? string.Empty).Trim();
        for (int i = 0; i < all.Length; i++)
        {
            StatelDebugInfo info = all[i];
            if (info == null)
                continue;

            if (filter.Length == 0
                || (!string.IsNullOrEmpty(info.MeshName)
                    && info.MeshName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                _filterHits.Add(info);
            }
        }

        _filterHits.Sort((a, b) => a.PlacementIndex.CompareTo(b.PlacementIndex));
    }

    void FrameHit(int index)
    {
        if (index < 0 || index >= _filterHits.Count)
            return;

        StatelDebugInfo hit = _filterHits[index];
        if (hit == null)
            return;

#if UNITY_EDITOR
        UnityEditor.Selection.activeGameObject = hit.gameObject;
#endif
        hit.LogShearBreakdown();

        Camera cam = Camera.main;
        if (cam == null)
            return;

        Bounds bounds = GetRendererBounds(hit.transform);
        Vector3 center = bounds.center;
        float radius = Mathf.Max(2f, bounds.extents.magnitude);
        cam.transform.position = center - cam.transform.forward * (radius * 2.5f);
        cam.transform.LookAt(center);
        SyncCameraAngles();
    }

    void LogAllSheared()
    {
        StatelDebugInfo[] all = FindObjectsByType<StatelDebugInfo>(FindObjectsSortMode.None);
        int count = 0;
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] == null || !all[i].Sheared)
                continue;
            all[i].LogShearBreakdown();
            count++;
        }

        Debug.Log($"[PlayfieldTest] Logged {count} sheared placement(s).");
    }

    void FramePlayfieldBounds()
    {
        Camera cam = Camera.main;
        if (cam == null || _playfieldRoot == null)
            return;

        Renderer[] renderers = _playfieldRoot.GetComponentsInChildren<Renderer>();
        if (renderers == null || renderers.Length == 0)
            return;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                bounds.Encapsulate(renderers[i].bounds);
        }

        float radius = Mathf.Max(10f, bounds.extents.magnitude);
        cam.transform.position = bounds.center + new Vector3(0f, radius * 0.35f, -radius * 0.75f);
        cam.transform.LookAt(bounds.center);
        SyncCameraAngles();
    }

    static Bounds GetRendererBounds(Transform root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers == null || renderers.Length == 0)
            return new Bounds(root.position, Vector3.one * 2f);

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                bounds.Encapsulate(renderers[i].bounds);
        }

        return bounds;
    }

    void UpdateFlyCamera()
    {
        Camera cam = Camera.main;
        if (cam == null)
            return;

        Mouse mouse = Mouse.current;
        Keyboard keyboard = Keyboard.current;

        if (_lockCursorOnRightMouse && mouse != null)
        {
            if (mouse.rightButton.wasPressedThisFrame)
            {
                _looking = true;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            else if (mouse.rightButton.wasReleasedThisFrame)
            {
                _looking = false;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        if (_looking && mouse != null)
        {
            Vector2 delta = mouse.delta.ReadValue();
            _yaw += delta.x * _lookSensitivity * 0.05f;
            _pitch -= delta.y * _lookSensitivity * 0.05f;
            _pitch = Mathf.Clamp(_pitch, -89f, 89f);
            cam.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }

        if (keyboard == null)
            return;

        Vector3 move = Vector3.zero;
        if (keyboard.wKey.isPressed) move += cam.transform.forward;
        if (keyboard.sKey.isPressed) move -= cam.transform.forward;
        if (keyboard.dKey.isPressed) move += cam.transform.right;
        if (keyboard.aKey.isPressed) move -= cam.transform.right;
        if (keyboard.eKey.isPressed) move += Vector3.up;
        if (keyboard.qKey.isPressed) move -= Vector3.up;

        if (move.sqrMagnitude > 0f)
        {
            bool fast = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
            float speed = _moveSpeed * (fast ? _fastMultiplier : 1f);
            cam.transform.position += move.normalized * (speed * Time.deltaTime);
        }
    }

    void SyncCameraAngles()
    {
        Camera cam = Camera.main;
        if (cam == null)
            return;

        Vector3 euler = cam.transform.rotation.eulerAngles;
        _yaw = euler.y;
        _pitch = euler.x > 180f ? euler.x - 360f : euler.x;
    }

    void EnsureSceneBasics()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            if (camGo.GetComponent<HDAdditionalCameraData>() == null)
                camGo.AddComponent<HDAdditionalCameraData>();
            cam.transform.position = new Vector3(0f, 20f, -40f);
            cam.transform.rotation = Quaternion.Euler(20f, 0f, 0f);
        }
        else if (cam.GetComponent<HDAdditionalCameraData>() == null)
        {
            cam.gameObject.AddComponent<HDAdditionalCameraData>();
        }

        Light light = FindAnyObjectByType<Light>();
        if (light == null)
        {
            var lightGo = new GameObject("Directional Light");
            light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            lightGo.AddComponent<HDAdditionalLightData>();
        }
        else if (light.GetComponent<HDAdditionalLightData>() == null)
        {
            light.gameObject.AddComponent<HDAdditionalLightData>();
        }
    }
}
