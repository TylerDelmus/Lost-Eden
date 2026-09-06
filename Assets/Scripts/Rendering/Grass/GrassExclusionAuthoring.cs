using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Scene-side editor for <see cref="GrassExclusionVolumes"/>.
///
/// Each child GameObject is one volume: its position is the centre, its rotation the
/// orientation, its lossy scale the size. Using child transforms rather than inspector
/// number fields means you get Unity's normal move/rotate/scale handles and can drag a
/// volume over a bridge until it looks right, which is the only practical way to author
/// these.
///
/// Playfields load from the RDB at runtime, so authoring happens in play mode: load the
/// zone, add and position volumes, then Save To Asset. AssetDatabase writes work in play
/// mode, so the asset survives leaving it - but the scene objects do NOT. That is why Save
/// merges by volume Id rather than replacing: otherwise each session, starting from an
/// empty scene, would wipe every volume authored in the last one.
///
/// Removing a volume is therefore deliberate, not implied by its absence from the scene -
/// use Remove From Asset on the child, or Replace Asset With Scene for a full destructive
/// sync.
/// </summary>
[ExecuteAlways]
public sealed class GrassExclusionAuthoring : MonoBehaviour
{
    [SerializeField] GrassExclusionVolumes _asset;

    [Tooltip("Which playfield the volumes below belong to. Must match the zone id you load.")]
    [SerializeField] int _playfieldId;

    [SerializeField] Vector3 _newVolumeSize = new Vector3(8f, 4f, 8f);

    [Header("Gizmos")]
    [Tooltip("Also draw every volume already stored in the asset for this playfield, " +
             "including ones not present in the scene. Useful for finding one to remove.")]
    [SerializeField] bool _showStoredVolumes = true;

    [SerializeField] bool _showLabels = true;

    [SerializeField] Color _fillColour = new Color(1f, 0.3f, 0.2f, 0.18f);
    [SerializeField] Color _wireColour = new Color(1f, 0.4f, 0.25f, 0.9f);

    [Tooltip("Colour for stored volumes that are not currently in the scene.")]
    [SerializeField] Color _storedColour = new Color(0.3f, 0.7f, 1f, 0.8f);

    public GrassExclusionVolumes Asset => _asset;
    public int PlayfieldId => _playfieldId;

    [ContextMenu("Add Box")]
    public void AddBox() => AddVolume(GrassExclusionVolumes.Shape.Box);

    [ContextMenu("Add Cylinder")]
    public void AddCylinder() => AddVolume(GrassExclusionVolumes.Shape.Cylinder);

    void AddVolume(GrassExclusionVolumes.Shape shape)
    {
        string id = GrassExclusionVolumes.NewId();

        var go = new GameObject($"{shape}_{id}");
        go.transform.SetParent(transform, worldPositionStays: false);
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = _newVolumeSize;

        var marker = go.AddComponent<GrassExclusionShape>();
        marker.Shape = shape;
        marker.Id = id;

        // Never drop a new volume exactly on the previous one - stacked identical volumes
        // look like a single one that replaced the old, which is confusing. Prefer wherever
        // the scene view is looking; otherwise step sideways from the last child.
        Vector3 position = transform.position;

#if UNITY_EDITOR
        UnityEditor.SceneView sceneView = UnityEditor.SceneView.lastActiveSceneView;
        if (sceneView != null)
            position = sceneView.pivot;
        else if (transform.childCount > 1)
            position = transform.GetChild(transform.childCount - 2).position + Vector3.right * _newVolumeSize.x;
#else
        if (transform.childCount > 1)
            position = transform.GetChild(transform.childCount - 2).position + Vector3.right * _newVolumeSize.x;
#endif

        go.transform.position = position;

#if UNITY_EDITOR
        UnityEditor.Selection.activeGameObject = go;
        UnityEditor.Undo.RegisterCreatedObjectUndo(go, "Add Grass Exclusion Volume");
#endif
    }

    /// <summary>
    /// Merges the scene's volumes into the asset by Id. Anything already stored that is not
    /// in the scene is left untouched.
    /// </summary>
    [ContextMenu("Save To Asset")]
    public void SaveToAsset()
    {
        if (_asset == null)
        {
            Debug.LogError("GrassExclusionAuthoring: no GrassExclusionVolumes asset assigned.");
            return;
        }

        List<GrassExclusionVolumes.Volume> scene = CollectFromScene(out int roots);

        int added = 0;
        for (int i = 0; i < scene.Count; i++)
        {
            if (_asset.Upsert(_playfieldId, scene[i]))
                added++;
        }

        MarkAssetDirty();

        int total = _asset.GetVolumes(_playfieldId)?.Count ?? 0;
        Debug.Log(
            $"GrassExclusionAuthoring: playfield {_playfieldId} - {added} added, " +
            $"{scene.Count - added} updated, {total} stored in total " +
            $"(from {roots} authoring object(s)).");
    }

    /// <summary>
    /// Destructive counterpart to Save: the asset ends up containing exactly what is in the
    /// scene. Only safe when every volume for this playfield is currently loaded.
    /// </summary>
    [ContextMenu("Replace Asset With Scene (destructive)")]
    public void ReplaceAssetWithScene()
    {
        if (_asset == null)
        {
            Debug.LogError("GrassExclusionAuthoring: no GrassExclusionVolumes asset assigned.");
            return;
        }

        List<GrassExclusionVolumes.Volume> scene = CollectFromScene(out int roots);
        int previous = _asset.GetVolumes(_playfieldId)?.Count ?? 0;

        _asset.ReplaceVolumes(_playfieldId, scene);
        MarkAssetDirty();

        Debug.LogWarning(
            $"GrassExclusionAuthoring: playfield {_playfieldId} replaced - was {previous} volume(s), " +
            $"now {scene.Count} (from {roots} authoring object(s)).");
    }

    [ContextMenu("Load From Asset")]
    public void LoadFromAsset()
    {
        if (_asset == null)
        {
            Debug.LogError("GrassExclusionAuthoring: no GrassExclusionVolumes asset assigned.");
            return;
        }

        List<GrassExclusionVolumes.Volume> volumes = _asset.GetVolumes(_playfieldId);

        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            GameObject child = transform.GetChild(i).gameObject;
            if (Application.isPlaying)
                Destroy(child);
            else
                DestroyImmediate(child);
        }

        if (volumes == null || volumes.Count == 0)
        {
            Debug.Log($"GrassExclusionAuthoring: no volumes stored for playfield {_playfieldId}.");
            return;
        }

        for (int i = 0; i < volumes.Count; i++)
            CreateChild(volumes[i]);

        Debug.Log($"GrassExclusionAuthoring: loaded {volumes.Count} volume(s) for playfield {_playfieldId}.");
    }

    /// <summary>
    /// Brings one stored volume into the scene as an editable child. Used by Load From
    /// Asset and by the inspector's per-volume Edit button, so a single volume can be
    /// pulled in and adjusted without loading a zone's entire set.
    /// </summary>
    public GameObject CreateChild(GrassExclusionVolumes.Volume volume)
    {
        string id = string.IsNullOrEmpty(volume.Id) ? GrassExclusionVolumes.NewId() : volume.Id;

        var go = new GameObject($"{volume.Shape}_{id}");
        go.transform.SetParent(transform, worldPositionStays: false);
        go.transform.SetPositionAndRotation(volume.Center, Quaternion.Euler(volume.EulerAngles));
        go.transform.localScale = volume.Size;

        var marker = go.AddComponent<GrassExclusionShape>();
        marker.Shape = volume.Shape;
        marker.Id = id;
        marker.Label = volume.Label;

        return go;
    }

    /// <summary>The scene child representing this stored volume, or null if it is not loaded.</summary>
    public Transform FindChildById(string id)
    {
        if (string.IsNullOrEmpty(id))
            return null;

        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            var marker = child.GetComponent<GrassExclusionShape>();
            if (marker != null && marker.Id == id)
                return child;
        }

        return null;
    }

    List<GrassExclusionVolumes.Volume> CollectFromScene(out int roots)
    {
        // Gather from EVERY authoring object targeting this playfield and asset, so it does
        // not matter whether volumes live under one object or several.
        var volumes = new List<GrassExclusionVolumes.Volume>();
        roots = 0;

        GrassExclusionAuthoring[] authoring =
            FindObjectsByType<GrassExclusionAuthoring>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        for (int a = 0; a < authoring.Length; a++)
        {
            GrassExclusionAuthoring source = authoring[a];
            if (source == null || source._playfieldId != _playfieldId || source._asset != _asset)
                continue;

            roots++;
            source.CollectInto(volumes);
        }

        return volumes;
    }

    void CollectInto(List<GrassExclusionVolumes.Volume> volumes)
    {
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (!child.gameObject.activeSelf)
                continue;

            var marker = child.GetComponent<GrassExclusionShape>();

            volumes.Add(new GrassExclusionVolumes.Volume
            {
                Id = marker != null ? marker.Id : GrassExclusionVolumes.NewId(),
                Label = marker != null ? marker.Label : null,
                Shape = marker != null ? marker.Shape : GrassExclusionVolumes.Shape.Box,
                Center = child.position,
                Size = AbsScale(child.lossyScale),
                EulerAngles = child.rotation.eulerAngles
            });
        }
    }

    void MarkAssetDirty()
    {
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(_asset);
        UnityEditor.AssetDatabase.SaveAssets();
#endif
    }

    static Vector3 AbsScale(Vector3 scale)
        => new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));

    void OnDrawGizmos()
    {
        var sceneIds = new HashSet<string>();

        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (!child.gameObject.activeSelf)
                continue;

            var marker = child.GetComponent<GrassExclusionShape>();
            GrassExclusionVolumes.Shape shape =
                marker != null ? marker.Shape : GrassExclusionVolumes.Shape.Box;

            if (marker != null)
                sceneIds.Add(marker.Id);

            DrawVolume(child.position, child.rotation, AbsScale(child.lossyScale), shape, _wireColour, _fillColour);

            if (_showLabels && marker != null)
                DrawLabel(child.position, marker.DisplayName);
        }

        if (!_showStoredVolumes || _asset == null)
        {
            Gizmos.matrix = Matrix4x4.identity;
            return;
        }

        // Stored-but-not-in-scene volumes, so a zone's full set is visible even after play
        // mode has thrown the scene objects away.
        List<GrassExclusionVolumes.Volume> stored = _asset.GetVolumes(_playfieldId);
        if (stored != null)
        {
            for (int i = 0; i < stored.Count; i++)
            {
                GrassExclusionVolumes.Volume v = stored[i];
                if (!string.IsNullOrEmpty(v.Id) && sceneIds.Contains(v.Id))
                    continue;

                DrawVolume(
                    v.Center, Quaternion.Euler(v.EulerAngles), v.Size, v.Shape,
                    _storedColour, new Color(_storedColour.r, _storedColour.g, _storedColour.b, 0.1f));

                if (_showLabels)
                    DrawLabel(v.Center, string.IsNullOrEmpty(v.Label) ? v.Id : $"{v.Label} ({v.Id})");
            }
        }

        Gizmos.matrix = Matrix4x4.identity;
    }

    static void DrawVolume(
        Vector3 position,
        Quaternion rotation,
        Vector3 size,
        GrassExclusionVolumes.Shape shape,
        Color wire,
        Color fill)
    {
        Gizmos.matrix = Matrix4x4.TRS(position, rotation, size);

        if (shape == GrassExclusionVolumes.Shape.Cylinder)
        {
            Gizmos.color = wire;
            DrawUnitCylinderWire();
        }
        else
        {
            Gizmos.color = fill;
            Gizmos.DrawCube(Vector3.zero, Vector3.one);
            Gizmos.color = wire;
            Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
        }

        Gizmos.matrix = Matrix4x4.identity;
    }

    static void DrawLabel(Vector3 position, string text)
    {
#if UNITY_EDITOR
        UnityEditor.Handles.Label(position, text);
#endif
    }

    /// <summary>
    /// Gizmos has no cylinder primitive, so draw one in the unit cube the gizmo matrix
    /// already maps: two rings at y = +/-0.5 plus four uprights. Radius 0.5 in x and z
    /// means a non-uniform scale reads as the ellipse the runtime test actually uses.
    /// </summary>
    static void DrawUnitCylinderWire()
    {
        const int segments = 24;

        Vector3 previousTop = default;
        Vector3 previousBottom = default;

        for (int i = 0; i <= segments; i++)
        {
            float angle = i / (float)segments * Mathf.PI * 2f;
            float x = Mathf.Cos(angle) * 0.5f;
            float z = Mathf.Sin(angle) * 0.5f;

            var top = new Vector3(x, 0.5f, z);
            var bottom = new Vector3(x, -0.5f, z);

            if (i > 0)
            {
                Gizmos.DrawLine(previousTop, top);
                Gizmos.DrawLine(previousBottom, bottom);
            }

            if (i % (segments / 4) == 0)
                Gizmos.DrawLine(bottom, top);

            previousTop = top;
            previousBottom = bottom;
        }
    }
}