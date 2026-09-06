using UnityEditor;
using UnityEngine;

/// <summary>
/// Inspector for <see cref="GrassExclusionAuthoring"/>: a list of every volume stored for
/// the playfield, with per-volume edit, frame and delete.
///
/// MUST live in a folder named "Editor", or the build will fail on the UnityEditor
/// references.
///
/// The point is being able to work on one volume out of many. Load From Asset pulls in the
/// whole zone, which is unwieldy once there are dozens; Edit here brings a single volume
/// into the scene, selects it, and leaves the rest alone - and because Save merges by Id,
/// adjusting that one and saving updates only it.
/// </summary>
[CustomEditor(typeof(GrassExclusionAuthoring))]
public sealed class GrassExclusionAuthoringEditor : Editor
{
    string _search = string.Empty;
    Vector2 _scroll;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var authoring = (GrassExclusionAuthoring)target;

        EditorGUILayout.Space();

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Add Box"))
                authoring.AddBox();
            if (GUILayout.Button("Add Cylinder"))
                authoring.AddCylinder();
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Save To Asset"))
                authoring.SaveToAsset();
            if (GUILayout.Button("Load All From Asset"))
                authoring.LoadFromAsset();
        }

        if (GUILayout.Button("Replace Asset With Scene (destructive)") &&
            EditorUtility.DisplayDialog(
                "Replace stored volumes?",
                $"Everything stored for playfield {authoring.PlayfieldId} that is not currently " +
                "in the scene will be discarded.\n\nOnly do this if the whole set is loaded.",
                "Replace", "Cancel"))
        {
            authoring.ReplaceAssetWithScene();
        }

        EditorGUILayout.Space();
        DrawStoredVolumes(authoring);
    }

    void DrawStoredVolumes(GrassExclusionAuthoring authoring)
    {
        GrassExclusionVolumes asset = authoring.Asset;
        if (asset == null)
        {
            EditorGUILayout.HelpBox("Assign a GrassExclusionVolumes asset to list stored volumes.", MessageType.Info);
            return;
        }

        var volumes = asset.GetVolumes(authoring.PlayfieldId);
        int count = volumes?.Count ?? 0;

        EditorGUILayout.LabelField($"Stored volumes for playfield {authoring.PlayfieldId} ({count})", EditorStyles.boldLabel);

        if (count == 0)
        {
            EditorGUILayout.HelpBox("Nothing stored yet. Add a volume, position it, then Save To Asset.", MessageType.Info);
            return;
        }

        _search = EditorGUILayout.TextField("Filter", _search);

        _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MaxHeight(320f));

        for (int i = 0; i < volumes.Count; i++)
        {
            GrassExclusionVolumes.Volume volume = volumes[i];

            if (!Matches(volume, _search))
                continue;

            Transform inScene = authoring.FindChildById(volume.Id);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    // A dot rather than a word: the list gets long, and "is this one open
                    // for editing" is the only state that matters at a glance.
                    Color previous = GUI.color;
                    GUI.color = inScene != null ? Color.green : Color.gray;
                    GUILayout.Label("●", GUILayout.Width(14f));
                    GUI.color = previous;

                    EditorGUILayout.LabelField($"{volume.Shape}", GUILayout.Width(60f));

                    EditorGUI.BeginChangeCheck();
                    string label = EditorGUILayout.TextField(volume.Label ?? string.Empty);
                    if (EditorGUI.EndChangeCheck())
                    {
                        volume.Label = label;
                        volumes[i] = volume;

                        if (inScene != null)
                        {
                            var marker = inScene.GetComponent<GrassExclusionShape>();
                            if (marker != null)
                                marker.Label = label;
                        }

                        MarkDirty(asset);
                    }

                    EditorGUILayout.LabelField(volume.Id, GUILayout.Width(66f));
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        $"{volume.Center.x:F0}, {volume.Center.y:F0}, {volume.Center.z:F0}   " +
                        $"size {volume.Size.x:F1} x {volume.Size.y:F1} x {volume.Size.z:F1}",
                        EditorStyles.miniLabel);

                    if (GUILayout.Button(inScene != null ? "Select" : "Edit", GUILayout.Width(56f)))
                    {
                        GameObject go = inScene != null
                            ? inScene.gameObject
                            : authoring.CreateChild(volume);

                        Selection.activeGameObject = go;
                        Undo.RegisterCreatedObjectUndo(go, "Edit Grass Exclusion Volume");
                    }

                    if (GUILayout.Button("Frame", GUILayout.Width(52f)))
                        Frame(volume);

                    if (GUILayout.Button("X", GUILayout.Width(24f)))
                    {
                        if (EditorUtility.DisplayDialog(
                                "Delete volume?",
                                $"Remove {Describe(volume)} from playfield {authoring.PlayfieldId}?",
                                "Delete", "Cancel"))
                        {
                            asset.Remove(authoring.PlayfieldId, volume.Id);
                            MarkDirty(asset);

                            if (inScene != null)
                                Undo.DestroyObjectImmediate(inScene.gameObject);

                            GUIUtility.ExitGUI();
                        }
                    }
                }
            }
        }

        EditorGUILayout.EndScrollView();
    }

    static void Frame(GrassExclusionVolumes.Volume volume)
    {
        SceneView view = SceneView.lastActiveSceneView;
        if (view == null)
            return;

        // Frame the volume's own bounds rather than just moving the pivot, so a large
        // bridge volume and a small one both end up filling a sensible amount of the view.
        view.Frame(new Bounds(volume.Center, volume.Size), instant: false);
    }

    static bool Matches(GrassExclusionVolumes.Volume volume, string search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return true;

        return (volume.Label != null && volume.Label.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) >= 0)
            || (volume.Id != null && volume.Id.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) >= 0)
            || volume.Shape.ToString().IndexOf(search, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static string Describe(GrassExclusionVolumes.Volume volume)
        => string.IsNullOrEmpty(volume.Label) ? volume.Id : $"{volume.Label} ({volume.Id})";

    static void MarkDirty(GrassExclusionVolumes asset)
    {
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
    }
}