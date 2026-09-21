using UnityEngine;

/// <summary>
/// typeCode 3029 — stock <c>GfxControlScatter_t</c>.
/// Spawns <c>field6</c> copies of the child effect in <c>field10</c>, spread over <c>field7</c>
/// seconds. Field 5 selects the distribution: 0 picks random times, 1 spaces them evenly.
/// Real templates (71044, 71055, 71056) leave the remaining fields zero, so every copy is spawned
/// on the locator; the scatter is in time rather than position.
/// </summary>
public sealed class GfxControlScatter : GfxControl
{
    const int MaxSlots = 256;

    readonly IEffectSpawnFactory _factory;
    readonly Color _tint;
    readonly int _childEffectId;
    readonly float[] _fireTimes;
    readonly EffectHandle[] _children;
    readonly bool[] _fired;

    public GfxControlScatter(
        GfxTweakRecord record,
        EffectLocator locator,
        IEffectSpawnFactory factory,
        Color tint)
        : base(record, locator)
    {
        _factory = factory;
        _tint = tint;

        int mode = record != null ? record.FieldInt(5, ScatterSchedule.ModeRandom) : ScatterSchedule.ModeRandom;
        int count = record != null ? record.FieldInt(6, 1) : 1;
        count = Mathf.Clamp(count, 0, MaxSlots);
        float duration = record != null ? Mathf.Max(0f, record.Field(7, 1f)) : 1f;
        _childEffectId = record != null ? record.FieldInt(10, 0) : 0;

        _fireTimes = ScatterSchedule.BuildFireTimes(count, duration, mode, () => Random.value);
        _children = new EffectHandle[_fireTimes.Length];
        _fired = new bool[_fireTimes.Length];

        SetDurationFromTemplate(7, 1f);
    }

    protected override void OnProcess(float dt)
    {
        if (_factory == null || _fireTimes.Length == 0 || _childEffectId == 0)
        {
            ReadyFlag = true;
            return;
        }

        bool allFired = true;
        bool anyRunning = false;

        for (int i = 0; i < _fireTimes.Length; i++)
        {
            if (!_fired[i])
            {
                // Stop opening new slots once winding down.
                if (IsTerminating)
                {
                    _fired[i] = true;
                    continue;
                }

                if (Age < _fireTimes[i])
                {
                    allFired = false;
                    continue;
                }

                _fired[i] = true;
                _children[i] = _factory.SpawnChild(_childEffectId, Locator, _tint);
            }

            if (_children[i] == null)
                continue;

            if (_factory.IsRunning(_children[i]))
            {
                anyRunning = true;
            }
            else
            {
                _factory.DeleteEffect(_children[i]);
                _children[i] = null;
            }
        }

        if (allFired && !anyRunning)
            ReadyFlag = true;
    }

    protected override void OnTerminateGracefully()
    {
        for (int i = 0; i < _children.Length; i++)
        {
            if (_children[i] != null)
                _factory?.TerminateEffectGracefully(_children[i]);
        }
    }

    protected override void OnReleased(bool immediate)
    {
        for (int i = 0; i < _children.Length; i++)
        {
            if (_children[i] != null)
                _factory?.DeleteEffect(_children[i]);
            _children[i] = null;
        }
    }
}
