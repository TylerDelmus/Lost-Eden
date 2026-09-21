using UnityEngine;

/// <summary>
/// typeCode 1007/1008 — stock <c>_GfxControlNano0_t</c> / <c>_GfxControlNano1_t</c>. The glow a nano
/// cast puts on the caster's hands.
///
/// Both are plain pooled sprite emitters on the same visual as Sparks. Verified rather than assumed:
/// 1008's constructor at Gamecode FUN_100e81e8 installs <c>_GfxControlNano1_t::vftable</c>, its create
/// at FUN_100e7a0e builds a single <c>GfxVisualSprite2Type0</c> into <c>this + 0x30</c> and sizes it
/// with the same <c>max(round(rate * 1.5 * life35), field31)</c> pool, and its spawn at FUN_100e7f70
/// hands that visual exactly the values <see cref="GfxControlSpriteEmitter"/> already produces:
///
/// <code>
/// velocity = direction * field32                 ; +0x94
/// life     = lerp(field34, field35)              ; +0x9c, +0xa0
/// size0/1  = field12, field14 with (end-start)/life rates
/// colour   = fields 16-19, rates (fields 20-23 - start)/life
/// frame    = firstFrame with (lastFrame - firstFrame)/life
/// mode     = field 38                            ; +0xac
/// </code>
///
/// Field 38 is the sprite mode, and it is 0 on 8010 and 8011, so stock skips the height-falloff branch
/// in ProcessSprites and the motion is plain ballistic — the one path we implement. Neither template
/// reaches field 40, so there is no gravity term and both stay in the emitter's frame.
///
/// What separates the two is only their numbers. 8010 sprays 150 a second on a 0.125 second life, each
/// sprite growing 0.25 -> 1.0 and fading out, so roughly 19 are alive at once and the hand carries a
/// continuous pulsing glow. 8011 bursts 50 sprites at a flat size 1.0 over half a second at speed 1.0,
/// an expanding shell that reaches about half a unit. Both are additive and tint to (0.8, 0.6, 1.0)
/// with alpha running 1 -> 0.
/// </summary>
public sealed class GfxControlNano : GfxControlSpriteEmitter
{
    public GfxControlNano(
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
