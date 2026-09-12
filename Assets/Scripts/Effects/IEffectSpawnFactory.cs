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
}
