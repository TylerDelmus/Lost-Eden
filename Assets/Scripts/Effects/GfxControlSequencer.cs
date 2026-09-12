using UnityEngine;

/// <summary>
/// typeCode 3004 — time-windowed multi-stage sequencer.
/// Packing: flags, N, then {effectId, startSec, endSec} × N.
/// </summary>
public sealed class GfxControlSequencer : GfxControl
{
    const int FlagLoop = 0x400;

    struct Step
    {
        public int EffectId;
        public float StartTime;
        public float EndTime;
        public bool Fired;
        public EffectHandle Child;
    }

    readonly IEffectSpawnFactory _factory;
    readonly Color _tint;
    readonly Step[] _steps;
    int _flags;
    float _cycleStart;

    public GfxControlSequencer(
        GfxTweakRecord record,
        EffectLocator locator,
        IEffectSpawnFactory factory,
        Color tint)
        : base(record, locator)
    {
        _factory = factory;
        _tint = tint;
        _flags = record != null ? record.FieldInt(0, 0) : 0;
        int n = record != null ? Mathf.Max(0, record.FieldInt(1, 0)) : 0;
        // Cap absurd N from corrupt data; stock uses bounds-safe zero reads.
        n = Mathf.Min(n, 64);
        _steps = new Step[n];

        for (int i = 0; i < n; i++)
        {
            int baseIdx = 2 + 3 * i;
            _steps[i] = new Step
            {
                EffectId = record.FieldInt(baseIdx, 0),
                StartTime = record.Field(baseIdx + 1, 0f),
                EndTime = record.Field(baseIdx + 2, -1f),
            };
        }

        SetDuration(InfiniteDuration);
    }

    protected override void OnProcess(float dt)
    {
        if (_factory == null)
        {
            ReadyFlag = true;
            return;
        }

        float relTime = Age - _cycleStart;
        bool allDone = true;

        for (int i = 0; i < _steps.Length; i++)
        {
            ref Step step = ref _steps[i];

            if (step.Child == null)
            {
                if (step.StartTime <= relTime)
                {
                    bool openEnded = step.EndTime < step.StartTime;
                    if (!step.Fired && (openEnded || relTime < step.EndTime))
                    {
                        if (step.EffectId != 0)
                        {
                            step.Child = _factory.SpawnChild(step.EffectId, Locator, _tint);
                            if (step.Child != null && step.EndTime > 0f && step.EndTime > step.StartTime)
                                step.Child.SetDuration(step.EndTime - step.StartTime);
                        }
                        step.Fired = true;
                    }
                }
                else
                {
                    allDone = false;
                }
            }

            if (step.Child != null)
            {
                if (_factory.IsRunning(step.Child))
                    allDone = false;
                else
                {
                    _factory.DeleteEffect(step.Child);
                    step.Child = null;
                }
            }
        }

        if (!allDone)
            return;

        if ((_flags & FlagLoop) != 0)
        {
            for (int i = 0; i < _steps.Length; i++)
                _steps[i].Fired = false;
            _cycleStart = Age;
            return;
        }

        ReadyFlag = true;
    }

    protected override void OnTerminateGracefully()
    {
        _flags &= ~FlagLoop;
        for (int i = 0; i < _steps.Length; i++)
        {
            if (_steps[i].Child != null)
                _factory?.TerminateEffectGracefully(_steps[i].Child);
        }
    }

    protected override void OnReleased(bool immediate)
    {
        for (int i = 0; i < _steps.Length; i++)
        {
            if (_steps[i].Child != null)
                _factory?.DeleteEffect(_steps[i].Child);
            _steps[i].Child = null;
        }
    }
}
