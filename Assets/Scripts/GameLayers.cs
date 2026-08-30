using UnityEngine;

public static class GameLayers
{
    public const string GroundName = "Ground";

    static int _ground = int.MinValue;
    static int _groundMask = int.MinValue;

    public static int Ground
    {
        get
        {
            if (_ground == int.MinValue)
            {
                _ground = LayerMask.NameToLayer(GroundName);
                if (_ground < 0)
                    Debug.LogError($"GameLayers: layer '{GroundName}' is not defined in TagManager.");
            }

            return _ground;
        }
    }

    /// <summary>Mask for world occlusion (terrain / surface shells).</summary>
    public static int GroundMask
    {
        get
        {
            if (_groundMask == int.MinValue)
            {
                int layer = Ground;
                _groundMask = layer >= 0 ? 1 << layer : 0;
            }

            return _groundMask;
        }
    }

    public static void SetLayerRecursively(GameObject root, int layer)
    {
        if (root == null || layer < 0)
            return;

        root.layer = layer;
        Transform t = root.transform;
        for (int i = 0; i < t.childCount; i++)
            SetLayerRecursively(t.GetChild(i).gameObject, layer);
    }
}
