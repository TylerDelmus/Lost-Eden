using System;

/// <summary>
/// Stock <c>_GfxControlPlasma_t</c> (typeCode 2002 / 0x7d2, vftable <c>Gamecode 1016d7e4</c>) and its
/// visual, DisplaySystem <c>GfxVisualPlasma</c> (ctor <c>1001b8f7</c>, render <c>1001bbf2</c>): a
/// wavy energy strip from a hit location's start to its end. Nano projectiles such as 28638's
/// tracer 17600 use it.
///
/// Control (ctor <c>100ec5ae</c>, loader <c>100ec4c8</c>, Process <c>100ec2e6</c>):
///   field 0 flags, 9 material, 10-13 start A,R,G,B, 14-17 end A,R,G,B, 18 duration (field 8 is
///   read and then overwritten by it). Every Process re-reads the hit location, so both ends follow
///   the caster's hand and the target: the visual sits at the start, its extent is end - start, its
///   clock is the age and its colour the start-to-end ramp at age / duration (<c>FUN_10108663</c>).
///   A hit location that has gone away readies the control. Slot 6 TerminateGracefully sets the
///   duration to 0, slot 8 SetDuration sets it, slots 11/12 replace the start/end colour.
///
/// Visual: 75 segments, so 76 centre points from the start along the extent, two vertices each,
/// drawn as one triangle strip (FVF 0x142: position, colour, one UV) with SRCALPHA/ONE, no culling
/// and no depth writes. Each centre point is pushed sideways by the sum of four cubed sine waves;
/// the sideways axis is cross(q, step) set to length 0.1, where q is the point one unit in front of
/// the camera in the visual's frame. Before building, a <c>rand()</c> with bits 0x7c0 clear nudges the
/// phase of wave 0 or 2 by half a radian.
/// </summary>
public sealed class PlasmaSim
{
    public const int Segments = 75;
    public const int VertexCount = 2 * Segments + 2;
    public const float HalfWidth = 0.1f;

    // GfxVisualPlasma ctor constants, per wave: amplitude (+0x1e0), wavelength (+0x1f0, stored as
    // 6.28 / wavelength) and scroll speed (+0x200). Phases (+0x210) start at zero.
    static readonly float[] Amplitude = { 0.15f, 0.3f, 0.2f, 0.1f };
    static readonly float[] Wavelength = { 0.2f, 0.3f, 0.4f, 0.5f };
    static readonly float[] Speed = { -0.8f, 1f, 1.2f, 0.7f };
    const double TwoPi = 6.28000020980835;

    readonly float[] _frequency = new float[4];
    readonly float[] _phase = new float[4];
    readonly float[] _start = new float[4];
    readonly float[] _end = new float[4];

    /// <summary>Control +0x10: field 18, or what SetDuration / TerminateGracefully set.</summary>
    public float Duration { get; set; }

    public PlasmaSim(float[] fields)
    {
        for (int k = 0; k < 4; k++)
            _frequency[k] = (float)(TwoPi / Wavelength[k]);

        Duration = F(fields, 18);
        for (int c = 0; c < 4; c++)
        {
            _start[c] = F(fields, 10 + c);
            _end[c] = F(fields, 14 + c);
        }
    }

    public float Phase(int wave) => _phase[wave];

    /// <summary>Slot 11: start colour A,R,G,B.</summary>
    public void SetStartColor(float a, float r, float g, float b)
    {
        _start[0] = a; _start[1] = r; _start[2] = g; _start[3] = b;
    }

    /// <summary>Slot 12: end colour A,R,G,B.</summary>
    public void SetStopColor(float a, float r, float g, float b)
    {
        _end[0] = a; _end[1] = r; _end[2] = g; _end[3] = b;
    }

    /// <summary>The colour at <paramref name="age"/>: <see cref="StockColorRamp"/> at age / duration.</summary>
    public uint ColorAt(float age) => StockColorRamp.Eval(_start, _end, age / Duration);

    /// <summary>
    /// One render of the visual (<c>1001bbf2</c>). <paramref name="ex"/>.. is the extent (end - start),
    /// <paramref name="qx"/>.. the point one unit in front of the camera, both relative to the start.
    /// Writes <see cref="VertexCount"/> positions (relative to the start) and D3D texture coordinates
    /// (tu, tv), in strip order: A0, B0, A1, B1, ...
    /// </summary>
    public void BuildStrip(
        float time,
        float ex, float ey, float ez,
        float qx, float qy, float qz,
        Func<int> rand,
        float[] positions,
        float[] uvs)
    {
        float invN = 1f / Segments;
        float sx = ex * invN, sy = ey * invN, sz = ez * invN;
        float len = (float)Math.Sqrt(sx * sx + sy * sy + sz * sz);
        if (len == 0f)
        {
            sx = 0.01f;
            sy = 0f;
            sz = 0f;
        }

        // side = cross(q, step), then SetLength(0.1).
        float wx = qy * sz - qz * sy;
        float wy = qz * sx - qx * sz;
        float wz = qx * sy - qy * sx;
        float k = HalfWidth * (1f / (float)Math.Sqrt(wx * wx + wy * wy + wz * wz));
        wx *= k;
        wy *= k;
        wz *= k;

        int r = rand();
        if ((r & 0x7c0) == 0)
        {
            int wave = (r & 2) != 0 ? 2 : 0;
            _phase[wave] = (float)(_phase[wave] + ((r & ~3) != 0 ? 1 : 0) * 0.5);
        }

        float cx = 0f, cy = 0f, cz = 0f;
        for (int i = 0; i <= Segments; i++)
        {
            float acc = 0f;
            double along = i * (double)len;
            for (int w = 0; w < 4; w++)
            {
                float x = (float)((Speed[w] * (double)time + along) * _frequency[w] + _phase[w]);
                float s = (float)Math.Sin(x);
                float cube = (float)((double)s * s * s);
                acc = (float)(cube * (double)Amplitude[w] + acc);
            }

            float mx = cx + wx * acc, my = cy + wy * acc, mz = cz + wz * acc;
            double tex = acc * 0.1;
            int a = i * 2, b = a + 1;
            Set(positions, a, mx + wx, my + wy, mz + wz);
            Set(positions, b, mx - wx, my - wy, mz - wz);
            uvs[a * 2] = (float)tex;
            uvs[a * 2 + 1] = 0f;
            uvs[b * 2] = 1f;
            uvs[b * 2 + 1] = (float)(1.0 - tex);

            cx += sx;
            cy += sy;
            cz += sz;
        }
    }

    static void Set(float[] p, int i, float x, float y, float z)
    {
        p[i * 3] = x;
        p[i * 3 + 1] = y;
        p[i * 3 + 2] = z;
    }

    static float F(float[] f, int i) => f != null && i < f.Length ? f[i] : 0f;
}
