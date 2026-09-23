using System;

/// <summary>
/// DisplaySystem's <c>GfxVisualCone</c> geometry (ctor <c>1000c196</c>, render <c>1000c0ec</c>,
/// rebuild <c>1000bd0a</c>): one hollow cone, drawn as a single triangle strip of
/// <c>2 * segments + 2</c> vertices through randy31's <c>RenderTriangleStrip</c> (<c>10089808</c>)
/// in D3DFVF_XYZ | DIFFUSE | TEX1 (0x142), stride 0x18.
///
/// Despite the name it is a <b>frustum</b>, not a point-topped cone: it runs from a ring of the
/// bottom radius at y = 0 in colour B to a ring of the top radius at y = height in colour A. Either
/// radius may be zero, which is what makes an actual cone.
///
/// The loop runs i = 0..segments <i>inclusive</i>, emitting a (bottom, top) pair each time, so the
/// last pair repeats the first and closes the ring — hence the 2n + 2.
///
/// The u and v offsets scroll: the control feeds its own accumulators into them every call, so the
/// texture crawls round and along the cone as the effect ages.
///
/// Nothing here depends on Unity.
/// </summary>
public sealed class ConeFrustum
{
    /// <summary><c>10089d30</c>: the ring is built over an exact two pi, not the rounded 6.28 the
    /// control fans its cones by.</summary>
    public const double FullTurn = 6.2831854820251465;

    /// <summary>Ctor defaults before the control's first Process (<c>1000c2da</c>).</summary>
    public const float DefaultHeight = 10f;

    /// <summary>Stock <c>+0x1ac</c>, field 11.</summary>
    public int Segments { get; }

    public int VertexCount => Segments < 0 ? 0 : Segments * 2 + 2;

    /// <summary>Stock <c>+0x1c0</c>, flags bit 8: u and v change roles.</summary>
    public bool SwapUv { get; }

    public readonly float[] X;
    public readonly float[] Y;
    public readonly float[] Z;
    public readonly float[] U;
    public readonly float[] V;
    public readonly uint[] Colors;

    readonly float[] _sin;
    readonly float[] _cos;

    public ConeFrustum(int segments, bool swapUv)
    {
        Segments = segments < 0 ? 0 : segments;
        SwapUv = swapUv;

        int n = VertexCount;
        X = new float[n];
        Y = new float[n];
        Z = new float[n];
        U = new float[n];
        V = new float[n];
        Colors = new uint[n];

        // 1000c292: stock fills two tables over one full turn, the last entry closing on the first.
        // Note +0x1d8 holds the COSINES and +0x1dc the sines, and the geometry takes x from the
        // first and z from the second — the opposite way round to Beam and Spiral2.
        _sin = new float[Segments + 1];
        _cos = new float[Segments + 1];
        for (int i = 0; i <= Segments; i++)
        {
            float a = Segments == 0 ? 0f : (float)(i * FullTurn / (double)Segments);
            _cos[i] = (float)Math.Cos(a);
            _sin[i] = (float)Math.Sin(a);
        }
    }

    /// <summary><c>1000bd0a</c>, one rebuild from what the control last wrote.</summary>
    public void Build(
        float bottomRadius, float topRadius, float height,
        uint colourBottom, uint colourTop,
        float uSpan, float vSpan, float uOffset, float vOffset)
    {
        if (Segments <= 0)
            return;

        // 1000bd25: a (bottom, top) pair per step, i running to segments inclusive.
        for (int i = 0; i <= Segments; i++)
        {
            int b = i * 2;
            int t = b + 1;

            X[b] = (float)(_cos[i] * (double)bottomRadius);
            Y[b] = 0f;
            Z[b] = (float)(_sin[i] * (double)bottomRadius);
            Colors[b] = colourBottom;

            X[t] = (float)(_cos[i] * (double)topRadius);
            Y[t] = height;
            Z[t] = (float)(_sin[i] * (double)topRadius);
            Colors[t] = colourTop;
        }

        if (!SwapUv)
        {
            // 1000be08: u runs round the ring over the whole span, v spans bottom to top.
            float step = (float)(uSpan / (double)Segments);
            float run = 0f;
            for (int i = 0; i <= Segments; i++)
            {
                int b = i * 2;
                U[b] = (float)(uOffset + (double)run);
                V[b] = vOffset;
                U[b + 1] = (float)(uOffset + (double)run);
                V[b + 1] = (float)(vOffset + (double)vSpan);
                run = (float)(step + (double)run);
            }
        }
        else
        {
            // 1000be96: the other way round — v runs the ring, u spans the height.
            float step = (float)(vSpan / (double)Segments);
            float run = 0f;
            for (int i = 0; i <= Segments; i++)
            {
                int b = i * 2;
                U[b] = uOffset;
                V[b] = (float)(run + (double)vOffset);
                U[b + 1] = (float)(uOffset + (double)uSpan);
                V[b + 1] = (float)(run + (double)vOffset);
                run = (float)(step + (double)run);
            }
        }
    }
}
