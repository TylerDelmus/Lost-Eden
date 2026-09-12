using UnityEngine;

/// <summary>
/// typeCode 3013 — wait random [min,max] seconds then spawn one child.
/// </summary>
public sealed class GfxControlDelay : GfxControl
{
    readonly IEffectSpawnFactory _factory;
    readonly Color _tint;
    readonly int _childEffectId;
    readonly float _delay;
    EffectHandle _child;
    bool _spawned;

    public GfxControlDelay(
        GfxTweakRecord record,
        EffectLocator locator,
        IEffectSpawnFactory factory,
        Color tint)
        : base(record, locator)
    {
        _factory = factory;
        _tint = tint;
        float min = record != null ? record.Field(0, 0f) : 0f;
        float max = record != null ? record.Field(1, min) : min;
        if (max < min)
            (min, max) = (max, min);
        // Stock: min + (rand() & 0x7FFF) * (1/16384) * (max - min)
        float u = (Random.Range(0, 0x8000) & 0x7FFF) * (1f / 16384f);
        _delay = min + u * (max - min);
        _childEffectId = record != null ? record.FieldInt(2, 0) : 0;
        SetDuration(InfiniteDuration);
    }

    protected override void OnProcess(float dt)
    {
        if (!_spawned)
        {
            if (Age < _delay)
                return;

            _spawned = true;
            if (_childEffectId != 0 && _factory != null)
                _child = _factory.SpawnChild(_childEffectId, Locator, _tint);

            if (_child == null)
            {
                ReadyFlag = true;
                return;
            }
        }

        if (_child == null || !_factory.IsRunning(_child))
            ReadyFlag = true;
    }

    protected override void OnTerminateGracefully()
    {
        if (_child != null)
            _factory?.TerminateEffectGracefully(_child);
        ReadyFlag = true;
    }

    protected override void OnReleased(bool immediate)
    {
        if (_child != null)
            _factory?.DeleteEffect(_child);
        _child = null;
    }
}
