using System;

/// <summary>
/// Stock <c>_GfxControlDeformer_t</c> (typeCode 3001 / 0xbb9, vftable <c>Gamecode 1016c6bc</c>): moves
/// the vertices of its dynel's CAT mesh through a vertex callback on the mesh's CATRender (Process
/// <c>100d7caa</c>, callback <c>FUN_100d776f</c>). 45057, nano 266281's hit, is mode 1.
///
/// Fields (loader <c>100d823f</c>): 8 duration, 10 mode, 11 peak, 12 fade-in seconds, 13 fade-out
/// seconds, 14 wave rate and 15 amplitude for mode 1 (0 and 9 are read but not used by mode 1).
///
/// Envelope (end of Process): once 0 &lt; duration and duration - fade-out &lt; age the control terminates
/// itself (slot 6 <c>100d7742</c>: the current envelope becomes the peak and the fade-out starts at
/// that age). Before that, while age &lt; fade-in, envelope = peak * age / fade-in (and otherwise keeps
/// its last value); after it, envelope = (1 - (age - start) / fade-out) * peak, and the control is
/// ready once that reaches 0. It starts at 0.
///
/// Mode 1, per vertex at render time: the skinned position moves along the skinned normal by
/// sin²(((b.x + 13T) * 13 + (b.y - 11T) * 17 + b.z * T * 19) * 11 + T) * amplitude * envelope, with b
/// the vertex's source position and T = rate * age.
/// </summary>
public sealed class DeformerSim
{
    public const int WobbleMode = 1;

    readonly float _fadeIn;
    readonly float _fadeOut;
    readonly float _rate;
    readonly float _amplitude;
    float _peak;
    float _terminatedAt;

    public int Mode { get; }
    public float Duration { get; set; }
    public float Envelope { get; private set; }
    public bool Terminating { get; private set; }

    public DeformerSim(float[] fields)
    {
        Duration = F(fields, 8);
        Mode = Int(fields, 10);
        _peak = F(fields, 11);
        _fadeIn = F(fields, 12);
        _fadeOut = F(fields, 13);
        _rate = F(fields, 14);
        _amplitude = F(fields, 15);
    }

    /// <summary>The envelope part of one Process at <paramref name="age"/>. True when the control is done.</summary>
    public bool Step(float age)
    {
        if (0f < Duration && Duration - _fadeOut < age)
            Terminate(age);

        if (!Terminating)
        {
            if (age < _fadeIn)
                Envelope = _peak * age / _fadeIn;
            return false;
        }

        Envelope = (1f - (age - _terminatedAt) / _fadeOut) * _peak;
        return Envelope <= 0f;
    }

    /// <summary>Slot 6: start fading out from the current envelope, once.</summary>
    public void Terminate(float age)
    {
        if (Terminating)
            return;
        Terminating = true;
        _peak = Envelope;
        _terminatedAt = age;
    }

    /// <summary>How far mode 1 pushes a vertex along its normal at <paramref name="age"/>.</summary>
    public float WobbleWeight(float age, float bx, float by, float bz)
    {
        float t = _rate * age;
        double t11 = t * 11.0;
        double t13 = t * 13.0;
        double arg = (((bx + t13) * 13.0 + (by - t11) * 17.0) + bz * (double)t * 19.0) * 11.0 + t;
        float s = (float)Math.Sin(arg);
        return (float)((double)s * s * _amplitude * Envelope);
    }

    static float F(float[] f, int i) => f != null && i < f.Length ? f[i] : 0f;

    static int Int(float[] f, int i) => f != null && i < f.Length ? BitConverter.SingleToInt32Bits(f[i]) : 0;
}
