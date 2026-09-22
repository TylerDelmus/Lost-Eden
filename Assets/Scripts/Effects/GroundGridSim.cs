using System;

/// <summary>
/// Stock <c>GfxControlGroundGrid_t</c> (type 3030, 0xbd6; vftable <c>Gamecode 1016f5ac</c>, loader
/// <c>1010ebbd</c>, init <c>1010e951</c>, Process <c>1010e704</c>) and the DisplaySystem
/// <c>GfxVisualGroundGrid</c> it fills (Update <c>100164b4</c>, SetAlpha <c>1001692d</c>): an N x N grid of
/// vertices laid over the ground round the emitter, drawn with the material as one textured sheet.
///
/// Fields: 0 flags, 8 duration, 9 material, 10 N, 11 visual mode (0 plain; 1 diamond fall-off and 2 a
/// per-vertex table are not ported), 12/13 u/v scale, 14 fade-in, 15 fade-out, 16 colour, 18 spacing,
/// 19 height above the ground, 20 time rate, 21/22 u/v scale rates, 23/24 u/v offset, 25/26 their rates.
/// Flags: 0x800 lays the grid once (else every call, following the emitter), 0x1000 keeps it after the
/// locator is gone, 0x20000 a random turn (only used by grids laid every call), 0x200 additive.
///
/// Vertex (row i, column j): the point (j s - h, 0, i s - h), h = (N - 1) s / 2, taken through the turn,
/// sits at the ground under emitter + that point plus field 19, relative to the emitter. Its UV is
/// ((j + c) / (N - 1) * u scale + u offset, (i + c) / (N - 1) * v scale + v offset), c = -(N - 1) / 2 with
/// flag 0x8000 else 0. Alpha: age / fade-in while fading in, 1 - (age - (duration - fade-out)) / fade-out
/// once fading out, else 1; it goes to the visual as the texture factor.
///
/// No Unity dependency so the recovered maths can be asserted from plain unit tests.
/// </summary>
public sealed class GroundGridSim
{
    public const int FlagOnce = 0x800;
    public const int FlagKeep = 0x1000;
    public const int FlagCentredUv = 0x8000;
    public const int FlagAdditive = 0x200;

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

    public float UScale { get; private set; }
    public float VScale { get; private set; }
    public float UOffset { get; private set; }
    public float VOffset { get; private set; }
    public float Alpha { get; private set; } = 1f;

    public bool Supported => VisualMode == 0;
    public bool Additive => (Flags & FlagAdditive) != 0;

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
        // 1010ecb5: +0xd0 = duration - fade-out.
        _fadeOutStart = Duration - _fadeOut;
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

    static int Int(float[] f, int i) => f != null && i >= 0 && i < f.Length ? BitConverter.SingleToInt32Bits(f[i]) : 0;
}
