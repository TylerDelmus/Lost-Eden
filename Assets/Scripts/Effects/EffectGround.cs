using UnityEngine;

/// <summary>
/// Stand-in for stock's ground height query (<c>Gamecode 100d33f3</c>, the playfield under a point).
/// The port casts straight down onto whatever isn't a dynel. NaN when nothing is there, so callers keep
/// their own height.
/// </summary>
public static class EffectGround
{
    const float Above = 200f;
    const float Reach = 5000f;

    public static float HeightAt(float x, float y, float z)
    {
        int mask = ~GameLayers.DynelMask;
        var origin = new Vector3(x, y + Above, z);
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, Reach, mask, QueryTriggerInteraction.Ignore))
            return hit.point.y;
        return float.NaN;
    }
}
