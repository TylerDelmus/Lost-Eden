using System;

/// <summary>
/// Stock <c>_GfxControlTracer4_t</c> (typeCode 1024 / 0x400, vftable <c>Gamecode 1016e04c</c>): three
/// twisting ribbons wound round the line from a hit location's start to its end, e.g. nano 266281's
/// tracer 45502. <c>CreateGfxControl(id, hitLoc)</c> (<c>101002e0</c>) reads start and end once.
///
/// Fields (loader <c>100ffcac</c>): 0 flags (locator bits), 1-7 locator, 8 duration, 9 material,
/// 11 link size, 12 radius, 13-16 colour A,R,G,B (FISTP-packed once); 10 and 17-19 are read but not
/// used by this class.
///
/// Build (<c>100fff4c</c>): dir = end - start, a segment shorter than 0.01 readies the control; the
/// locator is a fixed matrix with rows cross(dir, p), dir, p, start (p = FindPerpendicular(dir))
/// under the template rotation and offset. Three GfxVisualCord4 (<c>100ffd67</c>, additive) with
/// twenty links each: size field 11, the packed colour, life 0.9.
///
/// Process (<c>100ffa7b</c>), per ribbon k: phase[k] += dt * 12.56, and one <c>rand()</c> in four flips
/// the twist step (both are statics shared by every Tracer4, starting at 0 / 2.07 / 4.15 and 0.314);
/// then, newest link first, with y from 0 in steps of 0.05 and angle a from phase[k] in steps of the
/// twist: r = (sin(y * pi * 20 / 19) + 0.2) * field 12, the link sits at (cos a * r, len * 1.1 * y,
/// sin a * r) from the locator (local mode) and, one <c>rand()</c> in four, is jittered in x and z by
/// up to r / 2. Slot 6 readies the control at once; slot 8 is a no-op.
/// </summary>
public sealed class Tracer4Sim
{
    public const int Ribbons = 3;
    public const int Links = 20;
    public const float LinkLife = 0.9f;
    public const float MinDistance = 0.01f;

    // Gamecode 102c622c (twist step) and 102c6230.. (phases): statics shared by every Tracer4.
    static float _step = 0.314f;
    static readonly float[] _phase = { 0f, 2.073451280593872f, 4.146902561187744f };

    readonly float[] _basis = new float[9];
    readonly bool _local;
    readonly float _radius;
    readonly float[][] _links = new float[Ribbons][];

    public bool Degenerate { get; }
    public float Distance { get; }
    public float LinkSize { get; }
    public uint Argb { get; }
    public float OriginX { get; }
    public float OriginY { get; }
    public float OriginZ { get; }
    public bool Local => _local;

    /// <summary>Locator rows 0..2 (each a local axis in world space).</summary>
    public float[] Basis => _basis;

    /// <summary>Link positions of ribbon <paramref name="k"/>, newest first, x/y/z each, in the visual's frame.</summary>
    public float[] LinkPositions(int k) => _links[k];

    public static float TwistStep => _step;
    public static float Phase(int k) => _phase[k];

    /// <summary>Puts the shared statics back to their load-time values. For tests.</summary>
    public static void ResetShared()
    {
        _step = 0.314f;
        _phase[0] = 0f;
        _phase[1] = 2.073451280593872f;
        _phase[2] = 4.146902561187744f;
    }

    public Tracer4Sim(float[] fields, float sx, float sy, float sz, float ex, float ey, float ez)
    {
        int flags = Int(fields, 0);
        _local = (flags & 2) != 0;
        LinkSize = F(fields, 11);
        _radius = F(fields, 12);
        Argb = PackRounded(F(fields, 13), F(fields, 14), F(fields, 15), F(fields, 16));
        for (int k = 0; k < Ribbons; k++)
            _links[k] = new float[Links * 3];

        float dx = ex - sx, dy = ey - sy, dz = ez - sz;
        float dist = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        Distance = dist;
        if (!(MinDistance <= dist))
        {
            Degenerate = true;
            return;
        }

        float inv = 1f / dist;
        dx *= inv;
        dy *= inv;
        dz *= inv;
        Tracer1Sim.FindPerpendicular(dx, dy, dz, out float px, out float py, out float pz);
        float tx = dy * pz - dz * py;
        float ty = dz * px - dx * pz;
        float tz = dx * py - dy * px;
        float kt = 1f / (float)Math.Sqrt(tx * tx + ty * ty + tz * tz);
        tx *= kt;
        ty *= kt;
        tz *= kt;

        // Rows cross(dir, p), dir, p.
        _basis[0] = tx; _basis[1] = ty; _basis[2] = tz;
        _basis[3] = dx; _basis[4] = dy; _basis[5] = dz;
        _basis[6] = px; _basis[7] = py; _basis[8] = pz;
        Tracer1Sim.ApplyLocatorRotation(fields, flags, _basis);

        float ox = sx, oy = sy, oz = sz;
        Tracer1Sim.ApplyLocatorOffset(fields, _basis, ref ox, ref oy, ref oz);
        OriginX = ox;
        OriginY = oy;
        OriginZ = oz;
    }

    /// <summary>One stock Process after the base timer.</summary>
    public void Step(float dt, Func<int> rand, Func<float> rand01)
    {
        if (Degenerate)
            return;

        // FUN_1010640a: zero in local mode, the locator position otherwise.
        float gx = _local ? 0f : OriginX;
        float gy = _local ? 0f : OriginY;
        float gz = _local ? 0f : OriginZ;

        for (int k = 0; k < Ribbons; k++)
        {
            float y = 0f;
            _phase[k] = (float)(dt * 12.5600004196167 + _phase[k]);
            if ((rand() & 3) == 0)
                _step = -_step;

            float a = _phase[k];
            float[] links = _links[k];
            for (int i = 0; i < Links; i++)
            {
                float t = (float)(y * 3.1415927410125732 * 20.0 / 19.0);
                float r = (float)(((float)Math.Sin(t) + 0.20000000298023224) * _radius);
                float x = (float)Math.Cos(a) * r;
                float h = (float)(Distance * 1.100000023841858 * y);
                float z = (float)Math.Sin(a) * r;

                float lx = gx + x, ly = gy + h, lz = gz + z;
                r = (float)(r * 0.5);
                if ((rand() & 3) == 0)
                {
                    float jx = (float)((rand01() * 2.0 - 1.0) * r);
                    float jz = (float)((rand01() * 2.0 - 1.0) * r);
                    lx += jx;
                    ly = (float)(ly + 0.0);
                    lz += jz;
                }

                links[i * 3] = lx;
                links[i * 3 + 1] = ly;
                links[i * 3 + 2] = lz;
                y = (float)(y + 0.05000000074505806);
                a = _step + a;
            }
        }
    }

    /// <summary>Per channel FISTP(c * 255 - 0.49999), packed A,R,G,B (<c>100fff63</c>).</summary>
    static uint PackRounded(float a, float r, float g, float b)
    {
        int ia = Fistp(a * 255.0f - 0.49999);
        int ir = Fistp(r * 255.0f - 0.49999);
        int ig = Fistp(g * 255.0f - 0.49999);
        int ib = Fistp(b * 255.0f - 0.49999);
        return unchecked((uint)(((ia << 8 | ir) << 8 | ig) << 8 | ib));
    }

    static int Fistp(double v) => (int)Math.Round(v, MidpointRounding.ToEven);

    static float F(float[] f, int i) => f != null && i < f.Length ? f[i] : 0f;

    static int Int(float[] f, int i) => f != null && i < f.Length ? GfxBits.Of(f, i) : 0;
}
