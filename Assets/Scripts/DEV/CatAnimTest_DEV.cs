using System;
using System.Collections.Generic;
using AODB.Common.RDBObjects;
using UnityEngine;

public sealed class CatAnimTest_DEV : MonoBehaviour
{
    const string DefaultAoPath = @"C:\Program Files (x86)\Steam\steamapps\common\Anarchy Online";

    [SerializeField] string _aoPath = DefaultAoPath;
    [SerializeField] int _catMeshId = 0;
    [SerializeField] int _monsterDataId = 0;
    [SerializeField] int _animSet = 0;
    [SerializeField] bool _filterByAnimSet;
    [SerializeField] float _blendSeconds = CatAnimPlayer.DefaultBlendSeconds;
    [SerializeField] float _blendWeight = 0.5f;
    [SerializeField] float _trimStart;
    [SerializeField] float _trimEnd;

    ResourceDatabase _database;
    CatMeshLoader _loader;
    GameObject _subject;
    GameObject _visualRoot;

    readonly List<AnimEntry> _anims = new();
    Vector2 _animScroll;
    string _nameFilter = string.Empty;
    string _status = "Open the AO database, enter a CatMesh or MonsterData id, then Load.";
    int _selectedA = -1;
    int _selectedB = -1;
    int _loadedCatMeshId;
    int _loadedMonsterDataId;
    float _trimSourceDuration = 1f;
    int _trimAnimId;

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
        AnimCalibration.Reload();
    }

    void OnDestroy()
    {
        if (_subject != null)
            Destroy(_subject);

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
        const float width = 420f;
        GUILayout.BeginArea(new Rect(12f, 12f, width, Screen.height - 24f), GUI.skin.box);
        GUILayout.Label("CatMesh Animation Test");

        GUILayout.Label("AO Path");
        _aoPath = GUILayout.TextField(_aoPath ?? string.Empty);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Open DB", GUILayout.Width(100f)))
            OpenDatabase();
        GUILayout.Label(_database?.Rdb != null ? "DB: open" : "DB: closed");
        GUILayout.EndHorizontal();

        GUILayout.Space(6f);
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

        GUILayout.Label($"Loaded CatMesh={_loadedCatMeshId}  MonsterData={_loadedMonsterDataId}");
        GUILayout.Label(_status);

        GUILayout.Space(6f);
        GUILayout.Label("Name filter");
        _nameFilter = GUILayout.TextField(_nameFilter ?? string.Empty);

        _animScroll = GUILayout.BeginScrollView(_animScroll, GUILayout.Height(240f));
        for (int i = 0; i < _anims.Count; i++)
        {
            AnimEntry entry = _anims[i];
            if (!PassesFilter(entry.Name))
                continue;

            GUILayout.BeginHorizontal();
            bool isA = _selectedA == i;
            bool isB = _selectedB == i;
            if (GUILayout.Toggle(isA, "A", GUILayout.Width(28f)) != isA)
                SelectA(i);
            if (GUILayout.Toggle(isB, "B", GUILayout.Width(28f)) != isB)
                _selectedB = i;

            string calibMark = AnimCalibration.TryGet(entry.AnimId, out _) ? "*" : " ";
            if (GUILayout.Button($"{calibMark}{entry.AnimId}  [{entry.AnimSet}]  {entry.Name}", GUI.skin.label))
            {
                if (_selectedA < 0)
                    SelectA(i);
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

        DrawTrimControls();

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

        GUILayout.EndArea();
    }

    void DrawTrimControls()
    {
        float maxTrim = Mathf.Max(_trimSourceDuration - 0.001f, 0f);

        GUILayout.Space(4f);
        GUILayout.Label(
            _trimAnimId > 0
                ? $"Trim A (anim {_trimAnimId}, source {_trimSourceDuration:0.00}s)"
                : "Trim A (select an anim)");

        GUILayout.BeginHorizontal();
        GUILayout.Label("Start", GUILayout.Width(40f));
        float newStart = GUILayout.HorizontalSlider(_trimStart, 0f, maxTrim);
        newStart = FloatField(newStart);
        GUILayout.Label("s", GUILayout.Width(16f));
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("End", GUILayout.Width(40f));
        float newEnd = GUILayout.HorizontalSlider(_trimEnd, 0f, maxTrim);
        newEnd = FloatField(newEnd);
        GUILayout.Label("s", GUILayout.Width(16f));
        GUILayout.EndHorizontal();

        if (newStart + newEnd > maxTrim)
            newEnd = Mathf.Max(0f, maxTrim - newStart);

        _trimStart = newStart;
        _trimEnd = newEnd;

        float playable = Mathf.Max(_trimSourceDuration - _trimStart - _trimEnd, 0.001f);
        GUILayout.Label($"Playable {playable:0.00}s  (cut {_trimStart:0.00}s…{_trimEnd:0.00}s)");

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Preview Trim"))
            PreviewTrim();
        if (GUILayout.Button("Save Trim"))
            SaveTrim();
        if (GUILayout.Button("Clear Trim"))
            ClearTrim();
        GUILayout.EndHorizontal();
    }

    void OpenDatabase()
    {
        try
        {
            _database ??= new ResourceDatabase();
            _database.Initialize(_aoPath);
            _loader = new CatMeshLoader(_database, new CatMeshMaterialFactory(new AbiffMaterialFactory(_database)));
            AnimCalibration.Reload();
            LoadNameCaches();
            _status = "Database opened.";
        }
        catch (Exception ex)
        {
            _status = $"Open DB failed: {ex.Message}";
            Debug.LogException(ex);
        }
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

        // Always re-resolve MonsterData for the CatMesh so switching meshes refreshes the anim list.
        int monsterDataId = 0;
        if (!MonsterDataResolver.TryFindMonsterDataForCatMesh(_database, catMeshId, out monsterDataId))
        {
            _status = $"CatMesh {catMeshId}: no MonsterData found (set MonsterData manually, then Load MonsterData).";
            monsterDataId = 0;
            _monsterDataId = 0;
        }
        else
        {
            _monsterDataId = monsterDataId;
        }

        EnsureSubject();
        if (!_loader.ApplyCatMeshVisual(_subject.transform, catMeshId, monsterDataId, _animSet, ref _visualRoot, playIdle: false))
        {
            _status = $"Failed to load CatMesh {catMeshId}.";
            return;
        }

        _loadedCatMeshId = catMeshId;
        _loadedMonsterDataId = monsterDataId;
        RefreshAnimList(monsterDataId);
        FrameSubject();
        string meshLabel = _catMeshNames != null && _catMeshNames.TryGetValue(catMeshId, out string meshName)
            ? $"{catMeshId} ({meshName.Trim('\0')})"
            : catMeshId.ToString();
        _status = $"Loaded CatMesh {meshLabel}"
            + (monsterDataId > 0 ? $" via MonsterData {monsterDataId}." : " (no anim catalog).");
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

        if (!MonsterDataResolver.TryResolveBodyCatMeshId(_database, _monsterDataId, out int catMeshId))
        {
            _status = $"MonsterData {_monsterDataId} has no BodyCatMesh.";
            return;
        }

        _catMeshId = catMeshId;
        EnsureSubject();
        if (!_loader.ApplyCatMeshVisual(_subject.transform, catMeshId, _monsterDataId, _animSet, ref _visualRoot, playIdle: false))
        {
            _status = $"Failed to load MonsterData {_monsterDataId}.";
            return;
        }

        _loadedCatMeshId = catMeshId;
        _loadedMonsterDataId = _monsterDataId;
        RefreshAnimList(_monsterDataId);
        FrameSubject();
        _status = $"Loaded MonsterData {_monsterDataId} → CatMesh {catMeshId}.";
    }

    void RefreshAnimList(int monsterDataId)
    {
        _anims.Clear();
        _selectedA = -1;
        _selectedB = -1;
        _trimAnimId = 0;
        _trimStart = 0f;
        _trimEnd = 0f;
        _trimSourceDuration = 1f;

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

    void SelectA(int index)
    {
        _selectedA = index;
        LoadTrimForSelection();
    }

    void LoadTrimForSelection()
    {
        if (!TryGetEntry(_selectedA, out AnimEntry entry))
        {
            _trimAnimId = 0;
            _trimStart = 0f;
            _trimEnd = 0f;
            _trimSourceDuration = 1f;
            return;
        }

        _trimAnimId = entry.AnimId;
        AnimCalibration.Entry calib = AnimCalibration.GetOrDefault(entry.AnimId);
        _trimStart = calib.TrimStart;
        _trimEnd = calib.TrimEnd;

        if (TryGetPlayer(out CatAnimPlayer player))
        {
            CatAnimRuntimeClip clip = player.EnsureClip(entry.AnimId);
            _trimSourceDuration = clip != null ? clip.SourceDuration : 1f;
        }
        else
        {
            _trimSourceDuration = Mathf.Max(1f, _trimStart + _trimEnd + 0.001f);
        }
    }

    void PreviewTrim()
    {
        if (_trimAnimId <= 0 || !TryGetEntry(_selectedA, out AnimEntry entry))
        {
            _status = "Select anim A to preview trim.";
            return;
        }

        AnimCalibration.Set(entry.AnimId, _trimStart, _trimEnd);
        if (TryGetPlayer(out CatAnimPlayer player))
            player.InvalidateClipCache(entry.AnimId);

        PlaySelected(_selectedA, crossFade: false);
    }

    void SaveTrim()
    {
        if (_trimAnimId <= 0 || !TryGetEntry(_selectedA, out AnimEntry entry))
        {
            _status = "Select anim A to save trim.";
            return;
        }

        AnimCalibration.Set(entry.AnimId, _trimStart, _trimEnd);
        AnimCalibration.Save();
        if (TryGetPlayer(out CatAnimPlayer player))
            player.InvalidateClipCache(entry.AnimId);

        float playable = Mathf.Max(_trimSourceDuration - _trimStart - _trimEnd, 0.001f);
        _status = $"Saved trim for {entry.AnimId} ({entry.Name}): start={_trimStart:0.00}s end={_trimEnd:0.00}s playable={playable:0.00}s";
    }

    void ClearTrim()
    {
        if (_trimAnimId <= 0 || !TryGetEntry(_selectedA, out AnimEntry entry))
        {
            _status = "Select anim A to clear trim.";
            return;
        }

        _trimStart = 0f;
        _trimEnd = 0f;
        AnimCalibration.Set(entry.AnimId, 0f, 0f);
        AnimCalibration.Save();
        if (TryGetPlayer(out CatAnimPlayer player))
            player.InvalidateClipCache(entry.AnimId);

        _status = $"Cleared trim for {entry.AnimId} ({entry.Name}).";
    }

    void PlaySelected(int index, bool crossFade)
    {
        if (!TryGetPlayer(out CatAnimPlayer player) || !TryGetEntry(index, out AnimEntry entry))
            return;

        // Keep in-memory calibration in sync with UI when previewing A.
        if (index == _selectedA && entry.AnimId == _trimAnimId)
        {
            AnimCalibration.Set(entry.AnimId, _trimStart, _trimEnd);
            player.InvalidateClipCache(entry.AnimId);
        }

        bool ok = crossFade
            ? player.CrossFadeAnimId(entry.AnimId, _blendSeconds)
            : player.PlayAnimId(entry.AnimId, _blendSeconds);

        if (!ok)
        {
            _status = $"Failed to play {entry.AnimId}";
            return;
        }

        CatAnimRuntimeClip clip = player.EnsureClip(entry.AnimId);
        if (clip != null)
        {
            _status = $"{(crossFade ? "CrossFade" : "Play")} {entry.AnimId} ({entry.Name}) "
                + $"tracks={clip.Tracks.Length} src={clip.SourceDuration:0.00}s "
                + $"trim={clip.TrimStart:0.00}/{clip.TrimEnd:0.00} playable={clip.Duration:0.00}s";
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

        AnimCalibration.Set(a.AnimId, _trimStart, _trimEnd);
        player.InvalidateClipCache(a.AnimId);

        player.PlayAnimId(a.AnimId, 0f);
        bool ok = player.CrossFadeAnimId(b.AnimId, _blendSeconds);
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

        AnimCalibration.Set(a.AnimId, _trimStart, _trimEnd);
        player.InvalidateClipCache(a.AnimId);

        bool ok = player.BlendAnims(a.AnimId, b.AnimId, _blendWeight, _blendSeconds);
        _status = ok
            ? $"Blend {a.Name} ({1f - _blendWeight:0.00}) + {b.Name} ({_blendWeight:0.00})"
            : "Blend failed.";
    }

    void PlayLogical(string logicalName)
    {
        if (!TryGetPlayer(out CatAnimPlayer player))
            return;

        player.SetAnimSet(_animSet);
        player.SetMonsterDataId(_loadedMonsterDataId);
        bool ok = player.Play(logicalName, _blendSeconds);
        _status = ok ? $"Play(\"{logicalName}\") AnimSet={_animSet}" : $"Play(\"{logicalName}\") failed.";
    }

    bool EnsureReady()
    {
        if (_database?.Rdb != null && _loader != null)
            return true;

        OpenDatabase();
        return _database?.Rdb != null && _loader != null;
    }

    void EnsureSubject()
    {
        if (_subject != null)
            return;

        _subject = new GameObject("CatAnimSubject");
        _subject.transform.position = Vector3.zero;
    }

    void FrameSubject()
    {
        Camera cam = Camera.main;
        if (cam == null || _subject == null)
            return;

        Vector3 center = _subject.transform.position + Vector3.up * 1.0f;
        cam.transform.position = center + new Vector3(0f, 0.8f, -3.5f);
        cam.transform.LookAt(center);
    }

    bool TryGetPlayer(out CatAnimPlayer player)
    {
        player = null;
        if (_visualRoot == null)
        {
            _status = "Load a CatMesh first.";
            return false;
        }

        if (_visualRoot.TryGetComponent(out CatMeshVisualHolder holder) && holder.Player != null)
        {
            player = holder.Player;
            return true;
        }

        return _visualRoot.TryGetComponent(out player);
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

        AnimCalibration.Entry calib = AnimCalibration.GetOrDefault(entry.AnimId);
        string trim = calib.HasTrim ? $" trim={calib.TrimStart:0.00}/{calib.TrimEnd:0.00}" : string.Empty;
        return $"{entry.AnimId} {entry.Name}{trim}";
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

    static float FloatField(float value)
    {
        string text = GUILayout.TextField(value.ToString("0.###"));
        return float.TryParse(text, out float parsed) ? parsed : value;
    }
}
