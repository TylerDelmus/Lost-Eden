using System.Collections;
using System.Collections.Generic;
using AOSharp.Common.GameData;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.HighDefinition;
using Mathf = UnityEngine.Mathf;
using Quaternion = UnityEngine.Quaternion;
using Vector2 = UnityEngine.Vector2;
using Vector3 = UnityEngine.Vector3;
using Color = UnityEngine.Color;
using Rect = UnityEngine.Rect;

/// <summary>
/// DEV scene host: gfxtweak catalog + two Solitus-female CatMeshes for nano cast/hit tests.
/// </summary>
public sealed class GfxTest_DEV : MonoBehaviour
{
    const string DefaultAoPath = @"C:\Program Files (x86)\Steam\steamapps\common\Anarchy Online";
    const int SolitusFemaleHeadMeshId = 40681;
    const float NanoCastHoldSeconds = 2f;
    const float ActorSpacing = 1.6f;

    [Header("Load")]
    [SerializeField] string _aoPath;
    [SerializeField] bool _openDbOnStart = true;

    [Header("Spawn")]
    [SerializeField] int _effectId = 0x2cf2;
    [SerializeField] int _nanoId;
    [SerializeField] Color _tint = Color.white;
    [SerializeField] float _spawnDistance = 4f;
    [SerializeField] float _spawnHeight = 1.2f;

    [Header("Camera")]
    [SerializeField] float _moveSpeed = 8f;
    [SerializeField] float _fastMultiplier = 3f;
    [SerializeField] float _lookSensitivity = 2.5f;

    ResourceDatabase _database;
    AoImageTextureCache _imageTextures;
    EffectTextureNames _textureNames;
    GfxTweakCatalog _catalog;
    EffectHandler _handler;
    ItemTemplateCache _itemTemplates;
    CatMeshLoader _catMeshLoader;
    SkinTextureResolver _skinTextures;
    AbiffLoader _abiffLoader;

    VisualDynel _casterVisual;
    VisualDynel _targetVisual;
    GameObject _highlightProxy;
    Coroutine _nanoRoutine;

    readonly List<int> _allIds = new List<int>();
    readonly List<int> _filteredIds = new List<int>();
    readonly List<EffectHandle> _live = new List<EffectHandle>(16);

    string _status = "Open AO DB to load Setupf/gfxtweak.bin.";
    string _idFilter = string.Empty;
    Vector2 _listScroll;
    float _yaw;
    float _pitch;
    bool _looking;
    int _typeFilter; // 0=all, 1=billboard, 2=composite, 3=beam, 4=stars/particles, 5=other

    static readonly string[] TypeFilterLabels =
        { "All", "Billboard", "Composite", "Beam", "Stars/FX", "Other" };

    void Awake()
    {
        EnsureSceneBasics();
        SyncCameraAngles();
        _aoPath = ResolveAoPath(_aoPath);
    }

    static string ResolveAoPath(string current)
    {
        string prefs = LoginPreferences.GetAoPath();
        if (AoInstallPath.IsValid(prefs))
            return AoInstallPath.Normalize(prefs);

        if (AoInstallPath.IsValid(current))
            return AoInstallPath.Normalize(current);

        return AoInstallPath.IsValid(DefaultAoPath)
            ? AoInstallPath.Normalize(DefaultAoPath)
            : current ?? string.Empty;
    }

    void Start()
    {
        if (_openDbOnStart)
            OpenDatabase();
    }

    void OnDestroy()
    {
        if (_nanoRoutine != null)
            StopCoroutine(_nanoRoutine);
        ClearEffects();
        _database?.Rdb?.Dispose();
        _database = null;
        _handler = null;
    }

    void Update()
    {
        UpdateFlyCamera();
        PruneDeadHandles();
        _handler?.Tick(Time.deltaTime, Camera.main);
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(12f, 12f, 440f, Screen.height - 24f), GUI.skin.box);
        GUILayout.Label("GFX Test");

        GUILayout.Label("AO Path");
        _aoPath = GUILayout.TextField(_aoPath ?? string.Empty);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Open DB", GUILayout.Height(28f)))
            OpenDatabase();
        if (GUILayout.Button("Reload Catalog", GUILayout.Height(28f)))
            ReloadCatalog();
        GUILayout.EndHorizontal();

        GUILayout.Label(_database?.IsInitialized == true
            ? $"DB open — catalog {_catalog?.Count ?? 0}"
            : "DB closed");
        GUILayout.Label(_status ?? string.Empty);

        GUILayout.Space(6f);
        DrawSpawnControls();
        GUILayout.Space(6f);
        DrawCatalogList();
        GUILayout.Space(6f);
        GUILayout.Label("WASD/QE fly · RMB look · Space spawn · X clear");
        GUILayout.Label("Nano: cast anim + both hands (2s) → spell-dir + hit");
        GUILayout.EndArea();
    }

    void DrawSpawnControls()
    {
        GUILayout.Label("Spawn");
        GUILayout.BeginHorizontal();
        GUILayout.Label("Effect Id", GUILayout.Width(70f));
        string effectText = GUILayout.TextField(_effectId.ToString());
        if (int.TryParse(effectText, out int parsedEffect))
            _effectId = parsedEffect;
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Nano Id", GUILayout.Width(70f));
        string nanoText = GUILayout.TextField(_nanoId.ToString());
        if (int.TryParse(nanoText, out int parsedNano))
            _nanoId = parsedNano;
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Tint", GUILayout.Width(70f));
        _tint = DrawColorField(_tint);
        GUILayout.EndHorizontal();

        if (_catalog != null && _catalog.TryGet(_effectId, out GfxTweakRecord record))
        {
            GUILayout.Label(
                $"tag=0x{record.TypeTag:X} ({DescribeTag(record.TypeTag)}) life={record.Lifetime:F2} fields={record.Fields?.Length ?? 0}");
        }
        else
        {
            GUILayout.Label("Not in catalog (will use fallback flare params).");
        }

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Spawn at look", GUILayout.Height(28f)))
            SpawnAtLook();
        if (GUILayout.Button("Spawn origin", GUILayout.Height(28f)))
            SpawnAt(new Vector3(0f, _spawnHeight, 0f));
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Spawn from nano", GUILayout.Height(28f)))
            SpawnFromNano();
        if (GUILayout.Button($"Clear ({_live.Count})", GUILayout.Height(28f)))
            ClearEffects();
        GUILayout.EndHorizontal();
    }

    void DrawCatalogList()
    {
        GUILayout.Label("Catalog");
        GUILayout.BeginHorizontal();
        GUILayout.Label("Filter", GUILayout.Width(40f));
        string nextFilter = GUILayout.TextField(_idFilter ?? string.Empty);
        if (nextFilter != _idFilter)
        {
            _idFilter = nextFilter;
            RebuildFilter();
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        for (int i = 0; i < TypeFilterLabels.Length; i++)
        {
            bool on = _typeFilter == i;
            if (GUILayout.Toggle(on, TypeFilterLabels[i], GUI.skin.button) && !on)
            {
                _typeFilter = i;
                RebuildFilter();
            }
        }
        GUILayout.EndHorizontal();

        GUILayout.Label($"{_filteredIds.Count} / {_allIds.Count}");
        _listScroll = GUILayout.BeginScrollView(_listScroll, GUILayout.Height(280f));
        for (int i = 0; i < _filteredIds.Count; i++)
        {
            int id = _filteredIds[i];
            string label = id.ToString();
            if (_catalog != null && _catalog.TryGet(id, out GfxTweakRecord rec))
                label = $"{id}  0x{rec.TypeTag:X} {DescribeTag(rec.TypeTag)}";

            if (GUILayout.Button(label, _effectId == id ? GUI.skin.box : GUI.skin.button))
            {
                _effectId = id;
                SpawnAtLook();
            }
        }
        GUILayout.EndScrollView();
    }

    void OpenDatabase()
    {
        string path = AoInstallPath.Normalize(_aoPath);
        if (!AoInstallPath.IsValid(path))
        {
            _status = "Invalid AO path (need cd_image/data/db).";
            return;
        }

        _database?.Rdb?.Dispose();
        _database = new ResourceDatabase();
        try
        {
            _database.Initialize(path);
        }
        catch (System.Exception ex)
        {
            _status = $"DB open failed: {ex.Message}";
            _database = null;
            return;
        }

        LoginPreferences.SaveAoPath(path);
        _aoPath = path;
        _imageTextures = new AoImageTextureCache(_database);
        _textureNames = new EffectTextureNames(_database);
        _catalog = new GfxTweakCatalog(_database);
        _handler = new EffectHandler(_catalog, _imageTextures, _textureNames);
        _handler.SetLightParent(transform);
        _itemTemplates = new ItemTemplateCache(_database);

        var abiffMaterials = new AbiffMaterialFactory(_database);
        var catMeshMaterials = new CatMeshMaterialFactory(abiffMaterials);
        _skinTextures = new SkinTextureResolver(_database);
        _abiffLoader = new AbiffLoader(_database, abiffMaterials, _imageTextures);
        _catMeshLoader = new CatMeshLoader(_database, catMeshMaterials);

        EnsureNanoActors();

        _catalog.CopyIds(_allIds);
        RebuildFilter();
        _status = $"Loaded catalog ({_catalog.Count}). Solitus ♀ duo ready for nano tests.";
    }

    void ReloadCatalog()
    {
        if (_database?.Rdb == null)
        {
            OpenDatabase();
            return;
        }

        _catalog = new GfxTweakCatalog(_database);
        _handler = new EffectHandler(_catalog, _imageTextures, _textureNames);
        _handler.SetLightParent(transform);
        _catalog.CopyIds(_allIds);
        RebuildFilter();
        _status = $"Reloaded catalog ({_catalog.Count}).";
    }

    void SpawnAtLook()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            _status = "No camera.";
            return;
        }

        Vector3 pos = cam.transform.position
            + cam.transform.forward * _spawnDistance
            + Vector3.up * (_spawnHeight - 1.2f);
        SpawnAt(pos);
    }

    void SpawnAt(Vector3 position)
    {
        if (_handler == null)
        {
            _status = "Open DB first.";
            return;
        }

        bool isHighlight = _catalog != null
            && _catalog.TryGet(_effectId, out GfxTweakRecord preview)
            && EffectTypeTags.IsHighlight(preview.TypeCode);

        EffectLocator locator;
        if (isHighlight && TryGetActorRoot(_casterVisual, out GameObject casterRoot))
            locator = EffectLocator.OnHost(casterRoot);
        else if (isHighlight)
            locator = EffectLocator.OnHost(EnsureHighlightProxy(position));
        else
            locator = EffectLocator.WorldPoint(position, Quaternion.identity);

        EffectHandle handle = _handler.CreateEffect2(_effectId, locator, _tint);
        if (handle == null)
        {
            _status = $"CreateEffect2 failed for {_effectId}.";
            return;
        }

        _live.Add(handle);
        string tag = "?";
        string mat = "?";
        if (_catalog != null && _catalog.TryGet(_effectId, out GfxTweakRecord rec))
        {
            tag = $"0x{rec.TypeTag:X} {DescribeTag(rec.TypeTag)}";
            if (EffectTypeTags.IsHighlight(rec.TypeCode))
            {
                mat = $"variant={rec.FieldInt(1, 0)} dur={rec.Field(2, 0f):0.###}";
            }
            else
            {
                int matIdx = rec.Fields != null && rec.Fields.Length > 9
                    ? rec.FieldInt(9, -1)
                    : -1;
                if (matIdx == EffectMaterialTable.UntexturedIndex)
                {
                    mat = "untextured";
                }
                else if (matIdx >= 0 && matIdx < EffectMaterialTable.SlotCount)
                {
                    EffectMaterialTable.Slot slot = EffectMaterialTable.Get(matIdx);
                    mat = $"[{matIdx}] {slot.FileName} {slot.Cols}x{slot.Rows} f{slot.FirstFrame}-{slot.LastFrame}";
                }
                else
                {
                    mat = $"[{matIdx}]";
                }
            }
        }

        _status = isHighlight
            ? $"Highlight {_effectId} ({tag}) {mat} on proxy — live={_live.Count}"
            : $"Spawned {_effectId} ({tag}) mat={mat} @ {position} — live={_live.Count}";
    }

    GameObject EnsureHighlightProxy(Vector3 position)
    {
        if (_highlightProxy == null)
        {
            _highlightProxy = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            _highlightProxy.name = "GfxTest_HighlightProxy";
            Collider col = _highlightProxy.GetComponent<Collider>();
            if (col != null)
                Destroy(col);

            var renderer = _highlightProxy.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                Material lit = HdrpLitMaterialFactory.Create("GfxTestHighlightProxy");
                lit.SetColor("_BaseColor", new Color(0.35f, 0.35f, 0.4f, 1f));
                lit.EnableKeyword("_EMISSIVE_COLOR");
                if (lit.HasProperty("_EmissiveColor"))
                    lit.SetColor("_EmissiveColor", Color.black);
                if (lit.HasProperty("_EmissiveIntensity"))
                    lit.SetFloat("_EmissiveIntensity", 0f);
                HDMaterial.ValidateMaterial(lit);
                renderer.sharedMaterial = lit;
            }
        }

        _highlightProxy.transform.SetPositionAndRotation(position, Quaternion.identity);
        _highlightProxy.SetActive(true);
        return _highlightProxy;
    }

    void SpawnFromNano()
    {
        if (_handler == null || _itemTemplates == null)
        {
            _status = "Open DB first.";
            return;
        }

        if (_nanoId <= 0)
        {
            _status = "Set a nano id.";
            return;
        }

        if (!EnsureNanoActors())
        {
            _status = "Failed to build Solitus female CatMeshes.";
            return;
        }

        NanoSpell nano = _itemTemplates.GetNano(_nanoId);
        if (nano == null)
        {
            _status = $"Nano {_nanoId} failed to load.";
            return;
        }

        bool hasCast = NanoEffectResolver.TryResolveCast(nano, out NanoEffectResolver.SpellFx castFx);
        bool hasTrace = NanoEffectResolver.TryResolveTrace(nano, out NanoEffectResolver.SpellFx traceFx);
        bool hasImpact = NanoEffectResolver.TryResolveImpact(nano, out _);
        bool hasHit = NanoEffectResolver.TryResolveHit(nano, out NanoEffectResolver.SpellFx hitFx);

        if (!hasCast && !hasTrace && !hasImpact && !hasHit)
        {
            _status = $"Nano {_nanoId}: no Cast/Trace/Impact/Hit effect stats — not spawning.";
            Debug.LogWarning($"[GfxTest] Nano {_nanoId} missing cast/trace/impact/hit effect stats.");
            return;
        }

        if (!hasCast)
            Debug.LogWarning($"[GfxTest] Nano {_nanoId} missing CastEffectType.");
        if (!hasHit)
            Debug.LogWarning($"[GfxTest] Nano {_nanoId} missing HitEffectType.");

        if (_nanoRoutine != null)
            StopCoroutine(_nanoRoutine);
        _nanoRoutine = StartCoroutine(NanoCastThenHit(nano, castFx.EffectId, traceFx.EffectId, hitFx.EffectId));
    }

    IEnumerator NanoCastThenHit(NanoSpell nano, int castId, int traceId, int hitId)
    {
        const int casterKey = 1;
        const float tracerTravelSeconds = 0.35f;

        bool castAnimDone = false;
        if (_casterVisual != null)
        {
            if (!_casterVisual.PlayKindNameOnce("spell-sys", 0f, () => castAnimDone = true, overlay: true))
                castAnimDone = true;
        }
        else
        {
            castAnimDone = true;
        }

        EffectHandle castHandle = _handler.PlayNanoCastVisual(_casterVisual, _targetVisual, _nanoId, nano);
        if (castId != 0 && castHandle != null)
        {
            _effectId = castId;
            _live.Add(castHandle);
            _handler.RememberPendingCastVisual(casterKey, castHandle);
        }

        _status = $"Nano {_nanoId}: casting {castId}... (hit waits until cast finishes)";

        float waited = 0f;
        while (!castAnimDone && waited < NanoCastHoldSeconds + 8f)
        {
            waited += Time.deltaTime;
            yield return null;
        }

        if (!castAnimDone)
            yield return new WaitForSeconds(Mathf.Max(0f, NanoCastHoldSeconds - waited));

        PlayCasterKindName("spell-dir");


        // Cast finished: stop Spell1 + hand children before tracer/hit so they don't stack over them.


        _handler.EndPendingCastVisual(casterKey);

        float travel = _handler.BeginNanoTracerVisual(
            _casterVisual,
            _targetVisual,
            _nanoId,
            nano,
            casterKey,
            tracerTravelSeconds,
            out EffectHandle tracerHandle);
        if (tracerHandle != null)
            _live.Add(tracerHandle);

        if (travel > 0f)
        {
            _status = $"Nano {_nanoId}: cast done -> tracer {traceId} ({travel:0.##}s) -> hit";
            yield return new WaitForSeconds(travel);
        }

        EffectHandle hitHandle = _handler.PlayNanoHitVisual(
            _targetVisual, _nanoId, nano, casterKey, out EffectHandle impactHandle);
        if (impactHandle != null)
            _live.Add(impactHandle);
        if (hitHandle != null)
        {
            if (hitId != 0)
                _effectId = hitId;
            _live.Add(hitHandle);
            _status = $"Nano {_nanoId}: Cast={castId} -> tracer={traceId} -> Hit={hitId} on target (attach 0)";
        }
        else if (impactHandle != null)
        {
            _status = $"Nano {_nanoId}: Cast={castId} -> tracer={traceId} -> impact only on target";
        }
        else
        {
            _status = $"Nano {_nanoId}: cast finished (no impact/hit FX).";
        }

        _nanoRoutine = null;
    }

    void PlayCasterKindName(string kindName)
    {
        if (_casterVisual == null || string.IsNullOrEmpty(kindName))
            return;

        if (!_casterVisual.PlayKindNameOnce(kindName, 0f, null, overlay: true))
            Debug.LogWarning($"[GfxTest] Failed to play '{kindName}' on caster.");
    }

    bool SpawnCastOnBothHands(VisualDynel actor, int effectId)
    {
        if (_handler == null || actor == null || effectId <= 0)
            return false;

        // Stock Spell1: one control spans both hands (+ target chest/head).
        EffectHandle handle = _handler.CreateEffect2(effectId, actor, actor, _tint, attachOverride: 0);
        if (handle == null)
            return false;

        handle.SetDuration(60f);
        _live.Add(handle);
        return true;
    }

    bool SpawnOnActor(VisualDynel actor, int effectId)
    {
        if (_handler == null || actor == null || effectId <= 0)
            return false;
        if (!TryGetActorRoot(actor, out GameObject root))
            return false;

        EffectHandle handle = _handler.CreateEffect2(effectId, EffectLocator.OnHost(root), _tint);
        if (handle == null)
            return false;

        _live.Add(handle);
        return true;
    }

    bool EnsureNanoActors()
    {
        if (_database?.Rdb == null || _catMeshLoader == null)
            return false;

        _casterVisual = EnsureActor(
            _casterVisual,
            "GfxTest_Caster",
            new Vector3(-ActorSpacing, 0f, 0f),
            Quaternion.LookRotation(Vector3.right, Vector3.up));
        _targetVisual = EnsureActor(
            _targetVisual,
            "GfxTest_Target",
            new Vector3(ActorSpacing, 0f, 0f),
            Quaternion.LookRotation(Vector3.left, Vector3.up));

        FrameDuo();
        return _casterVisual != null && _targetVisual != null
            && _casterVisual.LoadedCatMeshId > 0
            && _targetVisual.LoadedCatMeshId > 0;
    }

    VisualDynel EnsureActor(VisualDynel existing, string name, Vector3 position, Quaternion rotation)
    {
        VisualDynel visual = existing;
        if (visual == null)
        {
            var go = new GameObject(name);
            visual = go.AddComponent<VisualDynel>();
        }

        visual.gameObject.name = name;
        visual.transform.SetPositionAndRotation(position, rotation);
        visual.Configure(_catMeshLoader, _database, _skinTextures, _imageTextures, _abiffLoader);

        StatCollection stats = visual.Stats;
        stats.Set(Stat.MonsterData, 0);
        stats.Set(Stat.Breed, (int)Breed.Solitus);
        stats.Set(Stat.Sex, (int)Gender.Female);
        stats.Set(Stat.Fatness, (int)Fatness.Normal);
        stats.Set(Stat.Race, 0);
        stats.Set(Stat.AnimSet, 0);
        stats.Set(Stat.HeadMesh, SolitusFemaleHeadMeshId);
        visual.Robe = false;
        visual.UpdateAppearance(playIdle: true);
        return visual;
    }

    static bool TryGetActorRoot(VisualDynel visual, out GameObject root)
    {
        root = null;
        if (visual == null)
            return false;
        root = visual.VisualRoot != null ? visual.VisualRoot : visual.gameObject;
        return root != null;
    }

    void FrameDuo()
    {
        Camera cam = Camera.main;
        if (cam == null || _casterVisual == null || _targetVisual == null)
            return;

        Vector3 mid = (_casterVisual.transform.position + _targetVisual.transform.position) * 0.5f
            + Vector3.up * 1.1f;
        cam.transform.position = mid + new Vector3(0f, 0.9f, -4.2f);
        cam.transform.LookAt(mid);
        SyncCameraAngles();
    }

    void ClearEffects()
    {
        if (_nanoRoutine != null)
        {
            StopCoroutine(_nanoRoutine);
            _nanoRoutine = null;
        }

        if (_casterVisual != null)
            _handler?.CancelPendingTrace(1);

        for (int i = 0; i < _live.Count; i++)
            _live[i]?.Destroy();
        _live.Clear();
        _status = "Cleared.";
    }

    void PruneDeadHandles()
    {
        for (int i = _live.Count - 1; i >= 0; i--)
        {
            if (_live[i] == null || !_live[i].IsAlive)
                _live.RemoveAt(i);
        }
    }

    void RebuildFilter()
    {
        _filteredIds.Clear();
        string filter = (_idFilter ?? string.Empty).Trim();
        bool hasFilter = filter.Length > 0;

        for (int i = 0; i < _allIds.Count; i++)
        {
            int id = _allIds[i];
            if (hasFilter && id.ToString().IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            if (_typeFilter != 0 && _catalog != null && _catalog.TryGet(id, out GfxTweakRecord rec))
            {
                int bucket = TagBucket(rec.TypeTag);
                if (bucket != _typeFilter)
                    continue;
            }

            _filteredIds.Add(id);
        }
    }

    static int TagBucket(int typeTag)
    {
        if (EffectTypeTags.IsBillboard(typeTag))
            return 1;
        if (EffectTypeTags.IsComposite(typeTag))
            return 2;
        if (EffectTypeTags.IsBeam(typeTag))
            return 3;
        if (EffectTypeTags.IsStars(typeTag) || EffectTypeTags.IsParticle(typeTag)
            || EffectTypeTags.IsHighlight(typeTag))
            return 4;
        return 5;
    }

    static string DescribeTag(int typeTag)
    {
        if (EffectTypeTags.IsBillboard(typeTag))
            return "billboard";
        if (typeTag == EffectTypeTags.Meta)
            return "meta";
        if (typeTag == EffectTypeTags.Sequencer)
            return "sequencer";
        if (typeTag == EffectTypeTags.Delay)
            return "delay";
        if (EffectTypeTags.IsStars(typeTag))
            return "stars";
        if (typeTag == EffectTypeTags.BParticle2)
            return "bparticle2";
        if (typeTag == EffectTypeTags.TParticle)
            return "tparticle";
        if (EffectTypeTags.IsBeam(typeTag))
            return "beam";
        if (typeTag == EffectTypeTags.Shield || typeTag == EffectTypeTags.Shield2)
            return "shield";
        if (EffectTypeTags.IsHighlight(typeTag))
            return "highlight";
        return $"other(0x{typeTag:X})";
    }

    static Color DrawColorField(Color color)
    {
        string hex = ColorUtility.ToHtmlStringRGBA(color);
        string next = GUILayout.TextField(hex);
        if (next != hex && ColorUtility.TryParseHtmlString("#" + next, out Color parsed))
            return parsed;
        return color;
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
            camGo.transform.position = new Vector3(0f, 1.6f, -4f);
            camGo.transform.rotation = Quaternion.Euler(10f, 0f, 0f);
        }

        if (cam.GetComponent<HDAdditionalCameraData>() == null)
            cam.gameObject.AddComponent<HDAdditionalCameraData>();

        Light light = FindAnyObjectByType<Light>();
        if (light == null)
        {
            var lightGo = new GameObject("Directional Light");
            light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        if (light.GetComponent<HDAdditionalLightData>() == null)
            light.gameObject.AddComponent<HDAdditionalLightData>();

        if (GameObject.Find("Floor") == null)
        {
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.position = Vector3.zero;
        }

        if (GameObject.Find("SpawnMarker") == null)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "SpawnMarker";
            marker.transform.position = new Vector3(0f, 1.2f, 0f);
            marker.transform.localScale = Vector3.one * 0.15f;
            var col = marker.GetComponent<Collider>();
            if (col != null)
                Destroy(col);
        }
    }

    void SyncCameraAngles()
    {
        Camera cam = Camera.main;
        if (cam == null)
            return;
        Vector3 euler = cam.transform.eulerAngles;
        _yaw = euler.y;
        _pitch = euler.x > 180f ? euler.x - 360f : euler.x;
    }

    void UpdateFlyCamera()
    {
        Camera cam = Camera.main;
        if (cam == null)
            return;

        Keyboard kb = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (kb == null)
            return;

        if (kb.spaceKey.wasPressedThisFrame)
            SpawnAtLook();
        if (kb.xKey.wasPressedThisFrame)
            ClearEffects();

        bool wantLook = mouse != null && mouse.rightButton.isPressed;
        if (wantLook != _looking)
        {
            _looking = wantLook;
            Cursor.lockState = _looking ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !_looking;
        }

        if (_looking && mouse != null)
        {
            Vector2 delta = mouse.delta.ReadValue();
            _yaw += delta.x * _lookSensitivity * 0.02f;
            _pitch -= delta.y * _lookSensitivity * 0.02f;
            _pitch = Mathf.Clamp(_pitch, -85f, 85f);
            cam.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }

        Vector3 move = Vector3.zero;
        if (kb.wKey.isPressed) move += cam.transform.forward;
        if (kb.sKey.isPressed) move -= cam.transform.forward;
        if (kb.dKey.isPressed) move += cam.transform.right;
        if (kb.aKey.isPressed) move -= cam.transform.right;
        if (kb.eKey.isPressed) move += Vector3.up;
        if (kb.qKey.isPressed) move -= Vector3.up;

        float speed = _moveSpeed;
        if (kb.leftShiftKey.isPressed)
            speed *= _fastMultiplier;
        cam.transform.position += move.normalized * (speed * Time.deltaTime);
    }
}
