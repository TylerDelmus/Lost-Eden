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

    /// <summary>Stock <c>+0x34</c>: what is left of the delay, counted down by dt.</summary>
    float _remaining;

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
        // 100d8a3b: rand() & 0x7fff, scaled by 1/16384 — NOT 1/32768, so the weight runs 0 to just
        // under 2 and a delay can reach nearly min + 2 * (max - min). That is stock's, odd as it is.
        // Stock does not order the two fields either, and no shipped record has max below min.
        float u = (Random.Range(0, 0x8000) & 0x7FFF) * (1f / 16384f);
        _delay = min + u * (max - min);
        _remaining = _delay;
        _childEffectId = record != null ? record.FieldInt(2, 0) : 0;
        SetDuration(InfiniteDuration);
    }

    /// <summary>Stock slot 11 (<c>100d8961</c>): forward to the child, if it has spawned.</summary>
    public override void SetStartColor(float a, float r, float g, float b) => _child?.SetStartColor(a, r, g, b);

    /// <summary>Stock slot 12 (<c>100d899a</c>).</summary>
    public override void SetStopColor(float a, float r, float g, float b) => _child?.SetStopColor(a, r, g, b);

    /// <summary>Stock slot 13 (<c>100d89d3</c>).</summary>
    public override void SetColor(uint argb) => _child?.SetColor(argb);

    /// <summary>
    /// Stock Process (<c>100d8863</c>) is a countdown, not a clock: <c>+0x34</c> starts at the delay
    /// and loses dt every call. While it is still positive stock only decrements and — on the call
    /// that takes it below zero — spawns the child; it never ends the control on that path, so a
    /// record with no child (12251) dies on the call after, not the same one.
    /// </summary>
    protected override void OnProcess(float dt)
    {
        if (!_spawned)
        {
            _remaining -= dt;
            if (_remaining < 0f)
            {
                _spawned = true;
                if (_childEffectId != 0 && _factory != null)
                    _child = _factory.SpawnChild(_childEffectId, Locator, _tint);
            }
            return;
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
