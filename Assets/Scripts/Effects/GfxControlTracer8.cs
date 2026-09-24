using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// typeCode 3026 (0xbd2), stock <c>GfxControlTracer8_t</c>. The mechanics are <see cref="Tracer8Sim"/>.
///
/// Tracer8 draws nothing of its own. It owns one child effect (field 11), made on the tracer's own ref
/// frame (<c>10114ce4</c>) and given that frame again every call (<c>10114c40</c> → the child's slot 5,
/// a straight 64-byte copy of the matrix), so the child follows both the position and the turn. On
/// teardown it fires field 14 at the point the flight *started* from, not where it ended: stock copies
/// the start into <c>+0x80</c> in the constructor (<c>10114eef</c>) and nothing writes there again, and
/// the teardown at <c>10114b4c</c> passes that copy.
///
/// The turn is <c>1013c747</c>: row 0 is unit(dir x up), row 2 is unit(dir) and row 1 closes the basis,
/// with up = (0, 1, 0). That is Unity's <c>LookRotation(dir, up)</c> turned half a revolution about the
/// direction, i.e. <c>LookRotation(dir, -up)</c>. A dir parallel to up leaves the frame unturned
/// (<c>1013c7b7</c> resets it to the identity).
/// </summary>
public sealed class GfxControlTracer8 : GfxControl
{
    readonly Tracer8Sim _sim;
    readonly IEffectSpawnFactory _factory;
    readonly EffectLocator _childLocator;
    readonly GfxControl _child;
    bool _fired;

    public Tracer8Sim Sim => _sim;
    public GfxControl Child => _child;

    public GfxControlTracer8(
        GfxTweakRecord record,
        EffectLocator locator,
        Vector3 start,
        Vector3 end,
        IEffectSpawnFactory factory)
        : base(record, locator)
    {
        _factory = factory;
        _sim = new Tracer8Sim(record?.Fields, start.x, start.y, start.z, end.x, end.y, end.z);
        // The loader keeps field 8 at +0x10 and the base's expiry applies to it.
        base.SetDuration(record != null ? record.Field(8, InfiniteDuration) : InfiniteDuration);

        if (_sim.ReadyAtStart)
        {
            ReadyFlag = true;
            return;
        }

        // 10114ce4: the child on the tracer's frame, then SetColor(1,1,1,1) and slot 12 with zeros.
        _childLocator = EffectLocator.WorldPoint(start, Turn());
        if (factory != null && _sim.ChildId > 0)
            _child = factory.CreateOwnedControl(_sim.ChildId, _childLocator);
        if (_child != null)
        {
            _child.SetStartColor(1f, 1f, 1f, 1f);
            _child.SetStopColor(0f, 0f, 0f, 0f);
        }
    }

    /// <summary>1013c747 with up = (0, 1, 0); a direction along up leaves the frame unturned.</summary>
    Quaternion Turn()
    {
        var dir = new Vector3(_sim.DirX, _sim.DirY, _sim.DirZ);
        Vector3 right = Vector3.Cross(dir, Vector3.up);
        // 1013c7b7: with the cross and the direction both zero stock resets the frame to the identity.
        // With only the cross zero — a flight straight up or down — it builds a basis with a zero row;
        // the port leaves that unturned instead. No record's flight is vertical.
        if (right == Vector3.zero)
            return Quaternion.identity;
        return Quaternion.LookRotation(dir, Vector3.down);
    }

    /// <summary>Stock slot 8 is the empty <c>10079931</c>: a tracer's duration is its record's.</summary>
    public override void SetDuration(float seconds)
    {
    }

    /// <summary>Stock slot 6 (<c>10114e69</c>) tears the child down and hands the colour on.</summary>
    protected override void OnTerminateGracefully() => ReadyFlag = true;

    /// <summary>Stock slot 13 (<c>10114d3a</c>) keeps the colour and passes it to the child.</summary>
    public override void SetColor(uint argb) => _child?.SetColor(argb);

    // Stock's first Process arms the tracer and still runs the step at age 0.
    protected override void OnArmed() => Step(0f);

    protected override void OnProcess(float dt) => Step(dt);

    void Step(float dt)
    {
        if (_sim.Position(Age, out float x, out float y, out float z))
            ReadyFlag = true;
        if (_child == null)
            return;
        _childLocator.SetWorldPoint(new Vector3(x, y, z), Turn());
        _child.Process(dt);
    }

    public override void CollectBillboards(List<EffectBillboardBatch.Quad> dest, Camera camera)
    {
        if (IsAlive)
            _child?.CollectBillboards(dest, camera);
    }

    public override void CollectStrips(List<EffectBillboardBatch.Strip> dest, Camera camera)
    {
        if (IsAlive)
            _child?.CollectStrips(dest, camera);
    }

    public override void CollectMeshes(List<EffectBillboardBatch.MeshDraw> dest, Camera camera)
    {
        if (IsAlive)
            _child?.CollectMeshes(dest, camera);
    }

    /// <summary>
    /// Stock's teardown (<c>10114b42</c>), reached from both the destructor and slot 6: field 14 is
    /// fired at the flight's start, then the child is released.
    /// </summary>
    protected override void OnReleased(bool immediate)
    {
        if (!_fired && _sim.EndEffectId > 0 && _factory != null)
        {
            _fired = true;
            _factory.SpawnChild(
                _sim.EndEffectId,
                EffectLocator.WorldPoint(new Vector3(_sim.FromX, _sim.FromY, _sim.FromZ), Quaternion.identity),
                Color.white);
        }
        _child?.Release(true);
    }
}
