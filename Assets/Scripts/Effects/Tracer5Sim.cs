using System;

/// <summary>
/// Stock <c>_GfxControlTracer5_t</c> (typeCode 1025 / 0x401, vftable <c>Gamecode 1016e0a4</c>): one streak
/// that runs from a hit location's start to its end, e.g. nano 45889's tracer 45708.
/// <c>CreateGfxControl(id, hitLoc)</c> (<c>10100b82</c>) reads start and end once.
///
/// Fields (loader <c>101003f9</c>): 0 flags (locator bits), 1-7 locator, 8 duration, 9 material,
/// 10 speed, 11 streak length, 12 width, 13-16 colour A,R,G,B (FISTP-packed once), 17-19 read, unused.
///
/// Init (<c>1010077d</c>): dir = end - start; a segment shorter than 0.01 readies the control. The speed
/// is capped at 5 * length, so the flight takes at least 0.2 s. The locator is the same fixed matrix as
/// Tracer4's (rows cross(dir, p), dir, p, start; p = FindPerpendicular(dir)). Build (<c>101004b4</c>): one
/// GfxVisualCord4 (additive, life-v) of three links, width field 12, the packed colour, lives 0.001,
/// 0.001 and 1, life scale 1.
///
/// Process (<c>10100aa3</c> -> <c>1010065a</c>): tail = speed * age, head = field 11 + tail; head is capped
/// at the length, tail kept at 0 or more, and a tail at or past the length is the length and readies the
/// control. Newest link first, they sit at (0, head, 0), (0, tail, 0) and the locator's world-mode
/// position (zero in local mode), along the locator's y, i.e. the direction of flight. The two newest links
/// make the drawn quad; the third only turns its side. All 13 records are local mode (field 0 = 2).
/// </summary>
public sealed class Tracer5Sim
{
    public const int Links = 3;
    public const float MinDistance = 0.01f;

    /// <summary>Link lives, newest first (<c>101005fd</c>: the third link built gets 1).</summary>
    public static readonly float[] LinkLives = { 1f, 0.001f, 0.001f };

    readonly float[] _basis = new float[9];
    readonly bool _local;
    readonly float _length;
    readonly float[] _links = new float[Links * 3];

    public bool Degenerate { get; }
    public float Distance { get; }
    public float Speed { get; }
    public float Width { get; }
    public uint Argb { get; }
    public float OriginX { get; }
    public float OriginY { get; }
    public float OriginZ { get; }
    public bool Local => _local;
    public float Head { get; private set; }
    public float Tail { get; private set; }
    public bool Arrived { get; private set; }

    /// <summary>Locator rows 0..2 (each a local axis in world space).</summary>
    public float[] Basis => _basis;

    /// <summary>Link positions, newest first, x/y/z each, in the visual's frame.</summary>
    public float[] LinkPositions => _links;

    public Tracer5Sim(float[] fields, float sx, float sy, float sz, float ex, float ey, float ez)
    {
        int flags = Int(fields, 0);
        _local = (flags & 2) != 0;
        float speed = F(fields, 10);
        _length = F(fields, 11);
        Width = F(fields, 12);
        Argb = PackRounded(F(fields, 13), F(fields, 14), F(fields, 15), F(fields, 16));

        float dx = ex - sx, dy = ey - sy, dz = ez - sz;
        float dist = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        Distance = dist;
        if (dist < MinDistance)
        {
            Degenerate = true;
            Speed = speed;
            return;
        }

        // 10100887: at most five lengths a second.
        float cap = (float)(dist * 5.0);
        Speed = speed > cap ? cap : speed;

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

    /// <summary>One stock Process after the base timer, at <paramref name="age"/>.</summary>
    public void Step(float age)
    {
        if (Degenerate)
            return;

        float tail = Speed * age;
        float head = _length + tail;
        if (Distance < head)
            head = Distance;
        if (!(0f <= tail))
            tail = 0f;
        if (!(Distance > tail))
        {
            tail = Distance;
            Arrived = true;
        }
        Head = head;
        Tail = tail;

        // FUN_1010640a: zero in local mode, the locator position otherwise.
        float gx = _local ? 0f : OriginX;
        float gy = _local ? 0f : OriginY;
        float gz = _local ? 0f : OriginZ;
        Set(0, gx, gy + head, gz);
        Set(1, gx, gy + tail, gz);
        Set(2, gx, gy, gz);
    }

    void Set(int i, float x, float y, float z)
    {
        _links[i * 3] = x;
        _links[i * 3 + 1] = y;
        _links[i * 3 + 2] = z;
    }

    /// <summary>Per channel FISTP(c * 255 - 0.49999), packed A,R,G,B (<c>1010057b</c>).</summary>
    static uint PackRounded(float a, float r, float g, float b)
    {
        int ia = Fistp((float)(a * 255.0) - 0.49999);
        int ir = Fistp((float)(r * 255.0) - 0.49999);
        int ig = Fistp((float)(g * 255.0) - 0.49999);
        int ib = Fistp((float)(b * 255.0) - 0.49999);
        return unchecked((uint)(((ia << 8 | ir) << 8 | ig) << 8 | ib));
    }

    static int Fistp(double v) => (int)Math.Round(v, MidpointRounding.ToEven);

    static float F(float[] f, int i) => f != null && i >= 0 && i < f.Length ? f[i] : 0f;

    static int Int(float[] f, int i) => f != null && i >= 0 && i < f.Length ? BitConverter.SingleToInt32Bits(f[i]) : 0;
}
