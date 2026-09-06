using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

/// <summary>
/// Face handles for exclusion volumes - the same interaction a BoxCollider gets from
/// "Edit Collider": drag one face and the opposite one stays put.
///
/// MUST live in a folder named "Editor" (e.g. Assets/Scripts/Rendering/Grass/Editor/),
/// or the build will fail on the UnityEditor references.
///
/// The Rect tool does not help here: it is a 2D/RectTransform tool and on a 3D object it
/// only operates in one plane. Unity's Scale tool always works from the pivot, which for a
/// box authored over a bridge means every resize also moves it - you end up nudging the
/// position back after every scale. A bounds handle edits size and centre together, which
/// is what "extend this edge to the end of the deck" actually needs.
/// </summary>
[CustomEditor(typeof(GrassExclusionShape))]
public sealed class GrassExclusionShapeEditor : Editor
{
    BoxBoundsHandle _handle;

    void OnEnable()
    {
        _handle = new BoxBoundsHandle
        {
            axes = PrimitiveBoundsHandle.Axes.All,
            midpointHandleDrawFunction = Handles.DotHandleCap
        };
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Drag the face handles in the scene view to resize. The opposite face stays " +
            "put, unlike the Scale tool which works from the centre.",
            MessageType.None);
    }

    void OnSceneGUI()
    {
        var shape = (GrassExclusionShape)target;
        Transform t = shape.transform;

        // Handle space is the object's position and rotation but NOT its scale: scale is
        // exactly what we are editing, so folding it into the matrix would compound it on
        // every drag.
        Matrix4x4 space = Matrix4x4.TRS(t.position, t.rotation, Vector3.one);
        Color colour = shape.Shape == GrassExclusionVolumes.Shape.Cylinder
            ? new Color(0.4f, 0.8f, 1f)
            : new Color(1f, 0.5f, 0.3f);

        using (new Handles.DrawingScope(colour, space))
        {
            _handle.center = Vector3.zero;
            _handle.size = Abs(t.lossyScale);

            EditorGUI.BeginChangeCheck();
            _handle.DrawHandle();
            if (!EditorGUI.EndChangeCheck())
                return;

            Undo.RecordObject(t, "Resize Grass Exclusion Volume");

            // Dragging one face moves the handle's centre as well as its size. Converting
            // that offset back to world space is what keeps the opposite face still.
            t.position = t.position + t.rotation * _handle.center;
            t.localScale = LocalScaleFor(t, Abs(_handle.size));
        }
    }

    /// <summary>
    /// The handle works in world-sized units, so convert back through the parent's scale.
    /// Volumes are saved from lossyScale, so a scaled authoring parent would otherwise make
    /// the handle disagree with what gets stored.
    /// </summary>
    static Vector3 LocalScaleFor(Transform t, Vector3 worldSize)
    {
        Transform parent = t.parent;
        if (parent == null)
            return worldSize;

        Vector3 p = parent.lossyScale;
        return new Vector3(
            SafeDivide(worldSize.x, p.x),
            SafeDivide(worldSize.y, p.y),
            SafeDivide(worldSize.z, p.z));
    }

    static float SafeDivide(float value, float divisor)
        => Mathf.Approximately(divisor, 0f) ? value : value / divisor;

    static Vector3 Abs(Vector3 v)
        => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
}
