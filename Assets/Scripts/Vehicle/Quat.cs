using System;

namespace LostEden.Vehicles
{
    /// <summary>
    /// Stock <c>Quaternion_t</c>, stored x, y, z, w to match the <c>Vehicle_t</c> layout at
    /// <c>+0x80..+0x8c</c> (the constructor at <c>Vehicle.dll 1000ce2f</c> writes 0, 0, 0, 1).
    /// Unity-free; the binding layer converts to <c>UnityEngine.Quaternion</c>, which uses the same
    /// component order and the same left-handed convention, so the conversion is a field copy.
    /// </summary>
    public struct Quat : IEquatable<Quat>
    {
        public float X;
        public float Y;
        public float Z;
        public float W;

        public Quat(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        public static readonly Quat Identity = new Quat(0f, 0f, 0f, 1f);

        /// <summary>Rotation of <paramref name="radians"/> about a <b>unit</b> <paramref name="axis"/>.</summary>
        public static Quat FromAxisAngle(Vec3 axis, float radians)
        {
            float half = radians * 0.5f;
            float s = (float)Math.Sin(half);
            return new Quat(axis.X * s, axis.Y * s, axis.Z * s, (float)Math.Cos(half));
        }

        /// <summary>Hamilton product: <paramref name="a"/> applied after <paramref name="b"/>.</summary>
        public static Quat operator *(Quat a, Quat b) => new Quat(
            a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
            a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
            a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
            a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);

        /// <summary>Rotate a vector by this quaternion.</summary>
        public static Vec3 operator *(Quat q, Vec3 v)
        {
            var u = new Vec3(q.X, q.Y, q.Z);
            float s = q.W;
            return u * (2f * Vec3.Dot(u, v))
                 + v * (s * s - Vec3.Dot(u, u))
                 + Vec3.Cross(u, v) * (2f * s);
        }

        /// <summary>
        /// <c>FUN_10005c95</c> — the inverse rotation for a unit quaternion, used by
        /// <c>UpdateHeadingToPos</c> to take a world direction into the target's local frame.
        /// </summary>
        public Quat Conjugate => new Quat(-X, -Y, -Z, W);

        public float Length => (float)Math.Sqrt(X * X + Y * Y + Z * Z + W * W);

        public Quat Normalized
        {
            get
            {
                float len = Length;
                if (len == 0f)
                    return Identity;
                return new Quat(X / len, Y / len, Z / len, W / len);
            }
        }

        public bool Equals(Quat other) => X == other.X && Y == other.Y && Z == other.Z && W == other.W;

        public override bool Equals(object obj) => obj is Quat q && Equals(q);

        public override int GetHashCode() => (X, Y, Z, W).GetHashCode();

        public override string ToString() => $"({X}, {Y}, {Z}, {W})";
    }
}
