using System;

/// <summary>
/// Converts stock's per-frame effect constants into frame-rate independent ones.
///
/// Stock's <c>_GfxControl_t::Process</c> ran once per rendered frame and took no delta, so every
/// motion and spawn constant inside it is "per Process call" at whatever frame rate the client
/// happened to hit — roughly 30 FPS. Ported verbatim those constants scale with our frame rate, so
/// the same effect drifts, fills and burns out several times faster on a 240 FPS machine than it
/// did in the original.
///
/// No Unity dependency so the conversion can be asserted from unit tests.
/// </summary>
public static class EffectFrameRate
{
    /// <summary>
    /// The frame time stock's constants are assumed to be authored against (~30 FPS). The 0.033 itself
    /// is stock's first-frame delta clamp in <c>_GfxControl_t::Process</c> (<c>Gamecode 100d2a86</c>),
    /// not a frame rate; see <see cref="StockProcessHz"/>.
    /// </summary>
    public const float StockFrameDt = 0.033f;

    /// <summary>
    /// Ceiling on <see cref="FrameSteps"/>. A hitch or a breakpoint must not teleport an effect
    /// across a whole animation, and a control that spawns per step must not empty its pool in one
    /// frame. Effects are cosmetic, so losing time to a stall is always preferable to a jump.
    /// </summary>
    public const float MaxFrameSteps = 3.5f;

    /// <summary>
    /// How many stock frames <paramref name="dt"/> covers. Multiply any per-call stock constant by
    /// this and it keeps stock's timing at any frame rate: 1 at 30 FPS, 0.25 at 120 FPS.
    /// </summary>
    public static float FrameSteps(float dt)
    {
        if (float.IsNaN(dt) || dt <= 0f)
            return 0f;
        float steps = dt / StockFrameDt;
        return steps > MaxFrameSteps ? MaxFrameSteps : steps;
    }

    /// <summary>
    /// Rate at which fixed-step controls replay stock Process calls. Stock calls every control's
    /// Process once per engine frame from <c>_EffectHandler_t::RunFunction</c> (<c>Gamecode 100d2423</c>)
    /// with no time gate, and the client has no frame cap of its own (only VSync), so the true rate is
    /// whatever the stock client ran at.
    /// UNVERIFIED: 30 is the long-standing assumption here, not a measured value. Measure it with
    /// <c>/framerate</c> in the stock client before treating a replayed control as stock-exact.
    /// </summary>
    public const float StockProcessHz = 30f;

    /// <summary>Length of one replayed stock Process call.</summary>
    public const float StockProcessSeconds = 1f / StockProcessHz;

    /// <summary>
    /// Whole fixed steps of <paramref name="stepSeconds"/> that fit in the time carried so far plus
    /// <paramref name="dt"/>, at most <paramref name="maxSteps"/>. Time past the cap is dropped rather
    /// than carried, so a stall never makes a control fast-forward through its whole life.
    /// </summary>
    public static int TakeFixedSteps(ref float carry, float dt, float stepSeconds, int maxSteps)
    {
        if (stepSeconds <= 0f || maxSteps <= 0)
            return 0;

        if (float.IsNaN(carry) || float.IsInfinity(carry) || carry < 0f)
            carry = 0f;
        if (!float.IsNaN(dt) && dt > 0f)
            carry += dt;

        int steps = (int)(carry / stepSeconds);
        if (steps > maxSteps)
        {
            carry = 0f;
            return maxSteps;
        }

        carry -= steps * stepSeconds;
        return steps;
    }

    /// <summary>
    /// Spreads a stock per-call allowance (a spawn budget, say) over real time, carrying the
    /// fraction that does not fit in this frame.
    ///
    /// A budget cannot just be scaled and truncated: at 120 FPS a stock allowance of 2 becomes 0.5,
    /// which truncates to nothing and stalls the emitter forever. Carrying the remainder instead
    /// opens two slots every fourth frame, which is the same rate stock ran at.
    /// </summary>
    /// <param name="carry">
    /// Caller-owned accumulator, updated in place. One per control; it holds less than one whole
    /// unit between calls.
    /// </param>
    public static int TakeBudget(ref float carry, int stockPerFrame, float dt)
    {
        if (stockPerFrame <= 0)
            return 0;

        if (float.IsNaN(carry) || float.IsInfinity(carry) || carry < 0f)
            carry = 0f;

        carry += stockPerFrame * FrameSteps(dt);

        int whole = carry >= int.MaxValue ? int.MaxValue : (int)carry;
        if (whole <= 0)
            return 0;

        carry -= whole;
        return whole;
    }
}
