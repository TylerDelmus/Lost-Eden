using System.Collections.Generic;
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

    /// <summary>The spawned children, by slot (null where a slot is empty).</summary>
    public IReadOnlyList<EffectHandle> Children => _children;

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

        // Own duration only: stock's ctor writes +0x10 directly (100e5f46 / 100e5f5e), it does not go
        // through slot 8, so the children keep theirs.
        if (infinite)
            base.SetDuration(InfiniteDuration);
        else
            base.SetDuration(90f); // stock post-ctor safety net
    }

    /// <summary>
    /// Stock slot 8, <c>Gamecode 100e5e09</c>: hands the duration to every child through
    /// <c>_EffectHandler_t::SetDuration</c>, then keeps <c>seconds + 15</c> for itself so the children
    /// finish first. This is how the nano cast's <c>SetDuration(6000)</c> reaches the controls that
    /// actually draw.
    /// </summary>
    public override void SetDuration(float seconds)
    {
        for (int i = 0; i < SlotCount; i++)
            _children?[i]?.SetDuration(seconds);
        base.SetDuration(seconds + 15f);
    }

    /// <summary>Stock slot 4, <c>Gamecode 100e5d7f</c>: forward to every child.</summary>
    public override void UpdatePosition(Vector3 position)
    {
        for (int i = 0; i < SlotCount; i++)
            _children[i]?.UpdatePosition(position);
    }

    /// <summary>Stock slot 11 (<c>100e5e4a</c>): forward to every child.</summary>
    public override void SetStartColor(float a, float r, float g, float b)
    {
        for (int i = 0; i < SlotCount; i++)
            _children[i]?.SetStartColor(a, r, g, b);
    }

    /// <summary>Stock slot 12 (<c>100e5e92</c>): forward to every child.</summary>
    public override void SetStopColor(float a, float r, float g, float b)
    {
        for (int i = 0; i < SlotCount; i++)
            _children[i]?.SetStopColor(a, r, g, b);
    }

    /// <summary>Stock slot 13 (<c>100e5eda</c>): forward to every child.</summary>
    public override void SetColor(uint argb)
    {
        for (int i = 0; i < SlotCount; i++)
            _children[i]?.SetColor(argb);
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

    /// <summary>
    /// Stock slot 6, <c>Gamecode 100e5ddd</c>: terminate every child gracefully and mark itself
    /// terminating (+0x28). It does not ready itself; Process readies it once no child is running, so
    /// the children get to wind down instead of being deleted with the parent.
    /// </summary>
    protected override void OnTerminateGracefully()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            if (_children[i] != null)
                _factory?.TerminateEffectGracefully(_children[i]);
        }
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
