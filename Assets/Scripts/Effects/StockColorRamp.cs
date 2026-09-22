/// <summary>
/// Stock's start-to-end colour ramp (Gamecode <c>FUN_10108663</c>, loaded by <c>FUN_101085de</c> from
/// eight template fields: start A,R,G,B then end A,R,G,B). Used by Plasma and Stars.
/// </summary>
public static class StockColorRamp
{
    /// <summary>
    /// Per channel _ftol((start + (end - start) * t) * 255), packed A,R,G,B with plain shifts and ORs,
    /// so an out-of-range channel spills into its neighbours as in stock. No clamping of
    /// <paramref name="t"/>.
    /// </summary>
    public static uint Eval(float[] start, float[] end, float t)
    {
        int packed = 0;
        for (int c = 0; c < 4; c++)
        {
            float delta = end[c] - start[c];
            int channel = (int)((start[c] + (double)delta * t) * 255.0);
            packed = c == 0 ? channel : (packed << 8) | channel;
        }
        return unchecked((uint)packed);
    }
}
