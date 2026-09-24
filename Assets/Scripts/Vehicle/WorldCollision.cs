using LostEden.Vehicles.Surfaces;
using UnityEngine;

namespace LostEden.Vehicles
{
    /// <summary>
    /// The playfield's collision surface, reachable from code that has no dependency-injected route to
    /// the <c>Playfield</c> — the camera's occlusion probe, dynel line-of-sight, the VFX ground queries.
    ///
    /// <para>
    /// This is what stock does: those queries go through <c>Surface_i</c>, not a physics engine.
    /// <c>CameraVehicleFixedThird_t::RecalcOptimalPos</c>'s occlusion search calls a line-of-sight
    /// predicate that is a <c>Surface_i::GetLineIntersection</c> underneath, and the playfield's surface
    /// is reached from a global map in stock too (<c>DummyVehicle_t::GetSurface</c> looks the playfield
    /// up by id in <c>N3 0x1005b7e8</c>). See <c>Docs/Movement.md</c> §9.
    /// </para>
    ///
    /// <para>
    /// Everything here takes and returns <b>world</b> coordinates; the surface answers in the terrain
    /// root's local space, so <see cref="Root"/> converts. With no surface bound every query reports
    /// "nothing hit", which is the same answer an empty scene gives.
    /// </para>
    /// </summary>
    public static class WorldCollision
    {
        /// <summary>The bound surface — terrain plus whatever statel cells are streamed in.</summary>
        public static ISurface Surface { get; private set; }

        /// <summary>The transform the surface's coordinates are expressed in. Null means the scene root.</summary>
        public static Transform Root { get; private set; }

        public static bool HasSurface => Surface != null;

        /// <summary>Called by <c>Playfield.SetCollisionSurface</c>.</summary>
        public static void Bind(ISurface surface, Transform root)
        {
            Surface = surface;
            Root = root;
        }

        public static void Unbind()
        {
            Surface = null;
            Root = null;
        }

        static Vec3 ToSurface(Vector3 world)
            => (Root != null ? Root.InverseTransformPoint(world) : world).ToVec3();

        static Vector3 FromSurface(Vec3 local)
            => Root != null ? Root.TransformPoint(local.ToUnity()) : local.ToUnity();

        static Vector3 DirectionFromSurface(Vec3 local)
            => Root != null ? Root.TransformDirection(local.ToUnity()) : local.ToUnity();

        /// <summary>
        /// True when the segment is blocked. The natural replacement for
        /// <c>Physics.Linecast</c>/<c>Physics.Raycast</c> against the ground layer.
        /// </summary>
        public static bool Blocked(Vector3 from, Vector3 to) => Blocked(from, to, out _, out _);

        /// <summary>True when the segment is blocked, with the hit and its normal in world space.</summary>
        public static bool Blocked(Vector3 from, Vector3 to, out Vector3 hit, out Vector3 normal)
        {
            hit = to;
            normal = Vector3.up;

            ISurface surface = Surface;
            if (surface == null)
                return false;

            if ((to - from).sqrMagnitude < 1e-8f)
                return false;

            if (!surface.GetLineIntersection(
                    ToSurface(from), ToSurface(to), out Vec3 localHit, out Vec3 localNormal, true, null))
                return false;

            hit = FromSurface(localHit);
            normal = DirectionFromSurface(localNormal).normalized;
            return true;
        }

        /// <summary>
        /// The ground under a world point: a downward segment from <paramref name="above"/> metres up to
        /// <paramref name="below"/> metres down. This is the shape the VFX queries want.
        /// </summary>
        public static bool GroundAt(
            Vector3 point, float above, float below, out Vector3 hit, out Vector3 normal)
        {
            Vector3 top = new Vector3(point.x, point.y + above, point.z);
            Vector3 bottom = new Vector3(point.x, point.y - below, point.z);
            return Blocked(top, bottom, out hit, out normal);
        }
    }
}
