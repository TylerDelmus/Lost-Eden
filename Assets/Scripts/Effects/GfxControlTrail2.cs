using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// typeCode 3039 (0xbdf), stock <c>GfxControlTrail2_t</c>: a ribbon trail of the locator's frame, in the
/// vehicle buffs. The rules are <see cref="Trail2Sim"/>; this feeds it the locator's frame and
/// draws the four strips.
///
/// Stock only runs on a dynel: Process ends it when the locator has no dynel (<c>10106184</c>) or the dynel
/// is gone. Init (<c>10115260</c>) resets the ring to the locator's frame (colour 0). Each call takes one
/// sample (<see cref="Trail2Sim.Take"/>); the port takes them on the 30 Hz replay (§3.7 group B) and sets the
/// origin, the trail's live end, every frame, as stock's every-frame Process does. The visual draws unlit,
/// texture × vertex colour, SrcAlpha/One, no Z write, no culling, the texture clamped (<c>1002c63e</c>).
///
/// Not ported: the dynel ctor's rendering switch (<c>10115071</c>), which hides the trail while the dynel's
/// mesh flags (<c>VisualMesh_t</c> +0x94 or the CAT mesh data's +0xd0) are clear; the port always draws.
/// </summary>
public sealed class GfxControlTrail2 : GfxControl
{
    const int MaxStepsPerFrame = 8;

    readonly Trail2Sim _sim;
    readonly Texture2D _texture;
    readonly Trail2Sim.Strip[] _built;
    readonly EffectBillboardBatch.Strip[] _strips;
    float _carry;
    float _stepAge;

    public Trail2Sim Sim => _sim;

    public GfxControlTrail2(GfxTweakRecord record, EffectLocator locator, Texture2D texture)
        : base(record, locator)
    {
        _texture = texture;
        _sim = new Trail2Sim(record?.Fields);
        // The base timer ends it at field 8 when that is above 0; every record has -1.
        base.SetDuration(_sim.Duration > 0f ? _sim.Duration : InfiniteDuration);

        _built = _sim.NewStrips();
        _strips = new EffectBillboardBatch.Strip[Trail2Sim.StripCount];
        int vertices = 2 * (_sim.Length + 1);
        for (int s = 0; s < _strips.Length; s++)
        {
            _strips[s] = new EffectBillboardBatch.Strip
            {
                Positions = new Vector3[vertices],
                Uvs = new Vector2[vertices],
                Colors = new Color32[vertices],
                Color = Color.white,
                Texture = _texture,
                Additive = true,
            };
        }

        // 10115305: Reset to the locator's frame, or the identity when it can't be resolved; colour 0.
        Matrix4x4 frame = Matrix4x4.identity;
        if (locator != null && locator.TryResolve(out Matrix4x4 m))
            frame = m;
        _sim.Reset(ToSample(frame));
    }

    /// <summary>Stock slot 8 is the empty <c>10079931</c>.</summary>
    public override void SetDuration(float seconds) { }

    // Stock's first Process arms the control and still runs the body at age 0.
    protected override void OnArmed() => Body(0f, 0f, sample: true);

    protected override void OnProcess(float dt)
    {
        float step = EffectFrameRate.StockProcessSeconds;
        int steps = EffectFrameRate.TakeFixedSteps(ref _carry, dt, step, MaxStepsPerFrame);
        for (int s = 0; s < steps && !ReadyFlag; s++)
        {
            _stepAge += step;
            Body(_stepAge, step, sample: true);
        }
        if (ReadyFlag)
            return;

        // The live end at this frame, then the visual's frame update.
        Body(Age, dt, sample: false);
        if (!ReadyFlag)
            _sim.Drift(dt, Random.value);
    }

    void Body(float age, float dt, bool sample)
    {
        // 10115681: no dynel under the locator ends it.
        if (!TryDynelForward(out Vector3 forward) || Locator == null || !Locator.TryResolve(out Matrix4x4 m))
        {
            ReadyFlag = true;
            return;
        }

        var fwd = new Trail2Sim.V3(forward.x, forward.y, forward.z);
        if (!_sim.Head(age, ToSample(m), fwd, out Trail2Sim.Sample head))
            return;
        if (sample)
            _sim.Take(head, dt);
        else
            _sim.SetOrigin(head);
    }

    /// <summary>The locator's dynel: its rotation × (0, 0, 1) (<c>n3Dynel_t::GetGlobalRot</c>).</summary>
    bool TryDynelForward(out Vector3 forward)
    {
        forward = Vector3.zero;
        if (Locator == null)
            return false;
        if (Locator.TryGetVisual(out VisualDynel visual) && visual != null)
        {
            forward = visual.transform.rotation * Vector3.forward;
            return true;
        }
        if (Locator.TryGetSourceDynel(out Dynel dynel) && dynel != null)
        {
            forward = dynel.transform.rotation * Vector3.forward;
            return true;
        }
        return false;
    }

    static Trail2Sim.Sample ToSample(Matrix4x4 m)
    {
        return new Trail2Sim.Sample
        {
            AxisX = new Trail2Sim.V3(m.m00, m.m10, m.m20),
            AxisY = new Trail2Sim.V3(m.m01, m.m11, m.m21),
            AxisZ = new Trail2Sim.V3(m.m02, m.m12, m.m22),
            Place = new Trail2Sim.V3(m.m03, m.m13, m.m23),
        };
    }

    public override void CollectStrips(List<EffectBillboardBatch.Strip> dest, Camera camera)
    {
        if (!IsAlive || dest == null || _texture == null || _sim.PointCount < 2)
            return;

        _sim.Build(_built);
        for (int s = 0; s < _strips.Length; s++)
        {
            Trail2Sim.Strip from = _built[s];
            EffectBillboardBatch.Strip to = _strips[s];
            for (int v = 0; v < from.Count; v++)
            {
                to.Positions[v] = new Vector3(from.Positions[v * 3], from.Positions[v * 3 + 1], from.Positions[v * 3 + 2]);
                // D3D v runs down the image, Unity's up.
                to.Uvs[v] = new Vector2(from.Uvs[v * 2], 1f - from.Uvs[v * 2 + 1]);
                to.Colors[v] = EffectBillboardBatch.ToColor32(from.Colours[v]);
            }
            to.Count = from.Count;
            dest.Add(to);
        }
    }
}
