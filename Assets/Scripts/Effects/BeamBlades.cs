using System;

/// <summary>
/// DisplaySystem's <c>GfxVisualBeam</c> geometry (ctor <c>10007ef4</c>, vftables <c>1008a014</c> /
/// <c>1008a024</c>, render <c>100087c1</c>, rebuild <c>10008056</c>): the beam is a star of flat
/// blades standing on the origin, each one drawn as a four-vertex triangle strip through randy31's
/// <c>RenderTriangleStrip</c> (<c>10089808</c>) in D3DFVF_XYZ | DIFFUSE | TEX1 (0x142), stride 0x18.
///
/// The blades are laid out over half a turn — the angle starts at 45 degrees and steps by
/// <c>180 / count</c> while it stays under 225 — and each one is a quad through the axis, so it shows
/// two opposite sides at once. It runs from the foot radius at y = 0 in colour 1 to the tip radius at
/// y = length in colour 2, with u across it and v along it.
///
/// The rebuild only runs on a call where the control has set the dirty byte (<c>+0x24c</c>), which it
/// does every time it writes any of the five values, so in practice that is every call.
///
/// A record with a second material gets one more quad (<c>1000855f</c>): a flat square of half-size
/// <b>foot</b> lying in the xz plane at <c>field 40 · length</c>, all four corners in colour 1, drawn
/// in a second pass. A glare disc somewhere along the beam.
///
/// Nothing here depends on Unity; <see cref="GfxControlBeam"/> turns these into strips and applies
/// the camera fade, which is the one part that needs to know where the camera is.
/// </summary>
public sealed class BeamBlades
{
    /// <summary><c>10008080</c>: the first blade's angle, degrees.</summary>
    public const float FirstAngleDegrees = 45f;

    /// <summary><c>1008a080</c>: blades are emitted while the angle stays under this.</summary>
    public const float LastAngleDegrees = 225f;

    /// <summary><c>10008070</c>: the blades share this much turn between them.</summary>
    public const float SpanDegrees = 180f;

    /// <summary><c>10007f50</c> from ctor argument 6, <c>(flags &gt;&gt; 8) &amp; 1</c>: u and v swap.</summary>
    public const int FlagSwapUv = 0x100;

    /// <summary><c>100082fb</c>: v runs the other way along the beam.</summary>
    public const int FlagFlipV = 0x1000;

    /// <summary><c>10007ff2</c>: the v offset is picked at random once, at construction.</summary>
    public const int FlagRandomV = 0x8000;

    /// <summary><c>1000839d</c>: blades seen edge-on fade out.</summary>
    public const int FlagCameraFade = 0x10000;

    /// <summary><c>100080ac</c>: stock turns the blade angle from degrees with these two constants.</summary>
    public const double QuarterPi = 0.7853981633974483;

    readonly int _flags;
    readonly float _uMax;
    readonly float _vSpan;

    /// <summary>Stock <c>+0x250</c>: a random v offset with flag 0x8000, otherwise zero.</summary>
    public float VOffset { get; }

    /// <summary>Stock <c>+0x1b0</c>, field 14.</summary>
    public int BladeCount { get; }

    public int VertexCount => BladeCount * 4;

    public readonly float[] X;
    public readonly float[] Y;
    public readonly float[] Z;
    public readonly float[] U;
    public readonly float[] V;
    public readonly uint[] Colors;

    /// <summary>The second material's quad, four vertices; empty when the record has no second material.</summary>
    public readonly float[] CapX = new float[4];
    public readonly float[] CapY = new float[4];
    public readonly float[] CapZ = new float[4];
    public readonly float[] CapU = { 1f, 0f, 1f, 0f };
    public readonly float[] CapV = { 1f, 1f, 0f, 0f };
    public readonly uint[] CapColors = new uint[4];

    /// <summary>Field 40: where along the beam the second material's quad sits.</summary>
    public float SecondQuadHeight { get; }

    public BeamBlades(
        int flags, int bladeCount, float uMax, float vSpan, float secondQuadHeight,
        Func<float> random = null)
    {
        _flags = flags;
        _uMax = uMax;
        _vSpan = vSpan;
        SecondQuadHeight = secondQuadHeight;
        BladeCount = bladeCount < 0 ? 0 : bladeCount;

        // 10007ff2: only the records that ask for it start somewhere random in the texture.
        VOffset = (flags & FlagRandomV) != 0 && random != null ? random() : 0f;

        int n = VertexCount;
        X = new float[n];
        Y = new float[n];
        Z = new float[n];
        U = new float[n];
        V = new float[n];
        Colors = new uint[n];
    }

    /// <summary>
    /// <c>10008056</c>, one rebuild from the five values the control last wrote.
    /// </summary>
    public void Build(float length, float foot, float tip, uint colour1, uint colour2)
    {
        if (BladeCount <= 0)
            return;

        float nearV = (float)(VOffset + (double)_vSpan);
        float farV = VOffset;
        if ((_flags & FlagFlipV) != 0)
        {
            // 100082fb: the two v values change ends.
            nearV = VOffset;
            farV = (float)(VOffset + (double)_vSpan);
        }

        // 10008070: fild the count, then 180 / it.
        float step = (float)(SpanDegrees / (double)BladeCount);
        float angle = FirstAngleDegrees;

        int v = 0;
        while (angle < LastAngleDegrees && v + 4 <= X.Length)
        {
            // 100080a6 / 10008158: both angles go to doubles before sin and cos.
            double near = angle * QuarterPi / 45.0;
            double far = (angle + SpanDegrees) * QuarterPi / 45.0;
            float sinA = (float)Math.Sin(near);
            float cosA = (float)Math.Cos(near);
            float sinB = (float)Math.Sin(far);
            float cosB = (float)Math.Cos(far);

            Set(v + 0, (float)(sinA * (double)foot), 0f, (float)(cosA * (double)foot), _uMax, nearV, colour1);
            Set(v + 1, (float)(sinB * (double)foot), 0f, (float)(cosB * (double)foot), 0f, nearV, colour1);
            Set(v + 2, (float)(sinA * (double)tip), length, (float)(cosA * (double)tip), _uMax, farV, colour2);
            Set(v + 3, (float)(tip * (double)sinB), length, (float)(tip * (double)cosB), 0f, farV, colour2);

            if ((_flags & FlagSwapUv) != 0)
            {
                // 10008362: u and v change places on all four.
                for (int i = v; i < v + 4; i++)
                {
                    float u = U[i];
                    U[i] = V[i];
                    V[i] = u;
                }
            }

            v += 4;
            angle = (float)(angle + (double)step);
        }

        // 1000855f: the second material's quad, half-size foot, at field 40 of the way up.
        float capY = (float)(SecondQuadHeight * (double)length);
        CapX[0] = foot; CapY[0] = capY; CapZ[0] = foot;
        CapX[1] = -foot; CapY[1] = capY; CapZ[1] = foot;
        CapX[2] = foot; CapY[2] = capY; CapZ[2] = -foot;
        CapX[3] = -foot; CapY[3] = capY; CapZ[3] = -foot;
        for (int i = 0; i < 4; i++)
            CapColors[i] = colour1;
    }

    void Set(int i, float x, float y, float z, float u, float v, uint colour)
    {
        X[i] = x;
        Y[i] = y;
        Z[i] = z;
        U[i] = u;
        V[i] = v;
        Colors[i] = colour;
    }

    /// <summary>
    /// <c>1000850f</c>: the camera fade scales a vertex's alpha and truncates (<c>10085066</c> is
    /// MSVC's round-then-correct-toward-zero helper, so it is a plain cast).
    /// </summary>
    public static uint ScaleAlpha(uint argb, float factor)
    {
        uint alpha = argb >> 24;
        int faded = (int)(alpha * (double)factor);
        if (faded < 0)
            faded = 0;
        if (faded > 255)
            faded = 255;
        return (argb & 0x00ffffffu) | ((uint)faded << 24);
    }
}
