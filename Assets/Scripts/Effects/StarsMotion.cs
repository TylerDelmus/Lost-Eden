using System;

/// <summary>
/// Pure stock Stars motion helpers (FUN_100f826a). No Unity deps so EditMode / console
/// tests can lock working nano FX (e.g. type 3 for 28612, type 8 for 28608).
/// </summary>
public static class StarsMotion
{
    /// <summary>
    /// starType 8 — expanding shell.
    /// phase = (cycleCount * age) / duration; frac = fract(phase);
    /// radius = sqrt(frac) * 0.01 * radiusScaleInt;
    /// size = sizeCurve * sqrt(frac) + sizeBias;
    /// </summary>
    public static void Case8(
        float age,
        float duration,
        int cycleCount,
        int radiusScaleInt,
        float sizeCurve,
        float sizeBias,
        out float radius,
        out float size,
        out float lifeFrac)
    {
        float dur = duration > 1e-4f ? duration : 1f;
        int cycles = cycleCount > 0 ? cycleCount : 1;
        float phase = (cycles * age) / dur;
        float frac = phase - (float)Math.Floor(phase);
        if (frac < 0f)
            frac = 0f;
        float s = (float)Math.Sqrt(frac);
        radius = s * 0.01f * radiusScaleInt;
        size = sizeCurve * s + sizeBias;
        lifeFrac = frac;
    }

    /// <summary>
    /// starType 3 size curve (alive particles): (lifeFrac + 0.2) * sizeCurve * (1 - lifeFrac²).
    /// </summary>
    public static float Case3Size(float lifeFrac, float sizeCurve)
    {
        float t = lifeFrac < 0f ? 0f : (lifeFrac > 1f ? 1f : lifeFrac);
        float size = (t + 0.2f) * sizeCurve * (1f - t * t);
        return size < 0.05f ? 0.05f : size;
    }

    /// <summary>
    /// One stock case-3 integration step (frame-rate normalized externally).
    /// tangent += (origin - worldPos) * k; worldPos += tangent * k;
    /// </summary>
    public static void Case3Step(
        ref float px, ref float py, ref float pz,
        ref float tx, ref float ty, ref float tz,
        float ox, float oy, float oz,
        float k)
    {
        float dx = ox - px;
        float dy = oy - py;
        float dz = oz - pz;
        tx += dx * k;
        ty += dy * k;
        tz += dz * k;
        px += tx * k;
        py += ty * k;
        pz += tz * k;
    }
}
