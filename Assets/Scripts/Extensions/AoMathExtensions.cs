using UnityEngine;
using AoVector3 = AOSharp.Common.GameData.Vector3;
using AoQuaternion = AOSharp.Common.GameData.Quaternion;

public static class AoMathExtensions
{
    public static Vector3 ToUnity(this AoVector3 v) => new(v.X, v.Y, v.Z);

    public static Quaternion ToUnity(this AoQuaternion q) => new(q.X, q.Y, q.Z, q.W);

    public static AoVector3 ToAo(this Vector3 v) => new(v.x, v.y, v.z);

    public static AoQuaternion ToAo(this Quaternion q) => new(q.x, q.y, q.z, q.w);
}
