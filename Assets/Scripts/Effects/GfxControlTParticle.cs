using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// typeCode 3020 (0xbcc), stock <c>GfxControlTParticle_t</c>. The maths is <see cref="TParticleSim"/>;
/// this places the visual and draws it.
///
/// The visual sits at the locator's local-mode position (<c>10106306</c>) with its turn
/// (<c>101062d5</c>, rows set to length 1 by <c>10112ff1</c>), so the streaks fly in the emitter's frame.
///
/// Drawing (<c>1002a350</c>): d = tail - head; side1 = |d x (0, 1, 0)| (else (1, 0, 0)), side2 = |side1 x d|
/// (else (0, 1, 0)). Two quads, on side2 then side1: head -/+ side * head width, tail -/+ side * tail
/// width, the head pair in the head colour and the tail pair in the tail colour, u 0 at the head and 1 at
/// the tail, v 0 on the minus side (flag 0x100: u across, v 1 at the head). A zero-length streak is
/// skipped.
///
/// Stock blends the two colours across each quad per vertex: the head pair takes the head colour, the
/// tail pair the tail's. The port does the same with vertex colours, every quad in one draw.
/// </summary>
public sealed class GfxControlTParticle : GfxControl
{
    readonly TParticleSim _sim;
    readonly Texture2D _texture;
    readonly bool _localMode;
    readonly EffectBillboardBatch.Strip _strip;
    Matrix4x4 _visual = Matrix4x4.identity;

    public TParticleSim Sim => _sim;

    public GfxControlTParticle(GfxTweakRecord record, EffectLocator locator, Texture2D texture)
        : base(record, locator)
    {
        _texture = texture;
        _sim = new TParticleSim(record?.Fields, () => Random.value);
        _localMode = (_sim.Flags & 2) != 0;
        base.SetDuration(InfiniteDuration);

        int quads = _sim.Particles.Length * 2;
        _strip = new EffectBillboardBatch.Strip
        {
            Positions = new Vector3[quads * 4],
            Uvs = new Vector2[quads * 4],
            Colors = new Color32[quads * 4],
            Color = Color.white,
            Texture = texture,
            Additive = _sim.Additive,
            Quads = true,
        };
    }

    /// <summary>Stock slot 6 is the base one (<c>100a76f0</c>): ready at once.</summary>
    protected override void OnTerminateGracefully() => ReadyFlag = true;

    protected override void OnArmed() => Body(0f);

    protected override void OnProcess(float dt) => Body(dt);

    void Body(float dt)
    {
        if (Locator == null || !Locator.TryResolve(out Matrix4x4 m))
        {
            ReadyFlag = true;
            return;
        }

        if (_localMode)
        {
            var o = Matrix4x4.identity;
            o.SetColumn(0, ((Vector3)m.GetColumn(0)).normalized);
            o.SetColumn(1, ((Vector3)m.GetColumn(1)).normalized);
            o.SetColumn(2, ((Vector3)m.GetColumn(2)).normalized);
            o.SetColumn(3, m.GetColumn(3));
            _visual = o;
        }
        else
        {
            _visual = Matrix4x4.identity;
        }

        if (!_sim.Advance(dt))
            ReadyFlag = true;
    }

    public override void CollectStrips(List<EffectBillboardBatch.Strip> dest, Camera camera)
    {
        if (!IsAlive || dest == null || _texture == null)
            return;

        TParticleSim.Particle[] particles = _sim.Particles;
        bool swap = _sim.SwapUv;
        Color32 headColour = EffectBillboardBatch.ToColor32(_sim.HeadArgb);
        Color32 tailColour = EffectBillboardBatch.ToColor32(_sim.TailArgb);
        int count = 0;

        for (int i = 0; i < particles.Length; i++)
        {
            TParticleSim.Particle p = particles[i];
            var head = new Vector3(p.X, p.Y, p.Z);
            var tail = new Vector3(p.TX, p.TY, p.TZ);
            Vector3 d = tail - head;
            if (d == Vector3.zero)
                continue;
            d.Normalize();

            Vector3 side1 = Vector3.Cross(d, Vector3.up);
            side1 = side1 == Vector3.zero ? Vector3.right : side1.normalized;
            Vector3 side2 = Vector3.Cross(side1, d);
            side2 = side2 == Vector3.zero ? Vector3.up : side2.normalized;

            AddRibbon(head, tail, side2, swap, headColour, tailColour, ref count);
            AddRibbon(head, tail, side1, swap, headColour, tailColour, ref count);
        }

        if (count == 0)
            return;
        _strip.Count = count;
        dest.Add(_strip);
    }

    /// <summary>One quad: the head pair in the head colour and width, the tail pair in the tail's.</summary>
    void AddRibbon(Vector3 head, Vector3 tail, Vector3 side, bool swap, Color32 headColour, Color32 tailColour, ref int count)
    {
        float wa = _sim.HeadWidth, wb = _sim.TailWidth;
        EffectBillboardBatch.Strip strip = _strip;
        int v = count;
        strip.Positions[v] = _visual.MultiplyPoint3x4(head - side * wa);
        strip.Positions[v + 1] = _visual.MultiplyPoint3x4(head + side * wa);
        strip.Positions[v + 2] = _visual.MultiplyPoint3x4(tail - side * wb);
        strip.Positions[v + 3] = _visual.MultiplyPoint3x4(tail + side * wb);
        strip.Colors[v] = headColour;
        strip.Colors[v + 1] = headColour;
        strip.Colors[v + 2] = tailColour;
        strip.Colors[v + 3] = tailColour;
        // D3D (tu, tv) with tv flipped for Unity.
        if (!swap)
        {
            strip.Uvs[v] = new Vector2(0f, 1f);
            strip.Uvs[v + 1] = new Vector2(0f, 0f);
            strip.Uvs[v + 2] = new Vector2(1f, 1f);
            strip.Uvs[v + 3] = new Vector2(1f, 0f);
        }
        else
        {
            strip.Uvs[v] = new Vector2(0f, 0f);
            strip.Uvs[v + 1] = new Vector2(1f, 0f);
            strip.Uvs[v + 2] = new Vector2(0f, 1f);
            strip.Uvs[v + 3] = new Vector2(1f, 1f);
        }
        count = v + 4;
    }
}
