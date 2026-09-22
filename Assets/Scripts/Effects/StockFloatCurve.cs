using System;

/// <summary>
/// Stock keyed float curve: the same Gamecode loader as <see cref="StockColorCurve"/> (<c>101166f2</c>)
/// with mode 0, so each key is (time, float); evaluated by <c>1011689d</c>. EffectMesh reads its alpha
/// curve with it from field 32.
///
/// Evaluate(t): with no keys, 1; with one, that key's value. Otherwise the first segment with
/// key[i].time &lt;= t &lt; key[i+1].time is blended linearly, (1 - f) * v[i] + f * v[i+1] with
/// f = (t - key[i].time) / (key[i+1].time - key[i].time) (a zero span counts as 1). A t outside every
/// segment, before the first key as well as after the last, takes the last key's value.
///
/// No Unity dependency so the recovered maths can be asserted from plain unit tests.
/// </summary>
public sealed class StockFloatCurve
{
    readonly float[] _times;
    readonly float[] _values;

    public int Count => _times.Length;

    public StockFloatCurve(float[] times, float[] values)
    {
        _times = times ?? Array.Empty<float>();
        _values = values ?? Array.Empty<float>();
    }

    /// <summary>
    /// Reads the curve starting at <paramref name="index"/> (the count), advancing it past the keys.
    /// </summary>
    public static StockFloatCurve Read(float[] fields, ref int index)
    {
        int count = Int(fields, index++);
        if (count < 0)
            count = 0;
        var times = new float[count];
        var values = new float[count];
        for (int k = 0; k < count; k++)
        {
            times[k] = F(fields, index++);
            values[k] = F(fields, index++);
        }
        return new StockFloatCurve(times, values);
    }

    public float Evaluate(float t)
    {
        int count = _times.Length;
        if (count == 0)
            return 1f;

        int last = count - 1;
        float result = _values[last];
        if (count <= 1)
            return result;

        for (int i = 0; i < last; i++)
        {
            if (_times[i] <= t && t < _times[i + 1])
            {
                float span = _times[i + 1] - _times[i];
                if (span == 0f)
                    span = 1f;
                float f = (t - _times[i]) / span;
                return (1f - f) * _values[i] + _values[i + 1] * f;
            }
        }
        return result;
    }

    static float F(float[] f, int i) => f != null && i >= 0 && i < f.Length ? f[i] : 0f;

    static int Int(float[] f, int i) => f != null && i >= 0 && i < f.Length ? BitConverter.SingleToInt32Bits(f[i]) : 0;
}
