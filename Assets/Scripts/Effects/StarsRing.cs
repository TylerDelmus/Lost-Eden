using System;

/// <summary>
/// Stock <c>_GfxControlStars_t</c> starTypes 7 and 8 (Process <c>FUN_100f8491</c>, shared case at
/// <c>100f9371</c>): 128 sprites pushed out from the locator along fixed directions in pulses. 45001,
/// nano 28638's hit, is type 7: a flat ring expanding to 20 m.
///
/// Directions (lazy init <c>FUN_100f7fb8</c>): type 7 (<c>100f81ea</c>) is a horizontal ring,
/// (sin a, 0, cos a) with a = 2*pi*j/128; type 8 (<c>100f8260</c>) spirals over a sphere, t = pi*j/128:
/// (sin 30t * sin t, cos t, cos 30t * sin t). Neither is turned by the locator.
///
/// Each Process: u = field31 * age / duration (field 26), frac = u - floor(u), s = sqrt(frac);
/// every sprite has size field28 * s + field29, sits at locator + direction * field30 * 0.01 * s,
/// and takes colour <see cref="StockColorRamp"/> over fields 18..25 at frac.
/// </summary>
public sealed class StarsRing
{
    public const int Count = 128;

    readonly float[] _dirs = new float[Count * 3];
    readonly float _sizeScale;
    readonly float _sizeOffset;
    readonly int _radius;
    readonly int _repeats;
    readonly float[] _start = new float[4];
    readonly float[] _end = new float[4];

    public float Frac { get; private set; }
    public float Size { get; private set; }
    public float Radius { get; private set; }
    public uint Argb { get; private set; }

    /// <summary>Unit directions, x/y/z per sprite.</summary>
    public float[] Directions => _dirs;

    public static bool Handles(int starType) => starType == 7 || starType == 8;

    public StarsRing(int starType, float[] fields)
    {
        _sizeScale = F(fields, 28);
        _sizeOffset = F(fields, 29);
        _radius = Int(fields, 30);
        _repeats = Int(fields, 31);
        for (int c = 0; c < 4; c++)
        {
            _start[c] = F(fields, 18 + c);
            _end[c] = F(fields, 22 + c);
        }

        for (int j = 0; j < Count; j++)
        {
            if (starType == 7)
            {
                float a = (float)(j * 6.2831854820251465 * 0.0078125);
                _dirs[j * 3] = (float)Math.Sin(a);
                _dirs[j * 3 + 1] = 0f;
                _dirs[j * 3 + 2] = (float)Math.Cos(a);
            }
            else
            {
                float t = (float)(j * 3.1415927410125732 * 0.0078125);
                float u = (float)(t * 30.0);
                float sinT = (float)Math.Sin(t);
                _dirs[j * 3] = (float)Math.Sin(u) * sinT;
                _dirs[j * 3 + 1] = (float)Math.Cos(t);
                _dirs[j * 3 + 2] = (float)Math.Cos(u) * sinT;
            }
        }
    }

    /// <summary>One stock Process at <paramref name="age"/> with the control's duration (field 26).</summary>
    public void Step(float age, float duration)
    {
        float u = (float)(_repeats * (double)age / duration);
        float whole = (float)Math.Floor((double)u);
        float frac = (float)((double)u - whole);
        float s = (float)Math.Sqrt(frac);

        Frac = frac;
        Size = (float)(_sizeScale * (double)s + _sizeOffset);
        Radius = (float)(_radius * (s * 0.01));
        Argb = StockColorRamp.Eval(_start, _end, frac);
    }

    static float F(float[] f, int i) => f != null && i < f.Length ? f[i] : 0f;

    static int Int(float[] f, int i) => f != null && i < f.Length ? BitConverter.SingleToInt32Bits(f[i]) : 0;
}
