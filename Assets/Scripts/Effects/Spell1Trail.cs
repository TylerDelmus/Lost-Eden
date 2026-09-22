using System;

/// <summary>
/// Stock <c>_GfxControlSpell1_t</c> window 2: the sprite trail laid between the caster's hands.
/// <see cref="WindowPasses"/> is the per-frame schedule (<c>FUN_100f3d22</c>) and <see cref="Pass"/>
/// one row of sprites (<c>FUN_100f3205</c>). Sprites are redrawn every frame: the visual,
/// <c>GfxVisualSprite2Type2</c>, clears its list after drawing (<c>10027df8</c>).
/// No Unity dependency so it can be asserted from unit tests.
/// </summary>
public static class Spell1Trail
{
    /// <summary>At most this many sprites in one pass (<c>100f3270 CMP EAX, 0x32</c>).</summary>
    public const int MaxPerPass = 50;

    /// <summary>
    /// The passes for this frame, each as (t0, size0, t1, size1) in <paramref name="passes"/>.
    /// Returns how many (0 or 2). Only called while start &lt;= age &lt;= end, and not on the frame
    /// the window opens.
    /// First half: from each hand inward, (0, .75-u, u, .25) and (1, .75-u, 1-u, .25), u = t/half*.5.
    /// Second half: from the middle outward, (s, .25, .5, c) and (1-s, .25, .5, c), s = u*.5,
    /// c = u*.75 + .25, u = (t-half)/half.
    /// </summary>
    public static int WindowPasses(float age, float start, float end, float[] passes)
    {
        float w = end - start;
        float half = w * 0.5f;
        float t = age - start;

        if (half > t)
        {
            float u = (float)((double)(t - 0f) / half * 0.5);
            float size0 = (float)(0.75 - u);
            Write(passes, 0, 0f, size0, u, 0.25f);
            Write(passes, 1, 1f, size0, (float)(1.0 - u), 0.25f);
            return 2;
        }

        if (t < w)
        {
            float u = (t - half) / half;
            float c = (float)(u * 0.75 + 0.25);
            float s = (float)(0.5 * u);
            Write(passes, 0, s, 0.25f, 0.5f, c);
            Write(passes, 1, (float)(1.0 - s), 0.25f, 0.5f, c);
            return 2;
        }

        return 0;
    }

    /// <summary>
    /// Sprites in one pass: _ftol(|handDistance * (t1 - t0)| * 50), at most 50. There is no lower
    /// bound: 0 draws nothing and 1 draws a single sprite at t0.
    /// </summary>
    public static int PassCount(float handDistance, float t0, float t1)
    {
        double span = (double)t1 - t0;
        float n = (float)(handDistance * span);
        if (!(0f <= n))
            n = -n;
        int count = (int)(n * 50.0);
        if (count < 0)
            count = 2;
        if (count > MaxPerPass)
            count = MaxPerPass;
        return count;
    }

    /// <summary>
    /// One pass. Calls <paramref name="emit"/>(t, size) for each sprite, where the sprite sits at
    /// handA + (handB - handA) * t. t carries a ±0.01 jitter from <paramref name="rand01"/> and is not
    /// clamped; size is interpolated with no floor.
    /// </summary>
    public static void Pass(float handDistance, float t0, float size0, float t1, float size1,
        Func<float> rand01, Action<float, float> emit)
    {
        int count = PassCount(handDistance, t0, t1);
        if (count <= 0)
            return;

        float steps = count - 1;
        float tStep = (float)(((double)t1 - t0) / steps);
        float sizeStep = (size1 - size0) / steps;
        float t = t0;
        float size = size0;
        for (int i = 0; i < count; i++)
        {
            float jittered = (float)(rand01() * 0.019999999552965164 - 0.009999999776482582 + t);
            t = tStep + t;
            emit(jittered, size);
            size = sizeStep + size;
        }
    }

    /// <summary>Window-2 colour (<c>100f2cc9</c>): fields 10-13 as _ftol(c*255) clamped 0..255, A,R,G,B.</summary>
    public static uint PackColor(float a, float r, float g, float b)
        => (uint)(Channel(a) << 24 | Channel(r) << 16 | Channel(g) << 8 | Channel(b));

    static int Channel(float c)
    {
        int v = (int)(c * 255.0);
        if (v < 0)
            v = 0;
        if (v > 0xff)
            v = 0xff;
        return v;
    }

    static void Write(float[] p, int i, float t0, float s0, float t1, float s1)
    {
        p[i * 4] = t0;
        p[i * 4 + 1] = s0;
        p[i * 4 + 2] = t1;
        p[i * 4 + 3] = s1;
    }
}
