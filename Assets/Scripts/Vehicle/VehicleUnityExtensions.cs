using N3Lite;
using N3Lite.Surfaces;
using UnityEngine;

namespace LostEden.Vehicles
{
    /// <summary>
    /// Conversions between the Unity-free vehicle maths and Unity's types. Kept in its own file so
    /// <c>Vec3</c> and <c>Quat</c> stay in the N3Lite package, which the server builds with no Unity
    /// reference.
    ///
    /// Both sides use the same left-handed, Y-up, X-right, Z-forward convention and the same x/y/z/w
    /// quaternion order, so every conversion here is a field copy.
    /// </summary>
    public static class VehicleUnityExtensions
    {
        public static Vector3 ToUnity(this Vec3 v) => new Vector3(v.X, v.Y, v.Z);

        public static Vec3 ToVec3(this Vector3 v) => new Vec3(v.x, v.y, v.z);

        public static Quaternion ToUnity(this Quat q) => new Quaternion(q.X, q.Y, q.Z, q.W);

        public static Quat ToQuat(this Quaternion q) => new Quat(q.x, q.y, q.z, q.w);
    }
}
