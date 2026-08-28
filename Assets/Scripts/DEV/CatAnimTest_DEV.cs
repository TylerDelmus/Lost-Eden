using System;
using System.Collections.Generic;
using AODB.Common.RDBObjects;
using AOSharp.Common.GameData;
using UnityEngine;
using Vector2 = UnityEngine.Vector2;
using Vector3 = UnityEngine.Vector3;
using Mathf = UnityEngine.Mathf;
using Quaternion = UnityEngine.Quaternion;
using Rect = UnityEngine.Rect;

public sealed class CatAnimTest_DEV : MonoBehaviour
{
    const string DefaultAoPath = @"C:\Program Files (x86)\Steam\steamapps\common\Anarchy Online";

    static readonly Breed[] Breeds = { Breed.Solitus, Breed.Opifex, Breed.Nanomage, Breed.Atrox };
    static readonly Gender[] Genders = { Gender.Male, Gender.Female };
    static readonly Fatness[] Fatnesses = { Fatness.Thin, Fatness.Normal, Fatness.Fat };
    static readonly (int Value, string Label)[] Races =
    {
        (0, "Caucasian"),
        (1, "African"),
        (2, "Asian"),
    };
    static readonly Dictionary<Breed, int> DefaultHeadMeshIds = new Dictionary<Breed, int>
    {
        { Breed.Solitus, 40681 },
        { Breed.Opifex, 40261 },
        { Breed.Nanomage, 40185 },
        { Breed.Atrox, 40111 },
    };

    [SerializeField] string _aoPath = DefaultAoPath;
    [SerializeField] int _catMeshId = 0;
    [SerializeField] int _monsterDataId = 0;
    [SerializeField] int _animSet = 0;
    [SerializeField] bool _filterByAnimSet;
    [SerializeField] float _blendSeconds = CatAnimPlayer.DefaultBlendSeconds;
    [SerializeField] float _blendWeight = 0.5f;
    [SerializeField] float _loopSmoothSeconds = CatAnimPlayer.DefaultLoopSmoothSeconds;

    Breed _breed = Breed.Solitus;
    Gender _gender = Gender.Male;
    Fatness _fatness = Fatness.Normal;
    int _race;
    bool _robe;
    bool _loadPanelExpanded = true;
    bool _scrubDragging;
    Vector2 _loadScroll;

    ResourceDatabase _database;
    CatMeshLoader _loader;
    SkinTextureResolver _skinTextures;
    AoImageTextureCache _imageTextures;
    AbiffLoader _abiffLoader;
    VisualDynel _visual;

    readonly List<AnimEntry> _anims = new();
    Vector2 _animScroll;
    string _nameFilter = string.Empty;
    string _status = "Open the AO database, then pick appearance or load by id.";
    int _selectedA = -1;
    int _selectedB = -1;

    Dictionary<int, string> _animNames;
    Dictionary<int, string> _catMeshNames;

    struct AnimEntry
    {
        public int AnimSet;
        public int AnimId;
        public string Name;
    }

    void Awake()
    {
        EnsureSceneBasics();
    }

    void OnDestroy()
    {
        if (_visual != null)
            Destroy(_visual.gameObject);

        _database?.Rdb?.Dispose();
    }

    void EnsureSceneBasics()
    {
        if (Camera.main == null)
        {
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            camGo.transform.position = new Vector3(0f, 1.6f, -3.5f);
            camGo.transform.rotation = Quaternion.Euler(10f, 0f, 0f);
        }

        if (FindAnyObjectByType<Light>() == null)
        {
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        if (GameObject.Find("Floor") == null)
        {
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.position = Vector3.zero;
        }
    }

    void OnGUI()
    {
        const float leftWidth = 420f;
        const float rightWidth = 360f;
        const float scrubHeight = 56f;
        float bottomPad = scrubHeight + 16f;

        GUILayout.BeginArea(new Rect(12f, 12f, leftWidth, Screen.height - 24f - bottomPad), GUI.skin.box);
        DrawMainPanel();
        GUILayout.EndArea();

        DrawLoadPanel(rightWidth, bottomPad);
        DrawScrubBar(scrubHeight);
    }

    void DrawMainPanel()
    {
        GUILayout.Label("CatMesh Animation Test");

        GUILayout.Label("AO Path");
        _aoPath = GUILayout.TextField(_aoPath ?? string.Empty);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Open DB", GUILayout.Width(100f)))
            OpenDatabase();
        GUILayout.Label(_database?.Rdb != null ? "DB: open" : "DB: closed");
        GUILayout.EndHorizontal();

        int loadedCat = _visual != null ? _visual.LoadedCatMeshId : 0;
        int loadedMd = _visual != null ? _visual.LoadedMonsterDataId : 0;
        GUILayout.Label($"Loaded CatMesh={loadedCat}  MonsterData={loadedMd}");
        GUILayout.Label(_status);

        GUILayout.Space(6f);
        GUILayout.Label("Name filter");
        _nameFilter = GUILayout.TextField(_nameFilter ?? string.Empty);

        _animScroll = GUILayout.BeginScrollView(_animScroll, GUILayout.Height(220f));
        for (int i = 0; i < _anims.Count; i++)
        {
            AnimEntry entry = _anims[i];
            if (!PassesFilter(entry.Name))
                continue;

            GUILayout.BeginHorizontal();
            bool isA = _selectedA == i;
            bool isB = _selectedB == i;
            if (GUILayout.Toggle(isA, "A", GUILayout.Width(28f)) != isA)
                _selectedA = i;
            if (GUILayout.Toggle(isB, "B", GUILayout.Width(28f)) != isB)
                _selectedB = i;

            string loopMark = HasLoopTiming(entry.AnimId) ? "*" : " ";
            if (GUILayout.Button($"{loopMark}{entry.AnimId}  [{entry.AnimSet}]  {entry.Name}", GUI.skin.label))
            {
                if (_selectedA < 0)
                    _selectedA = i;
                else
                    _selectedB = i;
            }

            GUILayout.EndHorizontal();
        }

        GUILayout.EndScrollView();

        GUILayout.Space(4f);
        GUILayout.Label($"A: {DescribeSelection(_selectedA)}");
        GUILayout.Label($"B: {DescribeSelection(_selectedB)}");

        GUILayout.BeginHorizontal();
        GUILayout.Label("Blend sec", GUILayout.Width(70f));
        _blendSeconds = GUILayout.HorizontalSlider(_blendSeconds, 0f, 1f);
        GUILayout.Label(_blendSeconds.ToString("0.00"), GUILayout.Width(40f));
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Blend A←→B", GUILayout.Width(70f));
        _blendWeight = GUILayout.HorizontalSlider(_blendWeight, 0f, 1f);
        GUILayout.Label(_blendWeight.ToString("0.00"), GUILayout.Width(40f));
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Loop smooth", GUILayout.Width(70f));
        float newLoopSmooth = GUILayout.HorizontalSlider(_loopSmoothSeconds, 0f, 0.5f);
        if (!Mathf.Approximately(newLoopSmooth, _loopSmoothSeconds))
        {
            _loopSmoothSeconds = newLoopSmooth;
            ApplyLoopSmoothToPlayer();
        }
        GUILayout.Label(_loopSmoothSeconds.ToString("0.00"), GUILayout.Width(40f));
        GUILayout.EndHorizontal();

        DrawLoopTimingInfo();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Play A"))
            PlaySelected(_selectedA, crossFade: false);
        if (GUILayout.Button("Play B"))
            PlaySelected(_selectedB, crossFade: false);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("CrossFade A→B"))
            CrossFadeSelected();
        if (GUILayout.Button("Blend A+B"))
            BlendSelected();
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Logical Idle"))
            PlayLogical("idle");
        if (GUILayout.Button("Logical Run"))
            PlayLogical("run");
        GUILayout.EndHorizontal();
    }

    void DrawLoadPanel(float width, float bottomPad)
    {
        float x = Screen.width - width - 12f;
        float y = 12f;
        float maxHeight = Screen.height - 24f - bottomPad;
        float height = _loadPanelExpanded ? Mathf.Min(520f, maxHeight) : 28f;

        GUILayout.BeginArea(new Rect(x, y, width, height), GUI.skin.box);
        GUILayout.BeginHorizontal();
        string arrow = _loadPanelExpanded ? "▼" : "▶";
        if (GUILayout.Button($"{arrow} CatMesh Load", GUILayout.ExpandWidth(true)))
            _loadPanelExpanded = !_loadPanelExpanded;
        GUILayout.EndHorizontal();

        if (_loadPanelExpanded)
        {
            _loadScroll = GUILayout.BeginScrollView(_loadScroll);

            DrawAppearanceRadios();

            GUILayout.Space(8f);
            GUILayout.Label("By Id");

            GUILayout.BeginHorizontal();
            GUILayout.Label("CatMesh Id", GUILayout.Width(90f));
            _catMeshId = IntField(_catMeshId);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("MonsterData", GUILayout.Width(90f));
            _monsterDataId = IntField(_monsterDataId);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("AnimSet", GUILayout.Width(90f));
            _animSet = IntField(_animSet);
            _filterByAnimSet = GUILayout.Toggle(_filterByAnimSet, "Filter list", GUILayout.Width(90f));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Load CatMesh"))
                LoadFromCatMesh();
            if (GUILayout.Button("Load MonsterData"))
                LoadFromMonsterData();
            GUILayout.EndHorizontal();

            int loadedCat = _visual != null ? _visual.LoadedCatMeshId : 0;
            int loadedMd = _visual != null ? _visual.LoadedMonsterDataId : 0;
            GUILayout.Label($"Loaded CatMesh={loadedCat}");
            GUILayout.Label($"MonsterData={loadedMd}");

            GUILayout.EndScrollView();
        }

        GUILayout.EndArea();
    }

    void ApplyLoopSmoothToPlayer()
    {
        if (TryGetPlayerQuiet(out CatAnimPlayer player))
            player.LoopSmoothSeconds = _loopSmoothSeconds;
    }

    void DrawScrubBar(float height)
    {
        float margin = 12f;
        float y = Screen.height - height - margin;
        float width = Screen.width - margin * 2f;
        GUILayout.BeginArea(new Rect(margin, y, width, height), GUI.skin.box);

        float duration = 0f;
        float time = 0f;
        bool hasPlayer = TryGetPlayerQuiet(out CatAnimPlayer player);
        if (hasPlayer)
        {
            duration = player.Duration;
            time = player.PlaybackTime;
        }

        GUILayout.BeginHorizontal();
        if (hasPlayer)
        {
            if (GUILayout.Button(player.Paused ? "Play" : "Pause", GUILayout.Width(60f)))
                player.Paused = !player.Paused;
        }
        else
        {
            GUILayout.Button("Play", GUILayout.Width(60f));
        }

        GUI.enabled = hasPlayer && duration > 0f;
        float newTime = GUILayout.HorizontalSlider(time, 0f, Mathf.Max(duration, 0.001f));
        GUI.enabled = true;

        if (hasPlayer && duration > 0f && !Mathf.Approximately(newTime, time))
        {
            if (!_scrubDragging)
            {
                _scrubDragging = true;
                player.Paused = true;
            }
            player.SetTime(newTime);
            time = newTime;
        }

        Event ev = Event.current;
        if (_scrubDragging && (ev.type == EventType.MouseUp || ev.rawType == EventType.MouseUp))
            _scrubDragging = false;

        GUILayout.Label($"{time:0.00}s / {duration:0.00}s", GUILayout.Width(110f));
        GUILayout.EndHorizontal();

        GUILayout.EndArea();
    }

    bool TryGetPlayerQuiet(out CatAnimPlayer player)
    {
        player = null;
        return _visual != null && _visual.TryGetAnimPlayer(out player);
    }

    void DrawAppearanceRadios()
    {
        GUILayout.Label("Appearance (UpdateAppearance)");

        GUILayout.BeginHorizontal();
        GUILayout.Label("Breed", GUILayout.Width(60f));
        for (int i = 0; i < Breeds.Length; i++)
        {
            Breed breed = Breeds[i];
            if (GUILayout.Toggle(_breed == breed, breed.ToString(), GUILayout.ExpandWidth(false)) && _breed != breed)
            {
                _breed = breed;
                ApplyAppearanceFromUi();
            }
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Gender", GUILayout.Width(60f));
        for (int i = 0; i < Genders.Length; i++)
        {
            Gender gender = Genders[i];
            if (GUILayout.Toggle(_gender == gender, gender.ToString(), GUILayout.ExpandWidth(false)) && _gender != gender)
            {
                _gender = gender;
                ApplyAppearanceFromUi();
            }
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Fatness", GUILayout.Width(60f));
        for (int i = 0; i < Fatnesses.Length; i++)
        {
            Fatness fatness = Fatnesses[i];
            if (GUILayout.Toggle(_fatness == fatness, fatness.ToString(), GUILayout.ExpandWidth(false)) && _fatness != fatness)
            {
                _fatness = fatness;
                ApplyAppearanceFromUi();
            }
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Race", GUILayout.Width(60f));
        for (int i = 0; i < Races.Length; i++)
        {
            (int value, string label) = Races[i];
            if (GUILayout.Toggle(_race == value, label, GUILayout.ExpandWidth(false)) && _race != value)
            {
                _race = value;
                ApplyAppearanceFromUi();
            }
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Robe", GUILayout.Width(60f));
        if (GUILayout.Toggle(_robe, "On", GUILayout.Width(40f)) && !_robe)
        {
            _robe = true;
            ApplyAppearanceFromUi();
        }
        if (GUILayout.Toggle(!_robe, "Off", GUILayout.Width(40f)) && _robe)
        {
            _robe = false;
            ApplyAppearanceFromUi();
        }
        GUILayout.EndHorizontal();

        if (GUILayout.Button("Load Appearance"))
            ApplyAppearanceFromUi();
    }

    void DrawLoopTimingInfo()
    {
        GUILayout.Space(4f);
        if (!TryGetEntry(_selectedA, out AnimEntry entry))
        {
            GUILayout.Label("Loop: (select anim A)");
            return;
        }

        if (!TryGetPlayerQuiet(out CatAnimPlayer player))
        {
            GUILayout.Label($"Loop: anim {entry.AnimId} (load a CatMesh to resolve)");
            return;
        }

        CatAnimRuntimeClip clip = player.EnsureClip(entry.AnimId);
        if (clip == null)
        {
            GUILayout.Label($"Loop: anim {entry.AnimId} (failed to load clip)");
            return;
        }

        if (clip.HasLoopTiming)
        {
            GUILayout.Label(
                $"Loop: {clip.LoopStart:0.00}s → {clip.LoopEnd:0.00}s  "
                + $"(playable {clip.Duration:0.00}s / source {clip.SourceDuration:0.00}s)");
        }
        else
        {
            GUILayout.Label($"Loop: none (full source {clip.SourceDuration:0.00}s)");
        }
    }

    void OpenDatabase()
    {
        try
        {
            _database ??= new ResourceDatabase();
            _database.Initialize(_aoPath);

            var abiffMaterials = new AbiffMaterialFactory(_database);
            var catMeshMaterials = new CatMeshMaterialFactory(abiffMaterials);
            _imageTextures = new AoImageTextureCache(_database);
            _skinTextures = new SkinTextureResolver(_database);
            _abiffLoader = new AbiffLoader(_database, abiffMaterials, _imageTextures);
            _loader = new CatMeshLoader(_database, catMeshMaterials);

            LoadNameCaches();
            EnsureVisual();
            _status = "Database opened.";
        }
        catch (Exception ex)
        {
            _status = $"Open DB failed: {ex.Message}";
            Debug.LogException(ex);
        }
    }

    void ApplyAppearanceFromUi()
    {
        if (!EnsureReady())
            return;

        EnsureVisual();
        StatCollection stats = _visual.Stats;
        // Appearance path (not MonsterData).
        stats.Set(Stat.MonsterData, 0);
        stats.Set(Stat.Breed, (int)_breed);
        stats.Set(Stat.Sex, (int)_gender);
        stats.Set(Stat.Fatness, (int)_fatness);
        stats.Set(Stat.Race, _race);
        stats.Set(Stat.AnimSet, _animSet);
        if (DefaultHeadMeshIds.TryGetValue(_breed, out int headMeshId))
            stats.Set(Stat.HeadMesh, headMeshId);
        _visual.Robe = _robe;

        _visual.UpdateAppearance(playIdle: false);

        _catMeshId = _visual.LoadedCatMeshId;
        _monsterDataId = _visual.LoadedMonsterDataId;
        RefreshAnimList(_visual.LoadedMonsterDataId);
        FrameSubject();
        ApplyLoopSmoothToPlayer();

        string meshLabel = _catMeshNames != null && _catMeshNames.TryGetValue(_catMeshId, out string meshName)
            ? $"{_catMeshId} ({meshName.Trim('\0')})"
            : _catMeshId.ToString();
        _status = _catMeshId > 0
            ? $"Appearance {_breed}/{_gender}/{_fatness}/race={_race}/robe={_robe} → CatMesh {meshLabel}"
            : $"No CatMesh for {_breed}/{_gender}/{_fatness}/robe={_robe}.";
    }

    void LoadFromCatMesh()
    {
        if (!EnsureReady())
            return;

        int catMeshId = _catMeshId;
        if (catMeshId <= 0)
        {
            _status = "Enter a CatMesh id.";
            return;
        }

        EnsureVisual();
        if (!_visual.ApplyCatMeshId(catMeshId, monsterDataId: 0, _animSet, playIdle: false))
        {
            _status = $"Failed to load CatMesh {catMeshId}.";
            return;
        }

        _catMeshId = _visual.LoadedCatMeshId;
        _monsterDataId = _visual.LoadedMonsterDataId;
        RefreshAnimList(_monsterDataId);
        FrameSubject();
        ApplyLoopSmoothToPlayer();
        string meshLabel = _catMeshNames != null && _catMeshNames.TryGetValue(_catMeshId, out string meshName)
            ? $"{_catMeshId} ({meshName.Trim('\0')})"
            : _catMeshId.ToString();
        _status = $"Loaded CatMesh {meshLabel}"
            + (_monsterDataId > 0 ? $" via MonsterData {_monsterDataId}." : " (no anim catalog).");
    }

    void LoadFromMonsterData()
    {
        if (!EnsureReady())
            return;

        if (_monsterDataId <= 0)
        {
            _status = "Enter a MonsterData id.";
            return;
        }

        EnsureVisual();
        _visual.Stats.Set(Stat.MonsterData, _monsterDataId);
        _visual.Stats.Set(Stat.AnimSet, _animSet);
        _visual.UpdateAppearance(playIdle: false);

        if (_visual.LoadedCatMeshId <= 0)
        {
            _status = $"MonsterData {_monsterDataId} failed to load.";
            return;
        }

        _catMeshId = _visual.LoadedCatMeshId;
        RefreshAnimList(_visual.LoadedMonsterDataId);
        FrameSubject();
        ApplyLoopSmoothToPlayer();
        _status = $"Loaded MonsterData {_monsterDataId} → CatMesh {_catMeshId}.";
    }

    void RefreshAnimList(int monsterDataId)
    {
        _anims.Clear();
        _selectedA = -1;
        _selectedB = -1;

        if (monsterDataId <= 0)
            return;

        int? filter = _filterByAnimSet ? _animSet : null;
        if (!MonsterDataResolver.TryGetAnimEntries(_database, monsterDataId, filter, out List<(int AnimSet, int AnimId)> entries))
            return;

        for (int i = 0; i < entries.Count; i++)
        {
            (int animSet, int animId) = entries[i];
            _anims.Add(new AnimEntry
            {
                AnimSet = animSet,
                AnimId = animId,
                Name = ResolveAnimName(animId)
            });
        }

        _anims.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
    }

    void PlaySelected(int index, bool crossFade)
    {
        if (!TryGetPlayer(out CatAnimPlayer player) || !TryGetEntry(index, out AnimEntry entry))
            return;

        bool ok = crossFade
            ? player.CrossFadeAnimId(entry.AnimId, _blendSeconds)
            : player.PlayAnimId(entry.AnimId, _blendSeconds);

        if (!ok)
        {
            _status = $"Failed to play {entry.AnimId}";
            return;
        }

        player.Paused = false;
        player.LoopSmoothSeconds = _loopSmoothSeconds;

        CatAnimRuntimeClip clip = player.EnsureClip(entry.AnimId);
        if (clip != null)
        {
            string loop = clip.HasLoopTiming
                ? $"loop={clip.LoopStart:0.00}/{clip.LoopEnd:0.00}"
                : "loop=none";
            _status = $"{(crossFade ? "CrossFade" : "Play")} {entry.AnimId} ({entry.Name}) "
                + $"tracks={clip.Tracks.Length} src={clip.SourceDuration:0.00}s "
                + $"{loop} playable={clip.Duration:0.00}s";
        }
        else
        {
            _status = $"{(crossFade ? "CrossFade" : "Play")} {entry.AnimId} ({entry.Name})";
        }
    }

    void CrossFadeSelected()
    {
        if (!TryGetPlayer(out CatAnimPlayer player)
            || !TryGetEntry(_selectedA, out AnimEntry a)
            || !TryGetEntry(_selectedB, out AnimEntry b))
        {
            _status = "Select A and B anims.";
            return;
        }

        player.PlayAnimId(a.AnimId, 0f);
        bool ok = player.CrossFadeAnimId(b.AnimId, _blendSeconds);
        if (ok)
            player.Paused = false;
        _status = ok ? $"CrossFade {a.Name} → {b.Name}" : "CrossFade failed.";
    }

    void BlendSelected()
    {
        if (!TryGetPlayer(out CatAnimPlayer player)
            || !TryGetEntry(_selectedA, out AnimEntry a)
            || !TryGetEntry(_selectedB, out AnimEntry b))
        {
            _status = "Select A and B anims.";
            return;
        }

        bool ok = player.BlendAnims(a.AnimId, b.AnimId, _blendWeight, _blendSeconds);
        if (ok)
            player.Paused = false;
        _status = ok
            ? $"Blend {a.Name} ({1f - _blendWeight:0.00}) + {b.Name} ({_blendWeight:0.00})"
            : "Blend failed.";
    }

    void PlayLogical(string logicalName)
    {
        if (!TryGetPlayer(out CatAnimPlayer player))
            return;

        int monsterDataId = _visual != null ? _visual.LoadedMonsterDataId : 0;
        player.SetAnimSet(_animSet);
        player.SetMonsterDataId(monsterDataId);
        bool ok = player.Play(logicalName, _blendSeconds);
        if (ok)
        {
            player.Paused = false;
            player.LoopSmoothSeconds = _loopSmoothSeconds;
        }
        _status = ok ? $"Play(\"{logicalName}\") AnimSet={_animSet}" : $"Play(\"{logicalName}\") failed.";
    }

    bool EnsureReady()
    {
        if (_database?.Rdb != null && _loader != null)
            return true;

        OpenDatabase();
        return _database?.Rdb != null && _loader != null;
    }

    void EnsureVisual()
    {
        if (_visual != null)
        {
            _visual.Configure(_loader, _database, _skinTextures, _imageTextures, _abiffLoader);
            return;
        }

        var go = new GameObject("VisualDynel");
        go.transform.position = Vector3.zero;
        _visual = go.AddComponent<VisualDynel>();
        _visual.Configure(_loader, _database, _skinTextures, _imageTextures, _abiffLoader);
    }

    void FrameSubject()
    {
        Camera cam = Camera.main;
        if (cam == null || _visual == null)
            return;

        Vector3 center = _visual.transform.position + Vector3.up * 1.0f;
        cam.transform.position = center + new Vector3(0f, 0.8f, -3.5f);
        cam.transform.LookAt(center);
    }

    bool TryGetPlayer(out CatAnimPlayer player)
    {
        player = null;
        if (_visual == null || !_visual.TryGetAnimPlayer(out player))
        {
            _status = "Load a CatMesh first.";
            return false;
        }

        return true;
    }

    bool TryGetEntry(int index, out AnimEntry entry)
    {
        entry = default;
        if (index < 0 || index >= _anims.Count)
            return false;

        entry = _anims[index];
        return true;
    }

    string DescribeSelection(int index)
    {
        if (!TryGetEntry(index, out AnimEntry entry))
            return "(none)";

        string loop = string.Empty;
        if (TryGetPlayerQuiet(out CatAnimPlayer player))
        {
            CatAnimRuntimeClip clip = player.EnsureClip(entry.AnimId);
            if (clip != null && clip.HasLoopTiming)
                loop = $" loop={clip.LoopStart:0.00}/{clip.LoopEnd:0.00}";
        }

        return $"{entry.AnimId} {entry.Name}{loop}";
    }

    bool HasLoopTiming(int animId)
    {
        if (_database?.Rdb == null || animId <= 0)
            return false;

        try
        {
            CATAnim anim = _database.Get<CATAnim>(ResourceTypeId.Anim, animId);
            return anim != null && anim.TryGetLoopTiming(out int start, out int end) && end > start;
        }
        catch
        {
            return false;
        }
    }

    bool PassesFilter(string name)
    {
        if (string.IsNullOrWhiteSpace(_nameFilter))
            return true;

        return name != null && name.IndexOf(_nameFilter.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
    }

    string ResolveAnimName(int animId)
    {
        if (_animNames != null && _animNames.TryGetValue(animId, out string named) && !string.IsNullOrEmpty(named))
            return named.Trim('\0');

        try
        {
            CATAnim anim = _database.Get<CATAnim>(ResourceTypeId.Anim, animId);
            if (!string.IsNullOrEmpty(anim?.Name))
                return anim.Name.Trim().Trim('\0');
        }
        catch
        {
            // fall through
        }

        return $"anim_{animId}";
    }

    void LoadNameCaches()
    {
        _animNames = new Dictionary<int, string>();
        _catMeshNames = new Dictionary<int, string>();
        try
        {
            InfoObject info = _database.Get<InfoObject>(1);
            if (info?.Types == null)
                return;

            if (info.Types.TryGetValue(ResourceTypeId.Anim, out Dictionary<int, string> anims) && anims != null)
            {
                foreach (KeyValuePair<int, string> pair in anims)
                    _animNames[pair.Key] = pair.Value;
            }

            if (info.Types.TryGetValue(ResourceTypeId.CatMesh, out Dictionary<int, string> meshes) && meshes != null)
            {
                foreach (KeyValuePair<int, string> pair in meshes)
                    _catMeshNames[pair.Key] = pair.Value;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"CatAnimTest_DEV: Failed to load InfoObject names ({ex.Message}).");
        }
    }

    static int IntField(int value)
    {
        string text = GUILayout.TextField(value.ToString());
        return int.TryParse(text, out int parsed) ? parsed : value;
    }
}
