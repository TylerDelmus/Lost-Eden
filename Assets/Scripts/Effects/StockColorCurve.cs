using System;

/// <summary>
/// Stock keyed colour curve (Gamecode load <c>101166f2</c> with mode 1, evaluate <c>1011678b</c>), used
/// by the particle controls (BParticle2's field 35 onward). The template holds a key count, then
/// (time, D3DCOLOR) pairs.
///
/// Evaluate(t): with no keys, 0xffffffff; with one, that key's colour. Otherwise the first segment with
/// key[i].time &lt;= t &lt; key[i+1].time is blended by randy31 <c>Color_t::Interpolate</c>
/// (<c>10019a7a</c>): w = (int)(f * 256), each byte (a * (256 - w) + b * w) &gt;&gt; 8, with
/// f = (t - key[i].time) / (key[i+1].time - key[i].time) (a zero span counts as 1). A t outside every
/// segment, before the first key as well as after the last, takes the last key's colour.
///
/// No Unity dependency so the recovered maths can be asserted from plain unit tests.
/// </summary>
public sealed class StockColorCurve
{
    readonly float[] _times;
    readonly uint[] _colours;

    public int Count => _times.Length;

    public StockColorCurve(float[] times, uint[] colours)
    {
        _times = times ?? Array.Empty<float>();
        _colours = colours ?? Array.Empty<uint>();
    }

    /// <summary>
    /// Reads the curve starting at <paramref name="index"/> (the count), advancing it past the keys, as
    /// <c>101166f2</c> does with the template's field reader.
    /// </summary>
    public static StockColorCurve Read(float[] fields, ref int index)
    {
        int count = Int(fields, index++);
        if (count < 0)
            count = 0;
        var times = new float[count];
        var colours = new uint[count];
        for (int k = 0; k < count; k++)
        {
            times[k] = F(fields, index++);
            colours[k] = unchecked((uint)Int(fields, index++));
        }
        return new StockColorCurve(times, colours);
    }

    public uint Evaluate(float t)
    {
        int count = _times.Length;
        if (count == 0)
            return 0xffffffffu;

        int last = count - 1;
        uint result = _colours[last];
        if (count <= 1)
            return result;

        int segment = last;
        for (int i = 0; i < last; i++)
        {
            if (!(_times[i] <= t))
                continue;
            if (t < _times[i + 1])
            {
                segment = i;
                break;
            }
        }
        if (segment >= last)
            return result;

        float span = _times[segment + 1] - _times[segment];
        if (span == 0f)
            span = 1f;
        float f = (t - _times[segment]) / span;
        return Interpolate(_colours[segment], _colours[segment + 1], f);
    }

    /// <summary>randy31 <c>Color_t::Interpolate</c> (<c>10019a7a</c>).</summary>
    public static uint Interpolate(uint a, uint b, float f)
    {
        int w = (int)(f * 256.0);
        int inv = 256 - w;
        uint result = 0;
        for (int shift = 0; shift < 32; shift += 8)
        {
            int ca = (int)((a >> shift) & 0xff);
            int cb = (int)((b >> shift) & 0xff);
            uint c = (uint)(((ca * inv + cb * w) >> 8) & 0xff);
            result |= c << shift;
        }
        return result;
    }

    static float F(float[] f, int i) => f != null && i >= 0 && i < f.Length ? f[i] : 0f;

    static int Int(float[] f, int i) => f != null && i >= 0 && i < f.Length ? BitConverter.SingleToInt32Bits(f[i]) : 0;
}
