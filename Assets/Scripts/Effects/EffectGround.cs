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
        if (LostEden.Vehicles.WorldCollision.HasSurface)
        {
            return LostEden.Vehicles.WorldCollision.GroundAt(
                new Vector3(x, y, z), Above, Reach - Above, out Vector3 surfaceHit, out _)
                ? surfaceHit.y
                : float.NaN;
        }

        int mask = ~GameLayers.DynelMask;
        var origin = new Vector3(x, y + Above, z);
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, Reach, mask, QueryTriggerInteraction.Ignore))
            return hit.point.y;
        return float.NaN;
    }

    /// <summary>
    /// The ground under a point with its normal, as stock's playfield query gives both (<c>Gamecode 100ada15</c>).
    /// False when nothing is there.
    /// </summary>
    public static bool TryGround(float x, float y, float z, out float height, out Vector3 normal)
    {
        if (LostEden.Vehicles.WorldCollision.HasSurface)
        {
            if (LostEden.Vehicles.WorldCollision.GroundAt(
                    new Vector3(x, y, z), Above, Reach - Above,
                    out Vector3 surfaceHit, out Vector3 surfaceNormal))
            {
                height = surfaceHit.y;
                normal = surfaceNormal;
                return true;
            }

            height = float.NaN;
            normal = Vector3.up;
            return false;
        }

        int mask = ~GameLayers.DynelMask;
        var origin = new Vector3(x, y + Above, z);
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, Reach, mask, QueryTriggerInteraction.Ignore))
        {
            height = hit.point.y;
            normal = hit.normal;
            return true;
        }

        height = float.NaN;
        normal = Vector3.zero;
        return false;
    }
}
