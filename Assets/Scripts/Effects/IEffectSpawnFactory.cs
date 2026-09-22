using UnityEngine;

/// <summary>
/// Factory callback used by Meta / Sequencer / Delay to spawn child effects.
/// </summary>
public interface IEffectSpawnFactory
{
    EffectHandle SpawnChild(int effectId, EffectLocator locator, Color tint);
    bool IsRunning(EffectHandle handle);
    void DeleteEffect(EffectHandle handle);
    void TerminateEffectGracefully(EffectHandle handle);

    /// <summary>
    /// Stock <c>_EffectHandler_t::CreateGfxControl(id, pos)</c> (Gamecode 100cea4b): a control the
    /// caller owns, processes and draws itself, not registered with the handler. Spell1 builds its
    /// hand children this way.
    /// </summary>
    GfxControl CreateOwnedControl(int effectId, EffectLocator locator);
}
