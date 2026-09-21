using UnityEngine;

/// <summary>
/// typeCode 1018 — stock <c>_GfxControlSparks_t</c>.
/// Same pooled sprite emitter as Flare (see <see cref="GfxControlSpriteEmitter"/>), extended with
/// gravity at field 40. Record 43720 bursts 120 sprites into an upward cone at ~8-11 m/s.
/// </summary>
public sealed class GfxControlSparks : GfxControlSpriteEmitter
{
    public GfxControlSparks(
        GfxTweakRecord record,
        EffectLocator locator,
        Texture2D atlas,
        EffectAtlasFrames frames,
        int cols,
        int rows,
        int firstFrame,
        int lastFrame,
        Color tint,
        EffectLightPool lights = null)
        : base(record, locator, atlas, frames, cols, rows, firstFrame, lastFrame, tint, lights,
            fallbackDuration: 2f)
    {
    }
}
