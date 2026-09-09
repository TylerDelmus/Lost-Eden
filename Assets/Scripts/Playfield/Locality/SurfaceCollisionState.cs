/// <summary>
/// Readiness of streamed surface collision under a world position.
/// </summary>
public enum SurfaceCollisionState
{
    /// <summary>Cell not yet requested, queued, loading, or collider not active.</summary>
    Pending,

    /// <summary>Active surface MeshCollider is present.</summary>
    Ready,

    /// <summary>
    /// No surface expected or load finished empty/missing (indoor idle, out of layout,
    /// or RDB surface absent). Motors should not wait.
    /// </summary>
    Unavailable,
}
