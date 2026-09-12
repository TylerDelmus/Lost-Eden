using UnityEngine;

/// <summary>
/// typeCode 2007 — up to 10 child effect IDs at indices 0–9; spawn all at construct.
/// Index 9 == −1 → clear slot and infinite parent duration.
/// </summary>
public sealed class GfxControlMeta : GfxControl
{
    const int SlotCount = 10;

    readonly IEffectSpawnFactory _factory;
    readonly Color _tint;
    readonly EffectHandle[] _children = new EffectHandle[SlotCount];

    public GfxControlMeta(
        GfxTweakRecord record,
        EffectLocator locator,
        IEffectSpawnFactory factory,
        Color tint)
        : base(record, locator)
    {
        _factory = factory;
        _tint = tint;

        bool infinite = false;
        for (int i = 0; i < SlotCount; i++)
        {
            int childId = record != null ? record.FieldInt(i, 0) : 0;
            if (i == 9 && childId == -1)
            {
                childId = 0;
                infinite = true;
            }

            if (childId == 0 || childId == record?.Id)
                continue;

            _children[i] = _factory?.SpawnChild(childId, locator, tint);
        }

        if (infinite)
            SetDuration(InfiniteDuration);
        else
            SetDuration(90f); // stock post-ctor safety net
    }

    protected override void OnProcess(float dt)
    {
        if (_factory == null)
        {
            ReadyFlag = true;
            return;
        }

        for (int i = 0; i < SlotCount; i++)
        {
            if (_children[i] != null && _factory.IsRunning(_children[i]))
                return;
        }

        ReadyFlag = true;
    }

    protected override void OnTerminateGracefully()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            if (_children[i] != null)
                _factory?.TerminateEffectGracefully(_children[i]);
        }
        ReadyFlag = true;
    }

    protected override void OnReleased(bool immediate)
    {
        for (int i = 0; i < SlotCount; i++)
        {
            if (_children[i] != null)
                _factory?.DeleteEffect(_children[i]);
            _children[i] = null;
        }
    }
}
