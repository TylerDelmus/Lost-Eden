using System;

/// <summary>
/// Stock <c>GfxControlVolGrid_t</c> (typeCode 3038, 0xbde; vftable <c>Gamecode 1016fbac</c>, loader
/// <c>10115f8c</c>, init <c>10115eb5</c>, Process <c>10116292</c>) with its DisplaySystem
/// <c>GfxVisualVolGrid</c> (ctor <c>1002f56d</c>, build <c>1002f86d</c>, edge fade <c>1002f69e</c>, draw
/// <c>100302ad</c>): a box of crossed textured slices, e.g. 72360-72363, the light pillars of the Blessing
/// nanos (269534 Blessing of the Ancient Form).
///
/// Fields: 0 flags, 8 duration, 9 material, 13 spin (rad/s about y), 14/15/16 the slice counts across x,
/// y and z, then five keyed curves over age / duration from field 17 on (<see cref="StockFloatCurve"/> /
/// <see cref="StockColorCurve"/>): height, bottom size, top size, bottom colour, top colour. Fields 10-12
/// are loaded and unused. Flags: 2 the locator's local frame, 0x400 each vertical slice maps the whole
/// texture (else its v is the slice's place), 0x800 the position follows the locator, 0x1000 a random
/// start angle, 0x2000 on the ground, 0x4000 slices fade as they turn edge-on to the camera.
///
/// Geometry, in the order stock builds it: for each of the x, z and y slices a fan of 5 vertices (centre
/// first, indices 0 1 2 3 4 1), position, D3DCOLOR and uv:
/// <list type="bullet">
/// <item>x slice i of a at x = (i / a) W - W / 2: centre (x, H / 2, 0) in the half-way colour, (x, 0, W / 2) and
/// (x, 0, -W / 2) in the bottom colour, (x, H, -D / 2) and (x, H, D / 2) in the top colour.</item>
/// <item>z slice i of c at z = (i / c) W - W / 2: centre (0, H / 2, z), (-W / 2, 0, z), (-D / 2, H, z),
/// (D / 2, H, z), (W / 2, 0, z), coloured the same way.</item>
/// <item>y slice j of b at y = (j / b) H, all in the colour j / b of the way up: centre (0, y, 0) and the
/// square (-W / 2, y, W / 2), (-W / 2, y, -W / 2), (W / 2, y, -W / 2), (W / 2, y, W / 2), always the whole
/// texture.</item>
/// </list>
/// W is the bottom size, D the top size, H the height. The texture scale and offset (+0x1e8..+0x1fc) stay
/// at their defaults 1, 1, 0, 0: nothing sets them.
///
/// No Unity dependency so the recovered maths can be asserted from plain unit tests.
/// </summary>
public sealed class VolGridSim
{
    public const int FlagLocalFrame = 2;
    public const int FlagWholeTexture = 0x400;
    public const int FlagFollow = 0x800;
    public const int FlagRandomAngle = 0x1000;
    public const int FlagGround = 0x2000;
    public const int FlagEdgeFade = 0x4000;

    public const int VerticesPerSlice = 5;

    /// <summary>1002f66d: the fan's six indices.</summary>
    public static readonly int[] FanIndices = { 0, 1, 2, 3, 4, 1 };

    readonly StockFloatCurve _height, _bottomSize, _topSize;
    readonly StockColorCurve _bottomColour, _topColour;

    public int Flags { get; }
    public float Duration { get; set; }
    public int Material { get; }
    public float Spin { get; }
    public int SlicesX { get; }
    public int SlicesY { get; }
    public int SlicesZ { get; }
    public int SliceCount => SlicesX + SlicesY + SlicesZ;

    // The visual's parameters (+0x1b8..+0x1c8), defaults from its ctor.
    public float Height { get; private set; } = 10f;
    public float BottomSize { get; private set; } = 4f;
    public float TopSize { get; private set; } = 4f;
    public uint BottomColour { get; private set; } = 0xffffffffu;
    public uint TopColour { get; private set; } = 0xffffffffu;

    public VolGridSim(float[] fields)
    {
        Flags = Int(fields, 0);
        Duration = F(fields, 8);
        Material = Int(fields, 9);
        Spin = F(fields, 13);
        SlicesX = Math.Max(0, Int(fields, 14));
        SlicesY = Math.Max(0, Int(fields, 15));
        SlicesZ = Math.Max(0, Int(fields, 16));
        int index = 17;
        _height = StockFloatCurve.Read(fields, ref index);
        _bottomSize = StockFloatCurve.Read(fields, ref index);
        _topSize = StockFloatCurve.Read(fields, ref index);
        _bottomColour = StockColorCurve.Read(fields, ref index);
        _topColour = StockColorCurve.Read(fields, ref index);
    }

    /// <summary>
    /// One Process call (<c>101162b5</c>..<c>101163dc</c>): false once age reaches the duration. Otherwise the
    /// curves at fmod(age, duration) / duration set the visual.
    /// </summary>
    public bool Step(float age)
    {
        if (Duration <= age)
            return false;

        // fmod stored as a float, then over the duration.
        float t = (float)((float)((double)age % Duration) / (double)Duration);
        Height = _height.Evaluate(t);
        BottomSize = _bottomSize.Evaluate(t);
        TopSize = _topSize.Evaluate(t);
        TopColour = _topColour.Evaluate(t);
        BottomColour = _bottomColour.Evaluate(t);
        return true;
    }

    /// <summary>
    /// 1002f86d: every slice's 5 vertices into <paramref name="positions"/> (xyz), <paramref name="colours"/>
    /// and <paramref name="uvs"/> (D3D, v down the image), sized <see cref="SliceCount"/> * 5.
    /// </summary>
    public void Build(float[] positions, uint[] colours, float[] uvs)
    {
        float w = BottomSize, d = TopSize, h = Height;
        float hw = (float)(w * 0.5), hd = (float)(d * 0.5);
        uint bottom = BottomColour, top = TopColour;
        uint mid = StockColorCurve.Interpolate(bottom, top, 0.5f);
        bool whole = (Flags & FlagWholeTexture) != 0;
        float halfH = (float)(h * 0.5);
        int v = 0;

        for (int i = 0; i < SlicesX; i++)
        {
            float f = (float)((double)i / SlicesX);
            float x = (float)((double)f * w - hw);
            Put(positions, colours, v + 0, x, halfH, 0f, mid);
            Put(positions, colours, v + 1, x, 0f, hw, bottom);
            Put(positions, colours, v + 2, x, 0f, -hw, bottom);
            Put(positions, colours, v + 3, x, h, -hd, top);
            Put(positions, colours, v + 4, x, h, hd, top);
            SideUvs(uvs, v, f, whole);
            v += VerticesPerSlice;
        }

        for (int i = 0; i < SlicesZ; i++)
        {
            float f = (float)((double)i / SlicesZ);
            float z = (float)((double)f * w - hw);
            Put(positions, colours, v + 0, 0f, halfH, z, mid);
            Put(positions, colours, v + 1, -hw, 0f, z, bottom);
            Put(positions, colours, v + 2, -hd, h, z, top);
            Put(positions, colours, v + 3, hd, h, z, top);
            Put(positions, colours, v + 4, hw, 0f, z, bottom);
            // The z slice runs its corners the other way round: u 0, 0, 1, 1 and v 0, 1, 1, 0.
            if (whole)
            {
                Uv(uvs, v + 0, 0.5f, 0.5f);
                Uv(uvs, v + 1, 0f, 0f);
                Uv(uvs, v + 2, 0f, 1f);
                Uv(uvs, v + 3, 1f, 1f);
                Uv(uvs, v + 4, 1f, 0f);
            }
            else
            {
                Uv(uvs, v + 0, 0.5f, f);
                Uv(uvs, v + 1, 0f, f);
                Uv(uvs, v + 2, 0f, f);
                Uv(uvs, v + 3, 1f, f);
                Uv(uvs, v + 4, 1f, f);
            }
            v += VerticesPerSlice;
        }

        for (int j = 0; j < SlicesY; j++)
        {
            float f = (float)((double)j / SlicesY);
            float y = (float)((double)h * f);
            uint c = StockColorCurve.Interpolate(bottom, top, f);
            Put(positions, colours, v + 0, 0f, y, 0f, c);
            Put(positions, colours, v + 1, -hw, y, hw, c);
            Put(positions, colours, v + 2, -hw, y, -hw, c);
            Put(positions, colours, v + 3, hw, y, -hw, c);
            Put(positions, colours, v + 4, hw, y, hw, c);
            Uv(uvs, v + 0, 0.5f, 0.5f);
            Uv(uvs, v + 1, 0f, 0f);
            Uv(uvs, v + 2, 0f, 1f);
            Uv(uvs, v + 3, 1f, 1f);
            Uv(uvs, v + 4, 1f, 0f);
            v += VerticesPerSlice;
        }
    }

    /// <summary>The x slice's uvs: corners (0, 0), (1, 0), (1, 1), (0, 1), or with v the slice's place.</summary>
    static void SideUvs(float[] uvs, int v, float f, bool whole)
    {
        if (whole)
        {
            Uv(uvs, v + 0, 0.5f, 0.5f);
            Uv(uvs, v + 1, 0f, 0f);
            Uv(uvs, v + 2, 1f, 0f);
            Uv(uvs, v + 3, 1f, 1f);
            Uv(uvs, v + 4, 0f, 1f);
        }
        else
        {
            Uv(uvs, v + 0, 0.5f, f);
            Uv(uvs, v + 1, 0f, f);
            Uv(uvs, v + 2, 1f, f);
            Uv(uvs, v + 3, 1f, f);
            Uv(uvs, v + 4, 0f, f);
        }
    }

    /// <summary>
    /// 1002f69e (flag 0x4000): each vertex's alpha of slice <paramref name="slice"/> times |n · dir|, n the
    /// slice's normal ((v1 - v0) x (v2 - v0), both edges normalised but the cross not, so a tall thin slice
    /// never gets back to full alpha) and <paramref name="dx"/>..<paramref name="dz"/>
    /// the unit direction between the camera and the grid in the grid's own frame. Untouched when either
    /// edge or the normal is zero, or the two edges are equal.
    /// </summary>
    public static void EdgeFade(float[] positions, uint[] colours, int slice, float dx, float dy, float dz)
    {
        int v0 = slice * VerticesPerSlice;
        if (!Edge(positions, v0 + 1, v0, out float ax, out float ay, out float az)
            || !Edge(positions, v0 + 2, v0, out float bx, out float by, out float bz))
            return;
        if (ax == bx && ay == by && az == bz)
            return;

        float nx = (float)((double)ay * bz - (double)az * by);
        float ny = (float)((double)az * bx - (double)ax * bz);
        float nz = (float)((double)ax * by - (double)ay * bx);
        if (nx == 0f && ny == 0f && nz == 0f)
            return;

        float dot = Math.Abs((float)((double)ny * dy + (double)nx * dx + (double)nz * dz));
        for (int k = 0; k < VerticesPerSlice; k++)
        {
            uint c = colours[v0 + k];
            int alpha = (int)((c >> 24) * (double)dot);
            colours[v0 + k] = (uint)(alpha << 24) | (c & 0xffffff);
        }
    }

    /// <summary>b - a, normalised (<c>100051f8</c>); false when it is zero.</summary>
    static bool Edge(float[] p, int b, int a, out float x, out float y, out float z)
    {
        x = p[b * 3] - p[a * 3];
        y = p[b * 3 + 1] - p[a * 3 + 1];
        z = p[b * 3 + 2] - p[a * 3 + 2];
        if (x == 0f && y == 0f && z == 0f)
            return false;
        float length = (float)Math.Sqrt((float)((double)y * y + (double)x * x + (double)z * z));
        float s = (float)(1.0 / length);
        x = (float)(x * (double)s);
        y = (float)(y * (double)s);
        z = (float)(z * (double)s);
        return true;
    }

    static void Put(float[] p, uint[] c, int v, float x, float y, float z, uint colour)
    {
        p[v * 3] = x;
        p[v * 3 + 1] = y;
        p[v * 3 + 2] = z;
        c[v] = colour;
    }

    static void Uv(float[] uvs, int v, float u, float w)
    {
        uvs[v * 2] = u;
        uvs[v * 2 + 1] = w;
    }

    static float F(float[] f, int i) => f != null && i >= 0 && i < f.Length ? f[i] : 0f;

    static int Int(float[] f, int i) => f != null && i >= 0 && i < f.Length ? BitConverter.SingleToInt32Bits(f[i]) : 0;
}
