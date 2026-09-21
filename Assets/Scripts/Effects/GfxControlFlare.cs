using UnityEngine;

/// <summary>
/// typeCode 1005/1006 — stock <c>_GfxControlFlare_t</c> / <c>_GfxControlFlare1_t</c>.
/// See <see cref="GfxControlSpriteEmitter"/> for the pooling and
/// <see cref="SpriteEmitterMath.SpikeEndScales"/> for the geometry.
///
/// Flare sprites are spikes, not points: stock gives this control its own visual
/// (<c>GfxVisualFlareType0</c>, whose NewSprite takes two positions and two rates rather than one of
/// each), and its spawn at Gamecode FUN_100dd0c4 starts both ends of a segment on the emitter and
/// slides them outward along one random direction. So a spike grows out of the hand and vanishes
/// instead of flying away, and fields 29/30 set its length rather than its speed.
///
/// Flare templates stop at field 35, so there is no gravity term.
/// </summary>
public sealed class GfxControlFlare : GfxControlSpriteEmitter
{
    public GfxControlFlare(
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
            fallbackDuration: 4f, spikeSegment: true)
    {
    }
}
