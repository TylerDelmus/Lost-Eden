using System;

/// <summary>
/// Stock <c>GfxControlScatter_t</c> schedule build-up (field loader FUN_10110b8e).
/// The control allocates <c>field6</c> slots up front and stamps each with a fire time spread over
/// <c>field7</c> seconds: mode 0 picks a uniform random time per slot, mode 1 spaces them evenly.
/// Each slot then spawns one copy of the child effect in <c>field10</c>.
/// No Unity dependency so the schedule can be asserted from unit tests.
/// </summary>
public static class ScatterSchedule
{
    /// <summary>field5 == 0: each slot fires at a uniform random point in [0, duration).</summary>
    public const int ModeRandom = 0;
    /// <summary>field5 == 1: slot i fires at duration * i / count.</summary>
    public const int ModeEven = 1;

    /// <summary>
    /// Builds the fire times exactly as stock does at construction time.
    /// <paramref name="unit01"/> supplies the random stream (stock uses FUN_1013dca1).
    /// Slots stock leaves unstamped (any other mode) stay at 0, matching the zeroed allocation.
    /// </summary>
    public static float[] BuildFireTimes(int count, float duration, int mode, Func<float> unit01)
    {
        if (count <= 0)
            return Array.Empty<float>();

        var times = new float[count];
        for (int i = 0; i < count; i++)
        {
            if (mode == ModeRandom)
                times[i] = (unit01?.Invoke() ?? 0f) * duration;
            else if (mode == ModeEven)
                times[i] = duration * i / count;
        }
        return times;
    }
}
