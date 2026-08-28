using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
static class AttractorSceneDebug
{
    const float PickRadius = 0.05f;
    const float LabelOffset = 0.06f;

    static AttractorSceneDebug()
    {
        SceneView.duringSceneGui += OnSceneGUI;
    }

    static void OnSceneGUI(SceneView sceneView)
    {
        AttractorCollection collection = FindSelectedCollection();
        if (collection == null || collection.ByPlace == null || collection.ByPlace.Count == 0)
            return;

        Color prevColor = Handles.color;
        foreach (var pair in collection.ByPlace)
        {
            Attractor attractor = pair.Value;
            if (attractor == null)
                continue;

            Vector3 pos = attractor.transform.position;
            bool isSelected = Selection.activeGameObject == attractor.gameObject;

            Handles.color = isSelected
                ? new Color(1f, 0.85f, 0.2f, 1f)
                : new Color(0.2f, 0.85f, 1f, 0.95f);

            float size = HandleUtility.GetHandleSize(pos) * 0.12f;
            size = Mathf.Max(size, PickRadius);

            if (Handles.Button(pos, Quaternion.identity, size, size, Handles.SphereHandleCap))
            {
                Selection.activeGameObject = attractor.gameObject;
                EditorGUIUtility.PingObject(attractor.gameObject);
            }

            Handles.Label(pos + Vector3.up * LabelOffset, pair.Key.ToString());
        }

        Handles.color = prevColor;
    }

    static AttractorCollection FindSelectedCollection()
    {
        GameObject active = Selection.activeGameObject;
        if (active == null)
            return null;

        if (active.TryGetComponent(out AttractorCollection onSelf))
            return onSelf;

        if (active.TryGetComponent(out Character character))
            return character.GetComponentInChildren<AttractorCollection>(true);

        AttractorCollection inParents = active.GetComponentInParent<AttractorCollection>();
        if (inParents != null)
            return inParents;

        return active.GetComponentInChildren<AttractorCollection>(true);
    }
}
