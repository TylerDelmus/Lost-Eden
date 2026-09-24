using System;

/// <summary>
/// Stock <c>_GfxControlSpiral_t</c> (typeCode 2001, 0x7d1; vftable <c>Gamecode 1016dca4</c>, ctor
/// <c>100f53a8</c>, loader <c>100f4ef1</c>, init <c>100f4f45</c>, Process <c>100f4cb5</c>): two DisplaySystem
/// <c>GfxVisualSpiral</c> ribbons (<see cref="SpiralRibbon"/>) half a turn apart, a double helix that winds
/// up round the locator, spinning, then unwinds from its base. Nothing here depends on Unity.
///
/// Fields: 0 flags (the locator's), 1-6 the locator offset and turn, 7 attach, 8 duration, 9 material,
/// 10-17 a colour ramp that nothing reads (slots 11 / 12 set it; slot 13 sets both ends to one colour).
///
/// Per call (<see cref="Step"/>), with p = 2 age / duration: each ribbon draws [0, p] while p &lt; 1, then
/// [p - 1, 1] while p &lt; 2, and keeps its last range after. Both sit at the locator's world-mode position
/// (<c>1010640a</c>), turned about the world y by age * 3.7 (<c>100520d9</c>), and take the age for their
/// texture scroll. A lost locator sets duration = age; slot 6 does the same.
/// </summary>
public sealed class SpiralSim
{
    public const int RibbonCount = 2;

    /// <summary><c>100f4d33</c>: radians per second about the world y.</summary>
    public const double SpinRate = 3.700000047683716;

    /// <summary><c>100f4f9e</c>: the second GfxVisualSpiral ctor argument, a full turn.</summary>
    public const float FullTurn = 6.28000020980835f;

    /// <summary>Stock +0x10: field 8, or what SetDuration / TerminateGracefully put there.</summary>
    public float Duration { get; set; }

    /// <summary>The ribbons' +0x1cc / +0x1d0, as the last <see cref="Step"/> left them (0 and 1 from the ctor).</summary>
    public float Start { get; private set; }
    public float End { get; private set; } = 1f;

    /// <summary>The turn about the world y, radians.</summary>
    public float Spin { get; private set; }

    public SpiralSim(float duration) => Duration = duration;

    /// <summary><c>100f4f9e</c>: ribbon <paramref name="i"/> starts at i · 6.28 / 2 radians.</summary>
    public static float RibbonOffset(int i) => (float)(i * 6.28000020980835 / RibbonCount);

    /// <summary>Slot 6 (<c>100f4de3</c>): duration = age.</summary>
    public void TerminateGracefully(float age) => Duration = age;

    /// <summary>One Process body after the base timer, at <paramref name="age"/>.</summary>
    public void Step(float age)
    {
        float p = (float)((age + (double)age) / Duration);
        if (p < 1f)
        {
            Start = 0f;
            End = p;
        }
        else if (p < 2f)
        {
            Start = (float)(p - 1.0);
            End = 1f;
        }

        Spin = (float)(age * SpinRate);
    }

    /// <summary>
    /// <c>100520d9</c>: the half angle of the turn, wrapped into [0, π) when the angle leaves [0, 2π).
    /// The quaternion is (axis · sin(half), cos(half)).
    /// </summary>
    public static float HalfAngle(float angle)
    {
        if (0f <= angle && angle < 6.283185307179586)
            return (float)(angle * 0.5);
        double turns = (float)(angle / 6.2831854820251465);
        float whole = (float)Math.Floor(turns);
        return (float)((float)(turns - whole) * 3.1415927410125732);
    }
}

/// <summary>
/// One DisplaySystem <c>GfxVisualSpiral</c> (ctor <c>10021eb4</c>, draw <c>10021924</c>), built by
/// Spiral with (material, offset, 6.28, 12): a ribbon of 12 segments round a full turn of radius 0.6,
/// climbing 0.3 per radian, 0.3 tall. Vertices come in pairs, top then bottom: (cos a · 0.6, 0.3 h,
/// sin a · 0.6) and 0.3 lower, with a = offset + h the angle along the turn. u runs along the ribbon,
/// 0.6 · 6.28 / 1.5 over it, starting at -age (so it scrolls); v is 1 on the top edge and 0 on the bottom
/// (D3D's, running down the image).
///
/// The draw keeps [start, end] of it: a start above 0 (or above 1, which stock resets to 0) moves the
/// first pair part way to the next one, an end below 1 moves the last kept pair part way back, and the
/// first and last pairs of what is kept get alpha 0, so the ends fade. The rest are the visual's colour,
/// white. The visual is additive, not lit, drawn from both sides.
/// </summary>
public sealed class SpiralRibbon
{
    public const int Segments = 12;
    public const int VertexCount = 2 * Segments + 2;

    // 10021ed5..10021f22: the visual's constants.
    public const float Radius = 0.6f;
    public const float Rise = 0.3f;
    public const float Height = 0.3f;
    public const float TextureLength = 1.5f;
    public const float ScrollRate = 1f;

    readonly float _turn;
    readonly float[] _cos = new float[Segments + 1];
    readonly float[] _sin = new float[Segments + 1];

    public readonly float[] X = new float[VertexCount];
    public readonly float[] Y = new float[VertexCount];
    public readonly float[] Z = new float[VertexCount];
    public readonly float[] U = new float[VertexCount];
    public readonly float[] V = new float[VertexCount];
    public readonly float[] Alpha = new float[VertexCount];

    /// <summary>The kept vertices after the last <see cref="Build"/>: [First, End).</summary>
    public int First { get; private set; }
    public int End { get; private set; }

    /// <summary>The ctor (<c>10021fc4</c>): cos and sin of offset + i · turn / 12 for i = 0..12.</summary>
    public SpiralRibbon(float offset, float turn = SpiralSim.FullTurn)
    {
        _turn = turn;
        float step = (float)(turn / (double)Segments);
        float angle = offset;
        for (int i = 0; i <= Segments; i++)
        {
            _cos[i] = (float)Math.Cos(angle);
            _sin[i] = (float)Math.Sin(angle);
            angle = (float)(step + (double)angle);
        }
    }

    /// <summary>The draw (<c>10021924</c>) at <paramref name="age"/> with the range [start, end].</summary>
    public void Build(float age, float start, float end)
    {
        // 10021942: start out of 0..1 becomes 0; end below start becomes start, above 1 becomes 1.
        if (!(0f <= start) || 1f < start)
            start = 0f;
        if (start > end)
            end = start;
        else if (1f < end)
            end = 1f;

        float t = (float)(-age * ScrollRate);
        float du = (float)(_turn * (double)Radius / TextureLength / Segments);
        for (int i = 0; i <= Segments; i++)
        {
            float h = (float)(i * (double)_turn / Segments);
            float x = (float)(_cos[i] * (double)Radius);
            float z = (float)(_sin[i] * (double)Radius);
            int a = 2 * i, b = a + 1;
            X[a] = x; Y[a] = (float)(Rise * (double)h); Z[a] = z; U[a] = t; V[a] = 1f;
            X[b] = x; Y[b] = (float)(Rise * (double)h - Height); Z[b] = z; U[b] = t; V[b] = 0f;
            Alpha[a] = 1f;
            Alpha[b] = 1f;
            t = (float)(du + (double)t);
        }

        First = 0;
        End = VertexCount;

        if (0f < start)
        {
            // 10021b2b.
            float f = (float)(Segments * (double)start);
            float whole = (float)Math.Floor(f);
            int k = (int)whole;
            float frac = (float)(f - (double)whole);
            float keep = (float)(1.0 - frac);
            First = 2 * k;
            Blend(First, First + 2, frac, keep);
            Blend(First + 1, First + 3, frac, keep);
        }

        if (end < 1f)
        {
            // 10021caa.
            float f = (float)(Segments * (double)end);
            float up = (float)Math.Ceiling(f);
            int k = (int)up;
            float frac = (float)(up - (double)f);
            float keep = (float)(1.0 - frac);
            if (k < 1)
                k = 1;
            Blend(2 * k, 2 * k - 2, frac, keep);
            Blend(2 * k + 1, 2 * k - 1, frac, keep);
            End = 2 * k + 2;
        }

        // 10021dfd: the first and last pairs fade out.
        Alpha[First] = 0f;
        Alpha[First + 1] = 0f;
        Alpha[End - 2] = 0f;
        Alpha[End - 1] = 0f;
    }

    /// <summary>Vertex <paramref name="to"/> = <paramref name="from"/> · frac + itself · keep, position and uv.</summary>
    void Blend(int to, int from, float frac, float keep)
    {
        X[to] = (float)(X[from] * frac) + (float)(X[to] * keep);
        Y[to] = (float)(Y[from] * frac) + (float)(Y[to] * keep);
        Z[to] = (float)(Z[from] * frac) + (float)(Z[to] * keep);
        U[to] = (float)(U[from] * (double)frac + keep * (double)U[to]);
        V[to] = (float)(V[from] * (double)frac + keep * (double)V[to]);
    }
}
