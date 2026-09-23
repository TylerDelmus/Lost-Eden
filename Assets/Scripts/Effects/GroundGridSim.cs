using System;

/// <summary>
/// Stock <c>GfxControlGroundGrid_t</c> (type 3030, 0xbd6; vftable <c>Gamecode 1016f5ac</c>, loader
/// <c>1010ebbd</c>, init <c>1010e951</c>, Process <c>1010e704</c>) and the DisplaySystem
/// <c>GfxVisualGroundGrid</c> it fills (Update <c>100164b4</c>, SetAlpha <c>1001692d</c>): an N x N grid of
/// vertices laid over the ground round the emitter, drawn with the material as one textured sheet.
///
/// Fields: 0 flags, 8 duration, 9 material, 10 N, 11 visual mode, 12/13 u/v scale, 14 fade-in,
/// 15 fade-out, 16 colour, 18 spacing, 19 height above the ground, 20 time rate, 21/22 u/v scale rates,
/// 23/24 u/v offset, 25/26 their rates.
/// Flags: 0x800 lays the grid once (else every call, following the emitter), 0x1000 keeps it after the
/// locator is gone, 0x20000 a random turn (only used by grids laid every call), 0x200 additive,
/// 0x8000 centres the UVs, 0x4000 ripples the alpha and 0x2000 ripples the height.
///
/// Vertex (row i, column j): the point (j s - h, 0, i s - h), h = (N - 1) s / 2, taken through the turn,
/// sits at the ground under emitter + that point plus field 19, relative to the emitter. Its UV is
/// ((j + c) / (N - 1) * u scale + u offset, (i + c) / (N - 1) * v scale + v offset), c = -(N - 1) / 2 with
/// flag 0x8000 else 0. Alpha: age / fade-in while fading in, 1 - (age - (duration - fade-out)) / fade-out
/// once fading out, else 1; it goes to the visual as the texture factor.
///
/// The visual mode picks a per-vertex fall-off t, and every vertex is then shaded
/// <c>max(0, 1 - t) * the colour's own alpha</c>, times the ripple with flag 0x4000. Mode 0 leaves
/// t at 0, so the whole sheet is one flat colour; mode 1 is an L1 (diamond) distance from the centre
/// over N / 2, computed inline; mode 2 is the radial distance over (N - 1) / 2, which the visual's
/// constructor precomputes into a float[N * N] table. Stock really does normalise the two modes by
/// different halves. A vertex past the edge (t > 1) is clipped to nothing by the max.
///
/// No Unity dependency so the recovered maths can be asserted from plain unit tests.
/// </summary>
public sealed class GroundGridSim
{
    public const int FlagOnce = 0x800;
    public const int FlagKeep = 0x1000;
    public const int FlagCentredUv = 0x8000;
    public const int FlagAdditive = 0x200;
    public const int FlagAlphaRipple = 0x4000;
    public const int FlagHeightRipple = 0x2000;

    public readonly int Flags;
    public readonly float Duration;
    public readonly int Size;
    public readonly int VisualMode;
    public readonly float Spacing;
    public readonly float Height;
    public readonly uint Argb;
    readonly float _fadeIn;
    readonly float _fadeOut;
    readonly float _fadeOutStart;
    readonly float _uRate, _vRate, _uOffRate, _vOffRate;
    readonly float _timeRate;
    readonly float[] _radial;

    public float UScale { get; private set; }
    public float VScale { get; private set; }
    public float UOffset { get; private set; }
    public float VOffset { get; private set; }
    public float Alpha { get; private set; } = 1f;

    /// <summary>The ripple's running phase, the visual's <c>+0x1b8</c>; it starts at 0 (10016c97).</summary>
    public float Phase { get; private set; }

    public bool Supported => VisualMode is 0 or 1 or 2;
    public bool Additive => (Flags & FlagAdditive) != 0;

    /// <summary>
    /// 1010e8d0..1010e908: the control only calls the visual's Update, which is what rebuilds the
    /// whole vertex buffer, when at least one of the four UV rates is non-zero. A grid with none of
    /// them keeps the vertices its init built and never advances <see cref="Phase"/> — so its ripple
    /// stands still too. That is stock's own shortcut, not an optimisation of ours.
    /// </summary>
    public bool Animates => _uRate != 0f || _vRate != 0f || _uOffRate != 0f || _vOffRate != 0f;

    /// <summary>The colour's own alpha, 0..1 (100164c6..100164ea).</summary>
    public float ColourAlpha => ((Argb >> 24) & 0xff) / 255f;

    public GroundGridSim(float[] fields)
    {
        Flags = Int(fields, 0);
        Duration = F(fields, 8);
        Size = Math.Max(0, Math.Min(Int(fields, 10), 512));
        VisualMode = Int(fields, 11);
        UScale = F(fields, 12);
        VScale = F(fields, 13);
        _fadeIn = F(fields, 14);
        _fadeOut = F(fields, 15);
        Argb = unchecked((uint)Int(fields, 16));
        Spacing = F(fields, 18);
        Height = F(fields, 19);
        _uRate = F(fields, 21);
        _vRate = F(fields, 22);
        UOffset = F(fields, 23);
        VOffset = F(fields, 24);
        _uOffRate = F(fields, 25);
        _vOffRate = F(fields, 26);
        _timeRate = F(fields, 20);
        // 1010ecb5: +0xd0 = duration - fade-out.
        _fadeOutStart = Duration - _fadeOut;

        if (VisualMode == 2)
            _radial = BuildRadialTable(Size);
    }

    /// <summary>
    /// 10016d53..10016de7: the visual's constructor fills a <c>float[N * N]</c> with each vertex's
    /// radial distance from the centre over (N - 1) / 2, so the middle is 0 and the edge midpoints
    /// are 1. It depends on nothing but N, which is why mode 2 needs no data from the record.
    /// </summary>
    static float[] BuildRadialTable(int n)
    {
        var table = new float[n * n];
        double half = (n - 1) * 0.5;
        for (int i = 0; i < n; i++)
        {
            float dy = (float)(i - half);
            double dy2 = (double)dy * dy;
            for (int j = 0; j < n; j++)
            {
                float dx = (float)(j - half);
                // Stock rounds to a float at each store; keep the same steps so the table matches.
                float r = (float)((double)dx * dx + dy2);
                r = (float)Math.Sqrt(r);
                table[i * n + j] = (float)(r / half);
            }
        }
        return table;
    }

    /// <summary>Half the grid's width, <c>(N - 1) * spacing / 2</c>.</summary>
    public float HalfWidth => (float)((Size - 1) * Spacing * 0.5);

    /// <summary>1010e750..1010e78d, then the UV rates (1010e87f..1010e8b3).</summary>
    public void Advance(float age, float dt)
    {
        if (age < _fadeIn)
            Alpha = age / _fadeIn;
        else if (_fadeOutStart <= age)
            Alpha = 1f - (age - _fadeOutStart) / _fadeOut;
        else
            Alpha = 1f;

        UScale += _uRate * dt;
        VScale += _vRate * dt;
        UOffset += _uOffRate * dt;
        VOffset += _vOffRate * dt;
    }

    /// <summary>
    /// 1001690b..1001691a: the visual drives the ripple forward by the time rate times the step, at
    /// the end of the same Update that rebuilt the vertices — so it only ticks while
    /// <see cref="Animates"/>.
    /// </summary>
    public void AdvancePhase(float dt) => Phase += _timeRate * dt;

    /// <summary>The fall-off at row i, column j: 0 in the middle, 1 at the edge, more past it.</summary>
    public float Falloff(int i, int j)
    {
        switch (VisualMode)
        {
            // 100165cb / 1001674e: a lookup in the constructor's table.
            case 2:
                int k = i * Size + j;
                return _radial != null && (uint)k < (uint)_radial.Length ? _radial[k] : 0f;
            // 100165e2..10016611: an L1 distance, and over N / 2 rather than the table's (N - 1) / 2.
            case 1:
                float half = Size * 0.5f;
                return (Math.Abs(j - half) + Math.Abs(i - half)) / half;
            // 100167a6: mode 0 pushes a plain zero, so every vertex is at full strength.
            default:
                return 0f;
        }
    }

    /// <summary>
    /// 10016616..1001662e: <c>1 - t</c>, floored at zero. Stock's compare keeps the value when it is
    /// greater than, equal to or unordered with zero, so a NaN survives — <c>0f > d</c> matches that,
    /// where <c>Math.Max</c> would not.
    /// </summary>
    public static float Fade(float t)
    {
        float d = 1f - t;
        return 0f > d ? 0f : d;
    }

    /// <summary>
    /// 10016631..10016673: a half-wave of <c>sin((t * 32 + phase) / 2)</c> lifted into 0..1. The
    /// t term is what turns it into a ring travelling out from the middle.
    /// </summary>
    public float Ripple(float t) => ((float)Math.Sin((t * 32f + Phase) * 0.5f) + 1f) * 0.5f;

    /// <summary>The packed colour written to the vertex (10016687..100166f0).</summary>
    public uint VertexArgb(float t)
    {
        float a = Fade(t) * ColourAlpha;
        if ((Flags & FlagAlphaRipple) != 0)
            a *= Ripple(t);
        // 10085066 is ftol, which truncates; stock never clamps because none of the three factors
        // can exceed 1.
        return ((uint)(int)(a * 255f) << 24) | (Argb & 0x00ffffffu);
    }

    /// <summary>How far the vertex is lifted off the ground (1001669a; zero without flag 0x2000).</summary>
    public float RippleHeight(float t) => (Flags & FlagHeightRipple) != 0 ? 0.5f * Ripple(t) : 0f;

    /// <summary>The unturned grid point for row i, column j.</summary>
    public void Point(int i, int j, out float x, out float z)
    {
        float h = HalfWidth;
        x = j * Spacing - h;
        z = i * Spacing - h;
    }

    /// <summary><c>100166db</c>..<c>1001673f</c>.</summary>
    public void Uv(int i, int j, out float u, out float v)
    {
        double c = (Flags & FlagCentredUv) != 0 ? -(Size - 1) * 0.5 : 0.0;
        int n = Math.Max(1, Size - 1);
        u = (float)((j + c) / n * UScale + UOffset);
        v = (float)((i + c) / n * VScale + VOffset);
    }

    static float F(float[] f, int i) => f != null && i >= 0 && i < f.Length ? f[i] : 0f;

    static int Int(float[] f, int i) => f != null && i >= 0 && i < f.Length ? GfxBits.Of(f, i) : 0;
}
