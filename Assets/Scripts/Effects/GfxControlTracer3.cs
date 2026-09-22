using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// typeCode 1022 (0x3fe), stock <c>_GfxControlTracer3_t</c>: carries a child effect (field 15, a Nano0
/// trail in every record) from the hit location's start to its end. The rules are
/// <see cref="Tracer3Sim"/>; the child is owned here, as stock's +0x2c is: made by position at the start,
/// moved and processed by the tracer every call, drawn with it and deleted with it.
/// </summary>
public sealed class GfxControlTracer3 : GfxControl
{
    readonly Tracer3Sim _sim;
    readonly GfxControl _child;

    public Tracer3Sim Sim => _sim;
    public GfxControl Child => _child;

    public GfxControlTracer3(
        GfxTweakRecord record,
        EffectLocator locator,
        Vector3 start,
        Vector3 end,
        IEffectSpawnFactory factory)
        : base(record, locator)
    {
        _sim = new Tracer3Sim(record?.Fields, start.x, start.y, start.z, end.x, end.y, end.z);
        // The base keeps field 8 (100ff683), and its expiry applies.
        base.SetDuration(record != null ? record.Field(8, InfiniteDuration) : InfiniteDuration);
        if (_sim.ReadyAtStart)
        {
            ReadyFlag = true;
            return;
        }

        // 100ff6e2: the child by position at the start, its colours, then one Process (its arming call).
        if (factory != null && _sim.ChildId > 0)
            _child = factory.CreateOwnedControl(_sim.ChildId, EffectLocator.WorldPoint(start, Quaternion.identity));
        if (_child != null)
        {
            _child.SetStartColor(_sim.A, _sim.R, _sim.G, _sim.B);
            _child.SetStopColor(0f, _sim.R, _sim.G, _sim.B);
            _child.Process(0f);
        }
    }

    /// <summary>Stock slot 8 is the empty <c>10079931</c>: a tracer's duration is its record's.</summary>
    public override void SetDuration(float seconds)
    {
    }

    /// <summary>Stock slot 6 (<c>100ff652</c>): ready at once.</summary>
    protected override void OnTerminateGracefully() => ReadyFlag = true;

    /// <summary>
    /// Stock slot 13 (<c>100ff7ba</c>): keeps the colour and hands it to the child. Stock then processes
    /// the child once more within the same frame; the port doesn't.
    /// </summary>
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
        _child.UpdatePosition(new Vector3(x, y, z));
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

    protected override void OnReleased(bool immediate) => _child?.Release(true);
}
