using System;

namespace LostEden.Vehicles.Surfaces
{
    /// <summary>
    /// One cell's worth of static collision geometry — a triangle soup in playfield world
    /// coordinates, from AODB record type <b>1000013</b> (<c>SurfaceResource</c>).
    /// See Docs/Movement.md §8.
    ///
    /// <para>
    /// Stock's equivalent is <c>KDTreeSurface_c</c>, which is <b>header-only</b> (RTTI in
    /// Collision.dll, N3.dll and Gamecode.dll, no exported methods, every copy inlined) and was
    /// therefore <b>not read</b>. Its KD-tree is an acceleration structure: for the nearest hit along a
    /// segment a direct sweep over the triangles returns the same answer, and a cell averages about 78
    /// triangles. What is <i>not</i> merely an optimisation is
    /// <see cref="CalculateClosestPoint"/> — see the note there.
    /// </para>
    /// </summary>
    public sealed class TriangleMeshSurface : ISurface
    {
        readonly Vec3[] _vertices;
        readonly int[] _indices;

        // A world AABB, for rejecting a segment before touching any triangle.
        readonly float _minX, _minY, _minZ, _maxX, _maxY, _maxZ;

        /// <summary>
        /// <paramref name="indices"/> is triples into <paramref name="vertices"/>. Both are taken by
        /// reference and must not be mutated afterwards.
        /// </summary>
        public TriangleMeshSurface(Vec3[] vertices, int[] indices)
        {
            _vertices = vertices ?? Array.Empty<Vec3>();
            _indices = indices ?? Array.Empty<int>();

            _minX = _minY = _minZ = float.MaxValue;
            _maxX = _maxY = _maxZ = float.MinValue;
            foreach (Vec3 v in _vertices)
            {
                if (v.X < _minX) _minX = v.X;
                if (v.X > _maxX) _maxX = v.X;
                if (v.Y < _minY) _minY = v.Y;
                if (v.Y > _maxY) _maxY = v.Y;
                if (v.Z < _minZ) _minZ = v.Z;
                if (v.Z > _maxZ) _maxZ = v.Z;
            }
        }

        public int TriangleCount => _indices.Length / 3;

        /// <summary>The world AABB of the geometry, or an inverted box when empty.</summary>
        public void GetBounds(out Vec3 min, out Vec3 max)
        {
            min = new Vec3(_minX, _minY, _minZ);
            max = new Vec3(_maxX, _maxY, _maxZ);
        }

        // ---- slot 4 ---------------------------------------------------------

        /// <summary>
        /// The nearest triangle hit along the segment. Uses
        /// <see cref="TilemapSurface.RayTriangle"/> so statel geometry and terrain are tested with the
        /// same numerics stock's <c>Intersect_Tile</c> uses, which also means the normal it hands back
        /// already faces the caller (it returns <c>-cross(e0, e1)</c> after requiring the ray to run
        /// <i>along</i> that cross product). It is <b>not</b> unit length — stock normalises afterwards,
        /// in <c>Intersect_Tile</c> (<c>10018a47</c>) rather than in the triangle test — so this does.
        ///
        /// <para>
        /// <b>Each triangle is tested with both windings.</b> <c>RayTriangle</c> is one-sided
        /// (<c>denominator &lt;= 0</c> rejects), which is right for terrain because tile winding and
        /// downward probes always agree, but the winding of record 1000013's triangles is unknown
        /// without reading <c>KDTreeSurface_c</c>. Testing both makes static world geometry solid from
        /// either side, which is the safe reading: the alternative silently drops half of every wall.
        /// </para>
        /// </summary>
        public bool GetLineIntersection(
            Vec3 start, Vec3 end, out Vec3 hit, out Vec3 normal, bool clipToBounds, object locality)
        {
            hit = Vec3.Zero;
            normal = Vec3.ReferenceUp;

            if (_indices.Length == 0 || !SegmentHitsBounds(start, end))
                return false;

            Vec3 direction = end - start;
            float length = direction.Length;
            if (length <= 0f)
                return false;

            direction = direction * (1f / length);

            bool found = false;
            float best = 0f;

            for (int i = 0; i + 2 < _indices.Length; i += 3)
            {
                Vec3 v0 = _vertices[_indices[i]];
                Vec3 v1 = _vertices[_indices[i + 1]];
                Vec3 v2 = _vertices[_indices[i + 2]];

                if (!TilemapSurface.RayTriangle(start, direction, v0, v1, v2, out Vec3 h, out Vec3 n)
                    && !TilemapSurface.RayTriangle(start, direction, v0, v2, v1, out h, out n))
                    continue;

                float t = Vec3.Dot(h - start, direction);
                if (t < 0f || t > length)                       // behind the start, or past the end
                    continue;

                if (found && t >= best)
                    continue;

                float nl = n.Length;                            // RayTriangle does not normalise
                hit = h;
                normal = nl > 0f ? n * (1f / nl) : Vec3.ReferenceUp;
                best = t;
                found = true;
            }

            return found;
        }

        bool SegmentHitsBounds(Vec3 a, Vec3 b)
        {
            if (_minX > _maxX)
                return false;

            if (Math.Max(a.X, b.X) < _minX || Math.Min(a.X, b.X) > _maxX) return false;
            if (Math.Max(a.Y, b.Y) < _minY || Math.Min(a.Y, b.Y) > _maxY) return false;
            if (Math.Max(a.Z, b.Z) < _minZ || Math.Min(a.Z, b.Z) > _maxZ) return false;
            return true;
        }

        // ---- slot 1 ---------------------------------------------------------

        /// <summary>
        /// The <b>floor</b> under <paramref name="point"/>: the highest triangle whose XZ projection
        /// contains it and which is not above it. Returns
        /// <see cref="TilemapSurface.NoClosestPoint"/> when nothing is under it, which makes the caller
        /// keep the terrain.
        ///
        /// <para>
        /// <b>This is a reasoned choice, not a 1:1 port</b> — stock's version is inside the unread
        /// <c>KDTreeSurface_c</c>. A true 3D closest point would be wrong here: the ground clamp raises
        /// the body to <c>closest.Y + stepHeight</c> (§6), so standing beside a wall would return a
        /// point on the wall at roughly the body's own height and lift the body a step every frame —
        /// it would climb walls. A downward floor query is the only reading that makes the clamp
        /// behave, and it is what lets a body stand on a statel deck while still walking under a
        /// bridge.
        /// </para>
        /// </summary>
        public void CalculateClosestPoint(Vec3 point, out Vec3 closest, out Vec3 normal, object locality)
        {
            // TilemapSurface.NoClosestPoint, not the point itself: a surface that returns the point it
            // was handed beats the terrain from any height and the ground clamp stops working.
            closest = new Vec3(point.X, TilemapSurface.NoClosestPoint, point.Z);
            normal = Vec3.ReferenceUp;

            if (_indices.Length == 0)
                return;
            if (point.X < _minX || point.X > _maxX || point.Z < _minZ || point.Z > _maxZ)
                return;

            // A float-equality guard, not a game constant: a body resting on a deck sits at the deck's
            // own height, so the surface it stands on must not be excluded by rounding.
            const float Tolerance = 1e-3f;
            float ceiling = point.Y + Tolerance;

            bool found = false;
            float bestY = 0f;
            Vec3 bestNormal = Vec3.ReferenceUp;

            for (int i = 0; i + 2 < _indices.Length; i += 3)
            {
                Vec3 v0 = _vertices[_indices[i]];
                Vec3 v1 = _vertices[_indices[i + 1]];
                Vec3 v2 = _vertices[_indices[i + 2]];

                if (!HeightAt(point.X, point.Z, v0, v1, v2, out float y, out Vec3 n))
                    continue;
                if (y > ceiling)
                    continue;
                if (found && y <= bestY)
                    continue;

                bestY = y;
                bestNormal = n;
                found = true;
            }

            if (!found)
                return;

            closest = new Vec3(point.X, bestY, point.Z);
            normal = bestNormal;
        }

        /// <summary>
        /// The triangle's height at an XZ position, or false when the position is outside it or the
        /// triangle is vertical (no height to give). Barycentric, so it matches the plane exactly at
        /// the vertices.
        /// </summary>
        static bool HeightAt(float x, float z, Vec3 v0, Vec3 v1, Vec3 v2, out float y, out Vec3 normal)
        {
            y = 0f;
            normal = Vec3.ReferenceUp;

            float d = (v1.Z - v2.Z) * (v0.X - v2.X) + (v2.X - v1.X) * (v0.Z - v2.Z);
            if (d == 0f)
                return false;

            float a = ((v1.Z - v2.Z) * (x - v2.X) + (v2.X - v1.X) * (z - v2.Z)) / d;
            if (a < 0f || a > 1f)
                return false;

            float b = ((v2.Z - v0.Z) * (x - v2.X) + (v0.X - v2.X) * (z - v2.Z)) / d;
            if (b < 0f || a + b > 1f)
                return false;

            float c = 1f - a - b;
            if (c < 0f)
                return false;

            y = a * v0.Y + b * v1.Y + c * v2.Y;

            Vec3 n = Vec3.Cross(v1 - v0, v2 - v0);
            if (n.Y < 0f)
                n = -n;                                          // a floor normal always points up
            float length = n.Length;
            normal = length > 0f ? n * (1f / length) : Vec3.ReferenceUp;
            return true;
        }

        /// <summary>Slot 8 — a stub, like every other surface's.</summary>
        public bool VetoPosition(ref Vec3 position, object locality, Vec3 previous) => false;
    }
}
