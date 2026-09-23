using System;

/// <summary>
/// Stock <c>GfxControlGroundShake_t</c> (type 3032, 0xbd8; vftable <c>Gamecode 1016f604</c>, loader
/// <c>1010f015</c>, Process <c>1010f12a</c>): shakes the camera, nothing else. It writes a random
/// offset into <c>n3Camera_t</c> +0x1d4 and calls <c>n3Camera_t::UpdateTargetEye</c>, with the camera
/// reached through <c>n3EngineClient_t::GetInstance()</c> +0x7c.
///
/// Fields: 0 flags, 8 duration, 9/10/11 unused by the loader — Process overwrites them every call with
/// the locator's position (+0x38..+0x40), so they are scratch, not settings — 12 the range, and 13..
/// a <see cref="StockFloatCurve"/> read over the life. Every record leaves 9, 10 and 11 at 0.
///
/// Per call (<c>1010f15d</c>): the control is ready once <c>duration &lt;= age</c>. Otherwise
/// <c>t = age / duration</c>, <c>d</c> is the distance from the locator to the camera, clamped to the
/// range, and the amount is
/// <c>curve(t) * (range - d) / range</c> — full strength at the epicentre, nothing at the range.
/// The offset is then <c>((2r - 1) * amount, (2r - 1) * amount, (2r - 1) * amount)</c>, a fresh draw
/// per axis per call.
///
/// No Unity dependency so the recovered maths can be asserted from plain unit tests.
/// </summary>
public sealed class GroundShakeSim
{
    public readonly int Flags;
    public readonly float Duration;

    /// <summary>Field 12: past this distance the shake is nothing.</summary>
    public readonly float Range;

    readonly StockFloatCurve _curve;
    readonly Func<float> _random;

    public StockFloatCurve Curve => _curve;

    public GroundShakeSim(float[] fields, Func<float> random)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
        Flags = Int(fields, 0);
        Duration = F(fields, 8);
        Range = F(fields, 12);
        int index = 13;
        _curve = StockFloatCurve.Read(fields, ref index);
    }

    /// <summary><c>1010f15d</c>: the control ends once the duration is reached.</summary>
    public bool Expired(float age) => !(age < Duration);

    /// <summary>
    /// The shake amount at <paramref name="age"/> for a camera <paramref name="distance"/> away
    /// (<c>1010f1cf</c>..<c>1010f1ff</c>).
    /// </summary>
    public float Amount(float age, float distance)
    {
        if (Range <= 0f)
            return 0f;

        float t = Duration != 0f ? age / Duration : 0f;
        float envelope = _curve.Evaluate(t);

        // 1010f1d7: a camera past the range is pinned to it, so the falloff bottoms out at zero.
        float d = Range < distance ? Range : distance;
        return (Range - d) / Range * envelope;
    }

    /// <summary>
    /// The offset for this call: three fresh draws, each <c>(2r - 1) * amount</c>
    /// (<c>1010f202</c>..<c>1010f248</c>).
    /// </summary>
    public void Offset(float amount, out float x, out float y, out float z)
    {
        x = Draw() * amount;
        y = Draw() * amount;
        z = Draw() * amount;
    }

    float Draw() => (float)(_random() * 2.0 - 1.0);

    static float F(float[] f, int i) => f != null && i >= 0 && i < f.Length ? f[i] : 0f;

    static int Int(float[] f, int i) => f != null && i >= 0 && i < f.Length ? GfxBits.Of(f, i) : 0;
}
