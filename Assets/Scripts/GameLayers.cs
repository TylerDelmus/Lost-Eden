using UnityEngine;

public static class GameLayers
{
    public const string GroundName = "Ground";

    static int _ground = int.MinValue;

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
