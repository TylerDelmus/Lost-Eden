using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Browse AO Graphics.uvga entries in memory and copy names for UXML texture-name attributes.
/// </summary>
public sealed class UvgaBrowserWindow : EditorWindow
{
    const float PreviewSize = 128f;

    string _aoPath = string.Empty;
    string _filter = string.Empty;
    string _status = string.Empty;
    UnityEngine.Vector2 _listScroll;
    string _selectedName;
    Texture2D _preview;
    List<string> _filteredNames = new List<string>();

    [MenuItem("Lost Eden/UVGA Browser")]
    static void Open()
    {
        GetWindow<UvgaBrowserWindow>("UVGA Browser");
    }

    void OnEnable()
    {
        _aoPath = UvgaEditorPaths.ResolveAoPath();
        ReloadArchive();
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("Anarchy Online path", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        _aoPath = EditorGUILayout.TextField(_aoPath);
        if (GUILayout.Button("Browse…", GUILayout.Width(80f)))
            BrowseAoPath();
        if (GUILayout.Button("Reload", GUILayout.Width(70f)))
        {
            UvgaEditorPaths.SetAoPath(_aoPath);
            ReloadArchive();
        }
        EditorGUILayout.EndHorizontal();

        if (!string.IsNullOrEmpty(_status))
            EditorGUILayout.HelpBox(_status, MessageType.Info);

        EditorGUILayout.Space(6f);
        EditorGUI.BeginChangeCheck();
        _filter = EditorGUILayout.TextField("Filter", _filter);
        if (EditorGUI.EndChangeCheck())
            RebuildFilter();

        UvgaPathTextureCache cache = UvgaEditorBootstrap.Cache;
        int total = cache.IsLoaded ? cache.Names.Count : 0;
        EditorGUILayout.LabelField($"Entries: {_filteredNames.Count} / {total}");

        EditorGUILayout.BeginHorizontal();
        DrawNameList();
        DrawPreviewPane();
        EditorGUILayout.EndHorizontal();
    }

    void DrawNameList()
    {
        EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
        _listScroll = EditorGUILayout.BeginScrollView(_listScroll);

        for (int i = 0; i < _filteredNames.Count; i++)
        {
            string name = _filteredNames[i];
            bool selected = string.Equals(name, _selectedName, StringComparison.OrdinalIgnoreCase);
            if (GUILayout.Toggle(selected, name, "Button") && !selected)
                SelectName(name);
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    void DrawPreviewPane()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(PreviewSize + 24f));
        EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

        Rect previewRect = GUILayoutUtility.GetRect(PreviewSize, PreviewSize, GUILayout.ExpandWidth(false));
        EditorGUI.DrawRect(previewRect, new Color(0.15f, 0.15f, 0.15f));
        if (_preview != null)
            GUI.DrawTexture(previewRect, _preview, ScaleMode.ScaleToFit, true);

        EditorGUILayout.LabelField(_selectedName ?? "(none)", EditorStyles.wordWrappedMiniLabel);

        using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_selectedName)))
        {
            if (GUILayout.Button("Copy texture-name"))
                EditorGUIUtility.systemCopyBuffer = _selectedName;

            if (GUILayout.Button("Copy UXML snippet"))
            {
                EditorGUIUtility.systemCopyBuffer =
                    $"<UvgaBackground texture-name=\"{_selectedName}\" />";
            }
        }

        EditorGUILayout.EndVertical();
    }

    void BrowseAoPath()
    {
        string start = System.IO.Directory.Exists(_aoPath) ? _aoPath : string.Empty;
        string selected = EditorUtility.OpenFolderPanel("Select Anarchy Online Folder", start, string.Empty);
        if (string.IsNullOrEmpty(selected))
            return;

        _aoPath = selected;
        UvgaEditorPaths.SetAoPath(_aoPath);
        ReloadArchive();
    }

    void ReloadArchive()
    {
        _selectedName = null;
        _preview = null;
        _filteredNames.Clear();

        string path = AoInstallPath.Normalize(_aoPath);
        if (!AoInstallPath.IsValid(path))
        {
            _status = "Set a valid Anarchy Online install path (must contain cd_image/data/db).";
            UvgaEditorBootstrap.Cache.Clear();
            UvgaTextureSource.RaiseChanged();
            return;
        }

        UvgaEditorPaths.SetAoPath(path);
        _aoPath = path;
        UvgaEditorBootstrap.ReloadFromPrefs();

        if (!UvgaEditorBootstrap.Cache.IsLoaded)
        {
            _status = $"Failed to load Graphics.uvgi under '{path}'.";
            return;
        }

        _status = $"Loaded {UvgaEditorBootstrap.Cache.Names.Count} UVGA entries.";
        RebuildFilter();
    }

    void RebuildFilter()
    {
        IEnumerable<string> names = UvgaEditorBootstrap.Cache.Names ?? Array.Empty<string>();
        if (string.IsNullOrWhiteSpace(_filter))
            _filteredNames = names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        else
        {
            string filter = _filter.Trim();
            _filteredNames = names
                .Where(n => n.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (!string.IsNullOrEmpty(_selectedName)
            && _filteredNames.All(n => !string.Equals(n, _selectedName, StringComparison.OrdinalIgnoreCase)))
        {
            _selectedName = null;
            _preview = null;
        }
    }

    void SelectName(string name)
    {
        _selectedName = name;
        _preview = null;
        if (UvgaEditorBootstrap.Cache.TryGet(name, out Texture2D texture))
            _preview = texture;
        Repaint();
    }
}
