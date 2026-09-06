using UnityEngine;

/// <summary>
/// Identity and shape for one exclusion volume child.
///
/// The Id is what makes saving non-destructive: it is generated once when the volume is
/// created and travels with it through save/load cycles, so re-saving updates that volume
/// in the asset instead of appending a duplicate or wiping everything else.
///
/// A child with no marker is treated as an unlabelled box, so volumes authored before this
/// existed still work.
/// </summary>
[DisallowMultipleComponent]
public sealed class GrassExclusionShape : MonoBehaviour
{
    [SerializeField] string _id;

    [Tooltip("Optional name shown in the scene view. Purely for finding this volume again.")]
    [SerializeField] string _label;

    [SerializeField] GrassExclusionVolumes.Shape _shape = GrassExclusionVolumes.Shape.Box;

    public GrassExclusionVolumes.Shape Shape
    {
        get => _shape;
        set => _shape = value;
    }

    public string Label
    {
        get => _label;
        set => _label = value;
    }

    /// <summary>Generates the id on first access, so a hand-added component still gets one.</summary>
    public string Id
    {
        get
        {
            if (string.IsNullOrEmpty(_id))
                _id = GrassExclusionVolumes.NewId();

            return _id;
        }
        set => _id = value;
    }

    public string DisplayName => string.IsNullOrEmpty(_label) ? Id : $"{_label} ({Id})";

    void Reset()
    {
        _id = GrassExclusionVolumes.NewId();
    }

    [ContextMenu("Remove From Asset")]
    void RemoveFromAsset()
    {
        var authoring = GetComponentInParent<GrassExclusionAuthoring>();
        if (authoring == null || authoring.Asset == null)
        {
            Debug.LogError("GrassExclusionShape: no parent GrassExclusionAuthoring with an asset assigned.");
            return;
        }

        bool removed = authoring.Asset.Remove(authoring.PlayfieldId, Id);

#if UNITY_EDITOR
        if (removed)
        {
            UnityEditor.EditorUtility.SetDirty(authoring.Asset);
            UnityEditor.AssetDatabase.SaveAssets();
        }
#endif

        Debug.Log(removed
            ? $"GrassExclusionShape: removed {DisplayName} from playfield {authoring.PlayfieldId}."
            : $"GrassExclusionShape: {DisplayName} was not stored for playfield {authoring.PlayfieldId}.");
    }
}