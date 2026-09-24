using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// typeCode 1002 (0x3ea), stock <c>_GfxControlBuffPlaceHolder_t</c> (vftable <c>Gamecode 1016c4fc</c>,
/// loader <c>100d54ca</c>, dynel ctor <c>100d5d20</c>, init <c>100d5834</c>, Process <c>100d52c3</c>): two
/// child effects swung round the attach point. Nano 25988's buff runs two of these round the head:
/// each a Flare (the leading spark) and a Cord (its trail).
///
/// Fields: 0 flags, 1-7 locator, 8 duration, 10 child A (a point effect), 11 child B (a dynel effect),
/// 12-15 / 16-19 start / stop colour, 21 radius R, 22 start phase, 23 rate (rad/s), 26 per-body table
/// (<see cref="EffectBodyTable"/>: offset z and R by breed, sex and fatness; also host Scale / 100).
///
/// Init: phase = field 22. Child A is created with <c>CreateGfxControl(id, pos)</c> at the locator plus
/// its Z axis * sin(phase) * R plus its X axis * cos(phase) * R, child B with <c>(id, dynel, 0)</c>; both
/// are the BuffPlaceHolder's own, not the handler's. Its colours go into both.
/// Process: an accumulator grows by rate * dt and the phase follows it in 0.45 rad steps. At each
/// step child B gets <c>UpdatePosition((cos, sin, 0) * R * scale)</c> and runs its Process, then child A
/// gets the locator plus its Y axis * sin * R plus its X axis * cos * R and runs its Process. So the
/// children advance once per step, each by the whole frame delta (the port: one stock frame).
/// Slots 11 / 12 store the colour and push it into both children (<c>100d55d9</c>), so a BPHFSM's
/// colours replace the record's. Slot 6 readies it; releasing it deletes both children.
/// </summary>
public sealed class GfxControlBuffPlaceHolder : GfxControl
{
    /// <summary>100d5335: the phase advances in steps of 0.45 rad.</summary>
    const float PhaseStep = 0.44999998807907104f;

    /// <summary>
    /// The delta each child's Process sees. Stock's base Process reads the engine's frame time, so a
    /// child run at every step takes a whole frame's delta per step. The port replays that at
    /// <see cref="EffectFrameRate.StockProcessHz"/> like its other per-call bodies (UNVERIFIED rate).
    /// </summary>
    const float ChildDt = EffectFrameRate.StockProcessSeconds;

    readonly float _radius;
    readonly float _rate;
    readonly float _scale;
    readonly float[] _start = new float[4];
    readonly float[] _stop = new float[4];
    GfxControl _pointChild;
    GfxControl _dynelChild;
    float _accumulator;
    float _phase;

    public GfxControl PointChild => _pointChild;
    public GfxControl DynelChild => _dynelChild;

    public GfxControlBuffPlaceHolder(
        GfxTweakRecord record,
        EffectLocator locator,
        IEffectSpawnFactory factory,
        EffectBodyTable.Entry body)
        : base(record, locator)
    {
        _radius = body.HasRadius ? body.Radius : record != null ? record.Field(21, 0f) : 0f;
        _phase = record != null ? record.Field(22, 0f) : 0f;
        _accumulator = _phase;
        _rate = record != null ? record.Field(23, 0f) : 0f;
        _scale = body.Scale;
        for (int c = 0; c < 4; c++)
        {
            _start[c] = record != null ? record.Field(12 + c, 0f) : 0f;
            _stop[c] = record != null ? record.Field(16 + c, 0f) : 0f;
        }
        SetDurationFromTemplate(8, 1.5f);

        if (factory == null || locator == null || !locator.TryResolve(out Matrix4x4 m))
            return;

        Vector3 start = (Vector3)m.GetColumn(3)
            + (Vector3)m.GetColumn(2) * (Mathf.Sin(_phase) * _radius)
            + (Vector3)m.GetColumn(0) * (Mathf.Cos(_phase) * _radius);

        int pointId = record != null ? record.FieldInt(10, 0) : 0;
        int dynelId = record != null ? record.FieldInt(11, 0) : 0;
        if (pointId > 0)
            _pointChild = factory.CreateOwnedControl(pointId, EffectLocator.WorldPoint(start, Quaternion.identity));
        if (dynelId > 0)
            _dynelChild = factory.CreateOwnedControl(dynelId, locator.WithAttach(0));

        PushColours();
    }

    /// <summary>Stock slot 11 (<c>100d56fc</c>).</summary>
    public override void SetStartColor(float a, float r, float g, float b)
    {
        _start[0] = a;
        _start[1] = r;
        _start[2] = g;
        _start[3] = b;
        PushColours();
    }

    /// <summary>Stock slot 12 (<c>100d5720</c>).</summary>
    public override void SetStopColor(float a, float r, float g, float b)
    {
        _stop[0] = a;
        _stop[1] = r;
        _stop[2] = g;
        _stop[3] = b;
        PushColours();
    }

    /// <summary>Stock slot 13 (<c>100d5744</c>): start = the colour, stop = it at alpha 0, then both into the children.</summary>
    public override void SetColor(uint argb) => SetColorAsStartAndFadeOut(argb);

    /// <summary>100d55d9: both colours into child A, then both into child B.</summary>
    void PushColours()
    {
        foreach (GfxControl child in new[] { _pointChild, _dynelChild })
        {
            if (child == null)
                continue;
            child.SetStartColor(_start[0], _start[1], _start[2], _start[3]);
            child.SetStopColor(_stop[0], _stop[1], _stop[2], _stop[3]);
        }
    }

    /// <summary>Stock slot 6 (<c>100d54c5</c>): ready at once.</summary>
    protected override void OnTerminateGracefully() => ReadyFlag = true;

    protected override void OnProcess(float dt)
    {
        _accumulator += _rate * dt;
        int steps = (int)((_accumulator - _phase) / PhaseStep);
        Matrix4x4 m = WorldMatrix;
        for (int i = 0; i < steps; i++)
        {
            _phase += PhaseStep;
            float c = Mathf.Cos(_phase) * _radius, s = Mathf.Sin(_phase) * _radius;
            if (_dynelChild != null)
            {
                _dynelChild.UpdatePosition(new Vector3(c, s, 0f) * _scale);
                _dynelChild.Process(ChildDt);
            }
            if (_pointChild != null)
            {
                _pointChild.UpdatePosition((Vector3)m.GetColumn(3) + (Vector3)m.GetColumn(1) * s + (Vector3)m.GetColumn(0) * c);
                _pointChild.Process(ChildDt);
            }
        }
    }

    public override void CollectBillboards(List<EffectBillboardBatch.Quad> dest, Camera camera)
    {
        if (!IsAlive)
            return;
        _dynelChild?.CollectBillboards(dest, camera);
        _pointChild?.CollectBillboards(dest, camera);
    }

    public override void CollectStrips(List<EffectBillboardBatch.Strip> dest, Camera camera)
    {
        if (!IsAlive)
            return;
        _dynelChild?.CollectStrips(dest, camera);
        _pointChild?.CollectStrips(dest, camera);
    }

    protected override void OnReleased(bool immediate)
    {
        _pointChild?.Release(true);
        _dynelChild?.Release(true);
        _pointChild = null;
        _dynelChild = null;
    }
}
