using System;
using System.Collections.Generic;
using System.Linq;
using AOSharp.Common.GameData;
using UnityEditor;
using UnityEngine;

enum DynelDebugTab
{
    Stats,
    Textures
}

public sealed class DynelDebugWindow : EditorWindow
{
    const float TexturePreviewSize = 96f;

    static readonly string[] TabLabels = { "Stats", "Textures" };

    Dynel _dynel;
    VisualDynel _visual;
    DynelDebugTab _tab;
    string _statSearch = string.Empty;
    UnityEngine.Vector2 _statsScroll;
    UnityEngine.Vector2 _texturesScroll;
    Action<Stat, int, int, bool> _statChangedHandler;

    [MenuItem("Lost Eden/Dynel Debug")]
    static void Open()
    {
        GetWindow<DynelDebugWindow>("Dynel Debug");
    }

    void OnEnable()
    {
        _statChangedHandler = OnStatChanged;
        Selection.selectionChanged += OnSelectionChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        ResolveSelection();
    }

    void OnDisable()
    {
        Selection.selectionChanged -= OnSelectionChanged;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        UnbindStatChanged();
    }

    void Update()
    {
        if (_tab == DynelDebugTab.Textures && EditorApplication.isPlaying && _visual != null)
            Repaint();
    }

    void OnSelectionChanged()
    {
        ResolveSelection();
        Repaint();
    }

    void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        ResolveSelection();
        Repaint();
    }

    void OnStatChanged(Stat stat, int previousFull, int newFull, bool isInitialSet)
    {
        Repaint();
    }

    void ResolveSelection()
    {
        UnbindStatChanged();
        _dynel = FindSelectedDynel();
        _visual = FindVisual(_dynel);
        BindStatChanged();
    }

    void BindStatChanged()
    {
        if (_dynel == null || !EditorApplication.isPlaying)
            return;

        _dynel.Stats.StatChanged += _statChangedHandler;
    }

    void UnbindStatChanged()
    {
        if (_dynel == null)
            return;

        _dynel.Stats.StatChanged -= _statChangedHandler;
    }

    static Dynel FindSelectedDynel()
    {
        GameObject active = Selection.activeGameObject;
        if (active == null)
            return null;

        if (active.TryGetComponent(out Dynel onSelf))
            return onSelf;

        Dynel inParents = active.GetComponentInParent<Dynel>();
        if (inParents != null)
            return inParents;

        return active.GetComponentInChildren<Dynel>(true);
    }

    static VisualDynel FindVisual(Dynel dynel)
    {
        if (dynel == null)
            return null;

        if (dynel is Character character && character.Visual != null)
            return character.Visual;

        if (dynel.TryGetComponent(out VisualDynel onSelf))
            return onSelf;

        return dynel.GetComponentInChildren<VisualDynel>(true);
    }

    void OnGUI()
    {
        DrawHeader();
        EditorGUILayout.Space(4f);

        _tab = (DynelDebugTab)GUILayout.Toolbar((int)_tab, TabLabels);
        EditorGUILayout.Space(4f);

        if (_dynel == null)
        {
            EditorGUILayout.HelpBox("Select a Dynel (or a child of one) in the Hierarchy or Scene.", MessageType.Info);
            return;
        }

        switch (_tab)
        {
            case DynelDebugTab.Stats:
                DrawStatsTab();
                break;
            case DynelDebugTab.Textures:
                DrawTexturesTab();
                break;
        }
    }

    void DrawHeader()
    {
        EditorGUILayout.LabelField("Target", EditorStyles.boldLabel);

        using (new EditorGUI.DisabledScope(true))
        {
            if (_dynel == null)
            {
                EditorGUILayout.TextField("Name", "(none)");
                EditorGUILayout.TextField("Identity", "(none)");
                EditorGUILayout.Toggle("IsNpc", false);
                return;
            }

            EditorGUILayout.ObjectField("GameObject", _dynel.gameObject, typeof(GameObject), true);
            EditorGUILayout.TextField("Name", string.IsNullOrEmpty(_dynel.Name) ? _dynel.gameObject.name : _dynel.Name);
            EditorGUILayout.TextField("Identity", $"{_dynel.Identity.Type} / {_dynel.Identity.Instance}");
            EditorGUILayout.Toggle("IsNpc", _dynel.IsNpc);
        }
    }

    void DrawStatsTab()
    {
        _statSearch = EditorGUILayout.TextField(_statSearch, EditorStyles.toolbarSearchField);

        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUILayout.Label("Stat", EditorStyles.miniBoldLabel, GUILayout.MinWidth(160f), GUILayout.ExpandWidth(true));
            GUILayout.Label("Base", EditorStyles.miniBoldLabel, GUILayout.Width(72f));
            GUILayout.Label("Bonus", EditorStyles.miniBoldLabel, GUILayout.Width(72f));
            GUILayout.Label("Full", EditorStyles.miniBoldLabel, GUILayout.Width(72f));
        }

        IEnumerable<(Stat Stat, int Base, int Bonus, int Full)> entries = _dynel.Stats.GetEntries();
        if (!string.IsNullOrWhiteSpace(_statSearch))
        {
            string query = _statSearch.Trim();
            entries = entries.Where(e =>
                e.Stat.ToString().IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                || ((int)e.Stat).ToString().IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        List<(Stat Stat, int Base, int Bonus, int Full)> sorted = entries
            .OrderBy(e => e.Stat.ToString(), StringComparer.OrdinalIgnoreCase)
            .ToList();

        _statsScroll = EditorGUILayout.BeginScrollView(_statsScroll);
        if (sorted.Count == 0)
        {
            EditorGUILayout.HelpBox(
                string.IsNullOrWhiteSpace(_statSearch)
                    ? "No stats set on this Dynel."
                    : "No stats match the search.",
                MessageType.None);
        }
        else
        {
            foreach ((Stat Stat, int Base, int Bonus, int Full) entry in sorted)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label($"{entry.Stat} ({(int)entry.Stat})", GUILayout.MinWidth(160f), GUILayout.ExpandWidth(true));
                    GUILayout.Label(entry.Base.ToString(), GUILayout.Width(72f));
                    GUILayout.Label(entry.Bonus.ToString(), GUILayout.Width(72f));
                    GUILayout.Label(entry.Full.ToString(), GUILayout.Width(72f));
                }
            }
        }

        EditorGUILayout.EndScrollView();
    }

    void DrawTexturesTab()
    {
        if (_visual == null)
        {
            EditorGUILayout.HelpBox("Selected Dynel has no VisualDynel component.", MessageType.Warning);
            return;
        }

        if (!EditorApplication.isPlaying)
        {
            EditorGUILayout.HelpBox("Enter Play Mode to inspect live skin/armor textures.", MessageType.Info);
            return;
        }

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.IntField("CatMesh", _visual.LoadedCatMeshId);
            EditorGUILayout.IntField("MonsterData", _visual.LoadedMonsterDataId);
            EditorGUILayout.Toggle("Robe", _visual.Robe);
        }

        EditorGUILayout.Space(4f);

        List<BodySlotTextureDebug> slots = _visual.GetBodySlotTextureDebug();
        _texturesScroll = EditorGUILayout.BeginScrollView(_texturesScroll);

        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUILayout.Label("Part", EditorStyles.miniBoldLabel, GUILayout.Width(56f));
            GUILayout.Label("Skin", EditorStyles.miniBoldLabel, GUILayout.Width(TexturePreviewSize + 8f));
            GUILayout.Label("Armor", EditorStyles.miniBoldLabel, GUILayout.Width(TexturePreviewSize + 8f));
            GUILayout.Label("Baked", EditorStyles.miniBoldLabel, GUILayout.Width(TexturePreviewSize + 8f));
            GUILayout.FlexibleSpace();
        }

        for (int i = 0; i < slots.Count; i++)
            DrawTextureSlotRow(slots[i]);

        EditorGUILayout.EndScrollView();
    }

    void DrawTextureSlotRow(BodySlotTextureDebug slot)
    {
        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField(slot.Part.ToString(), EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label(string.Empty, GUILayout.Width(56f));
            DrawTextureColumn(
                slot.Skin,
                slot.SkinId > 0 ? $"#{slot.SkinId}" : "(none)",
                slot.SkinName);
            DrawTextureColumn(
                slot.Armor,
                slot.ArmorId > 0 ? $"#{slot.ArmorId}" : "(none)",
                slot.ArmorId > 0 ? "AOTexture" : null);
            DrawTextureColumn(
                slot.Baked,
                slot.Baked != null ? slot.Baked.name : "(not applied)",
                null);
            GUILayout.FlexibleSpace();
        }

        using (new EditorGUI.DisabledScope(true))
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label(string.Empty, GUILayout.Width(56f));
            EditorGUILayout.ObjectField(slot.Skin, typeof(Texture2D), false, GUILayout.Width(TexturePreviewSize + 8f));
            EditorGUILayout.ObjectField(slot.Armor, typeof(Texture2D), false, GUILayout.Width(TexturePreviewSize + 8f));
            EditorGUILayout.ObjectField(slot.Baked, typeof(Texture2D), false, GUILayout.Width(TexturePreviewSize + 8f));
            GUILayout.FlexibleSpace();
        }
    }

    static void DrawTextureColumn(Texture2D texture, string idLabel, string detail)
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(TexturePreviewSize + 8f)))
        {
            UnityEngine.Rect previewRect = GUILayoutUtility.GetRect(
                TexturePreviewSize,
                TexturePreviewSize,
                GUILayout.Width(TexturePreviewSize),
                GUILayout.Height(TexturePreviewSize));

            EditorGUI.DrawRect(previewRect, new Color(0.12f, 0.12f, 0.12f, 1f));
            if (texture != null)
                GUI.DrawTexture(previewRect, texture, ScaleMode.ScaleToFit, true);
            else
                GUI.Label(previewRect, "—", EditorStyles.centeredGreyMiniLabel);

            GUILayout.Label(idLabel ?? string.Empty, EditorStyles.miniLabel);
            if (!string.IsNullOrEmpty(detail))
                GUILayout.Label(detail, EditorStyles.miniLabel);
        }
    }
}
