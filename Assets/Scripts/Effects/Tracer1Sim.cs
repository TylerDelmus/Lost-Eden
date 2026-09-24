using System;

/// <summary>
/// Stock <c>_GfxControlTracer1_t</c> (typeCode 1019 / 0x3fb, vftable <c>Gamecode 1016df3c</c>): a
/// nano's flying projectile, e.g. 28612's tracer 45694. <c>CreateGfxControl(id, hitLocation)</c>
/// (<c>100fecea</c>) reads the hit location's start and end once and builds it from them;
/// <c>CreateGfxControlTracer(id, from, to)</c> (<c>100fec4e</c>) passes the two points directly.
///
/// Fields (loader <c>FUN_100fe3b0</c>):
///   0 flags (locator: bit 0 track, bit 1 local, bit 2 rotation order), 1-3 locator offset,
///   4-6 locator rotation, 8 duration, 9 material, 10-13 sprite A,R,G,B (14-17 are read but unused),
///   18 sprite size, 19 links per ring N, 20 rings M, then 2N points (x, y, z) from field 21:
///   link k runs from point 2k to point 2k+1.
///
/// Build (<c>FUN_100fe9f9</c>): dir = end - start and dist = |dir|; anything shorter than 0.01 readies
/// the control. Speed = min(100, 5 * dist), so the flight takes max(0.2, dist / 100) seconds. The
/// locator is a fixed matrix (kind 2) with rows cross(p, dir), p, dir, start, where p is
/// <c>_GfxControl_t::FindPerpendicular(dir)</c>, with the template's rotation and offset applied on
/// top (<c>_GfxLocator_t 10106903</c> / <c>10106193</c>). The visual (<c>FUN_100fe845</c>) is a
/// <see cref="FlareType0Visual"/> of N*M sprites, one per link per ring (<c>FUN_100fe545</c>), ring r
/// turned about local Y by 2*pi*r/M. The sprites never move or fade: no velocities, no ramps, and a
/// life of 1 s, which is also the default life, so they are gone after one second of flight.
///
/// Process (<c>100fe7e1</c> / <c>100fe706</c>): d = speed * age, and the control is ready once d
/// reaches dist; the visual sits at the locator position + dir * d, then the sprites run.
///
/// Field 0 bit 1 (local) keeps the sprites in the locator's frame and turns the visual with it.
/// Otherwise the sprites sit at locator position + point, unrotated, and the visual is unrotated.
/// </summary>
public sealed class Tracer1Sim
{
    public const int FlagTrack = 1;
    public const int FlagLocal = 2;
    public const int FlagRotationZyx = 4;

    /// <summary>+0x9c before the loader clamps it (<c>100fecea</c>).</summary>
    public const float MaxSpeed = 100f;

    /// <summary>The loader's clamp: speed is at most dist * 5, i.e. the flight lasts at least 0.2 s.</summary>
    public const double SpeedPerDistance = 5.0;

    public const float MinDistance = 0.01f;

    /// <summary>Life and default life of every sprite (<c>FUN_100fe845</c> / <c>FUN_100fe545</c>).</summary>
    public const float SpriteLife = 1f;

    readonly FlareType0Visual _visual;
    readonly float[] _basis = new float[9];
    readonly bool _local;

    /// <summary>True when stock readies the control at build: a segment shorter than 0.01.</summary>
    public bool Degenerate { get; }

    /// <summary>Segment length (+0xb8).</summary>
    public float Distance { get; }

    /// <summary>Metres per second (+0x9c).</summary>
    public float Speed { get; }

    /// <summary>Unit direction start to end (+0xac).</summary>
    public float DirX { get; }
    public float DirY { get; }
    public float DirZ { get; }

    /// <summary>Locator position (<c>_GfxLocator_t</c> +0xc): start plus the template offset.</summary>
    public float OriginX { get; }
    public float OriginY { get; }
    public float OriginZ { get; }

    /// <summary>The visual's position after the last <see cref="Step"/>.</summary>
    public float VisualX { get; private set; }
    public float VisualY { get; private set; }
    public float VisualZ { get; private set; }

    /// <summary>
    /// Locator rows 0..2 (row-major 3x3: each row is a local axis in world space). The visual turns
    /// by these in local mode and not at all otherwise.
    /// </summary>
    public float[] Basis => _basis;

    public bool Local => _local;

    public FlareType0Visual Visual => _visual;

    /// <summary>Seconds from launch to arrival.</summary>
    public float FlightSeconds => Speed > 0f ? Distance / Speed : 0f;

    /// <param name="fields">The template's field array (floats; ints read as raw bits).</param>
    /// <param name="firstFrame">The material's first frame (<c>FUN_100fe845</c> +0x78).</param>
    public Tracer1Sim(
        float[] fields,
        float startX, float startY, float startZ,
        float endX, float endY, float endZ,
        int firstFrame)
    {
        int flags = Int(fields, 0);
        _local = (flags & FlagLocal) != 0;

        // dir = end - start; dist = sqrt of the float length squared.
        float dx = endX - startX;
        float dy = endY - startY;
        float dz = endZ - startZ;
        float lenSq = dx * dx + dy * dy + dz * dz;
        float dist = (float)Math.Sqrt(lenSq);
        Distance = dist;

        int links = Int(fields, 19);
        int rings = Int(fields, 20);
        _visual = new FlareType0Visual(Math.Max(0, links * 2 * rings / 2), SpriteLife);

        if (!(MinDistance <= dist))
        {
            Degenerate = true;
            return;
        }

        float speed = MaxSpeed;
        float limit = (float)(dist * SpeedPerDistance);
        if (speed > limit)
            speed = limit;
        Speed = speed;

        // SetLength(1): scale by 1 / |v|.
        float inv = 1f / dist;
        dx *= inv;
        dy *= inv;
        dz *= inv;
        DirX = dx;
        DirY = dy;
        DirZ = dz;

        FindPerpendicular(dx, dy, dz, out float px, out float py, out float pz);
        Cross(px, py, pz, dx, dy, dz, out float tx, out float ty, out float tz);
        SetLength1(ref tx, ref ty, ref tz);

        // Locator matrix rows: cross(p, dir), p, dir. Row 3 is start.
        _basis[0] = tx; _basis[1] = ty; _basis[2] = tz;
        _basis[3] = px; _basis[4] = py; _basis[5] = pz;
        _basis[6] = dx; _basis[7] = dy; _basis[8] = dz;

        ApplyLocatorRotation(fields, flags, _basis);

        float ox = startX, oy = startY, oz = startZ;
        ApplyLocatorOffset(fields, _basis, ref ox, ref oy, ref oz);
        OriginX = ox;
        OriginY = oy;
        OriginZ = oz;

        BuildSprites(fields, links, rings, firstFrame);

        // FUN_100fe845 places the visual on the locator position (local) or zero before any Process.
        PlaceVisual(0f);
    }

    /// <summary>
    /// One stock Process after the base timer (<c>100fe7e1</c>): move, then run the sprites. Returns
    /// true once the projectile has reached the end, which readies the control.
    /// </summary>
    public bool Step(float age, float dt)
    {
        if (Degenerate)
            return true;

        float d = Speed * age;
        bool arrived = false;
        if (Distance <= d)
        {
            d = Distance;
            arrived = true;
        }

        PlaceVisual(d);
        _visual.ProcessSprites(dt);
        return arrived;
    }

    void PlaceVisual(float d)
    {
        // GetPosition (10106306): the locator position in local mode, zero in world mode.
        float bx = _local ? OriginX : 0f;
        float by = _local ? OriginY : 0f;
        float bz = _local ? OriginZ : 0f;
        VisualX = bx + DirX * d;
        VisualY = by + DirY * d;
        VisualZ = bz + DirZ * d;
    }

    /// <summary><c>FUN_100fe545</c>.</summary>
    void BuildSprites(float[] fields, int links, int rings, int firstFrame)
    {
        // FUN_1010640a: zero in local mode, the locator position in world mode.
        float gx = _local ? 0f : OriginX;
        float gy = _local ? 0f : OriginY;
        float gz = _local ? 0f : OriginZ;

        float size = F(fields, 18);
        float a = F(fields, 10), r = F(fields, 11), g = F(fields, 12), b = F(fields, 13);

        for (int ring = 0; ring < rings; ring++)
        {
            float angle = (float)((double)ring / rings * 6.2831854820251465);
            float c = (float)Math.Cos(angle);
            float s = (float)Math.Sin(angle);

            for (int k = 0; k < links; k++)
            {
                int p = 21 + k * 6;
                float ax = F(fields, p), ay = F(fields, p + 1), az = F(fields, p + 2);
                float qx = F(fields, p + 3), qy = F(fields, p + 4), qz = F(fields, p + 5);

                _visual.NewSprite(new FlareType0Visual.Sprite
                {
                    P1x = gx + (ax * c - az * s),
                    P1y = gy + ay,
                    P1z = gz + (az * c + ax * s),
                    P2x = gx + (qx * c - qz * s),
                    P2y = gy + qy,
                    P2z = gz + (qz * c + qx * s),
                    Size0 = size,
                    Size1 = size,
                    Life = SpriteLife,
                    Argb = 0xffffffffu,
                    A = a,
                    R = r,
                    G = g,
                    B = b,
                    Frame = firstFrame,
                    Active = true,
                });
            }
        }
    }

    /// <summary>
    /// The template rotation (fields 4-6, field 0 bit 2 picks the order) applied to a locator basis:
    /// basis = R x basis (<c>_GfxLocator_t 10106903</c> / <c>10106193</c>).
    /// </summary>
    public static void ApplyLocatorRotation(float[] fields, int flags, float[] basis)
    {
        float r4 = F(fields, 4), r5 = F(fields, 5), r6 = F(fields, 6);
        if (r4 == 0f && r5 == 0f && r6 == 0f)
            return;

        var rot = new float[] { 1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f };
        if ((flags & FlagRotationZyx) == 0)
        {
            RotateAround(rot, 1, 2, r4); // E0
            RotateAround(rot, 2, 0, r5); // E1
            RotateAround(rot, 0, 1, r6); // E2
        }
        else
        {
            RotateAround(rot, 0, 1, r6);
            RotateAround(rot, 2, 0, r5);
            RotateAround(rot, 1, 2, r4);
        }
        MultiplyInto(rot, basis);
    }

    /// <summary>The template offset (fields 1-3) along the basis rows: 10106193.</summary>
    public static void ApplyLocatorOffset(float[] fields, float[] basis, ref float ox, ref float oy, ref float oz)
    {
        float f1 = F(fields, 1), f2 = F(fields, 2), f3 = F(fields, 3);
        if (f1 == 0f && f2 == 0f && f3 == 0f)
            return;
        ox += basis[0] * f1 + basis[3] * f2 + basis[6] * f3;
        oy += basis[1] * f1 + basis[4] * f2 + basis[7] * f3;
        oz += basis[2] * f1 + basis[5] * f2 + basis[8] * f3;
    }

    /// <summary>
    /// <c>_GfxControl_t::FindPerpendicular</c> (<c>100d3363</c>): a unit vector perpendicular to
    /// <paramref name="x"/>.., or (1, 0, 0) when both of its trial vectors vanish.
    /// </summary>
    public static void FindPerpendicular(float x, float y, float z, out float px, out float py, out float pz)
    {
        float tx = y - z, ty = z - x, tz = x - y;
        if (tx * tx + ty * ty + tz * tz == 0f)
        {
            tx = z + y;
            ty = z - x;
            tz = -x - y;
            if (tx * tx + ty * ty + tz * tz == 0f)
            {
                px = 1f;
                py = ty;
                pz = tz;
                return;
            }
        }

        Cross(x, y, z, tx, ty, tz, out px, out py, out pz);
        SetLength1(ref px, ref py, ref pz);
    }

    static void Cross(float ax, float ay, float az, float bx, float by, float bz, out float x, out float y, out float z)
    {
        x = ay * bz - az * by;
        y = az * bx - ax * bz;
        z = ax * by - ay * bx;
    }

    static void SetLength1(ref float x, ref float y, ref float z)
    {
        float k = 1f / (float)Math.Sqrt(x * x + y * y + z * z);
        x *= k;
        y *= k;
        z *= k;
    }

    /// <summary>
    /// <c>RotateAroundE0/E1/E2</c> (<c>100d2b39</c> / <c>100d2bfe</c> / <c>100d2cc1</c>): with
    /// c = cos, s = sin, row a becomes c*a + s*b and row b becomes c*b - s*a.
    /// </summary>
    static void RotateAround(float[] m, int rowA, int rowB, float angle)
    {
        float c = (float)Math.Cos(angle);
        float s = (float)Math.Sin(angle);
        for (int i = 0; i < 3; i++)
        {
            float va = m[rowA * 3 + i];
            float vb = m[rowB * 3 + i];
            m[rowA * 3 + i] = va * c + vb * s;
            m[rowB * 3 + i] = vb * c - va * s;
        }
    }

    /// <summary><paramref name="m"/> = <paramref name="rot"/> x <paramref name="m"/> (<c>1013be32</c>, row-major).</summary>
    static void MultiplyInto(float[] rot, float[] m)
    {
        var o = new float[9];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                o[i * 3 + j] = rot[i * 3] * m[j] + rot[i * 3 + 1] * m[3 + j] + rot[i * 3 + 2] * m[6 + j];
        Array.Copy(o, m, 9);
    }

    static float F(float[] f, int i) => f != null && i < f.Length ? f[i] : 0f;

    static int Int(float[] f, int i) => f != null && i < f.Length ? GfxBits.Of(f, i) : 0;
}
