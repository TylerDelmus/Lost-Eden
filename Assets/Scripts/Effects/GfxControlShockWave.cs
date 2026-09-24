using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// typeCode 3000 (0xbb8), stock <c>_GfxControlShockWave_t</c>: ground rings spreading one after another
/// and thin cones flashing up with each. The rules are <see cref="ShockWaveSim"/>; this feeds it the
/// centre and draws what it builds.
///
/// Built on a dynel (<c>100eeec0</c>), the centre is the dynel's own position (attach 0), followed every
/// call with field 0 bit 0; built on a point it stays there. Both visuals draw a triangle strip of
/// 2N + 2 FVF 0x142 vertices, texture × vertex colour, no Z write, no culling, the texture wrapping:
/// <list type="bullet">
/// <item><c>GfxVisualGroundRing</c> (<c>10017051</c>): SrcAlpha/One with flag 0x800, which every record
/// sets. Without it stock blends Zero/SrcColor (a darkening multiply); no record takes that path, so it
/// isn't ported. Nor is flag 0x8000, which no record sets (Docs §9).</item>
/// <item><c>GfxVisualCone</c> (<c>1000bf1e</c>): SrcAlpha/One (ShockWave builds it additive), each cone
/// upright at its own position.</item>
/// </list>
/// </summary>
public sealed class GfxControlShockWave : GfxControl
{
    readonly ShockWaveSim _sim;
    readonly EffectLocator _centre;
    readonly bool _follow;
    readonly Texture2D _ringTexture;
    readonly Texture2D _coneTexture;
    readonly EffectBillboardBatch.Strip[] _ringStrips;
    readonly EffectBillboardBatch.Strip[] _coneStrips;
    Vector3 _position;

    public ShockWaveSim Sim => _sim;

    public GfxControlShockWave(GfxTweakRecord record, EffectLocator locator, Texture2D ringTexture, Texture2D coneTexture)
        : base(record, locator)
    {
        _ringTexture = ringTexture;
        _coneTexture = coneTexture;

        // The dynel's own position (Vehicle_t::GetGlobalPos): attach 0, without the record's template.
        _centre = locator?.WithAttach(0);
        _follow = record != null && (record.FieldInt(0, 0) & ShockWaveSim.FlagFollow) != 0
            && locator != null && !locator.IsWorldPoint;
        _position = Centre(Vector3.zero);
        _sim = new ShockWaveSim(record?.Fields, _position.x, _position.y, _position.z, EffectGround.HeightAt);
        // Expiry is the sim's: the rings keep the control alive past the base duration.
        base.SetDuration(InfiniteDuration);

        _ringStrips = new EffectBillboardBatch.Strip[_sim.Rings.Length];
        for (int i = 0; i < _ringStrips.Length; i++)
            _ringStrips[i] = NewStrip(_sim.Segments, _ringTexture, _sim.RingsAdditive);
        _coneStrips = new EffectBillboardBatch.Strip[_sim.Cones.Length];
        for (int j = 0; j < _coneStrips.Length; j++)
            _coneStrips[j] = NewStrip(_sim.ConeSegments, _coneTexture, additive: true);
    }

    static EffectBillboardBatch.Strip NewStrip(int segments, Texture texture, bool additive)
    {
        int vertices = 2 * segments + 2;
        return new EffectBillboardBatch.Strip
        {
            Positions = new Vector3[vertices],
            Uvs = new Vector2[vertices],
            Colors = new Color32[vertices],
            Color = Color.white,
            Texture = texture,
            Additive = additive,
        };
    }

    Vector3 Centre(Vector3 fallback)
    {
        if (_centre != null && _centre.TryResolve(out Matrix4x4 m))
            return m.GetColumn(3);
        return fallback;
    }

    /// <summary>Stock slot 8 (<c>1010340b</c>): +0x10, the rings' life.</summary>
    public override void SetDuration(float seconds)
    {
        if (_sim != null)
            _sim.Life = seconds;
    }

    /// <summary>Stock slot 6 (the base <c>100a76f0</c>): the ready flag, which a running ring clears.</summary>
    protected override void OnTerminateGracefully() => _sim.Terminate();

    // Stock's first Process arms the control and still runs the body at age 0.
    protected override void OnArmed() => Body();

    protected override void OnProcess(float dt) => Body();

    void Body()
    {
        // 100ee538: with bit 0 the centre is the dynel's position every call.
        if (_follow)
            _position = Centre(_position);
        if (_sim.Step(Age, _position.x, _position.y, _position.z))
            ReadyFlag = true;
    }

    public override void CollectStrips(List<EffectBillboardBatch.Strip> dest, Camera camera)
    {
        if (!IsAlive || dest == null)
            return;

        for (int i = 0; i < _sim.Rings.Length; i++)
        {
            ShockWaveSim.Ring ring = _sim.Rings[i];
            if (!ring.Alive || !ring.Drawn || _ringTexture == null)
                continue;
            Fill(_ringStrips[i], new Vector3(ring.X, ring.Y, ring.Z), ring.Vertices, ring.Uvs, ring.Inner, ring.Outer);
            dest.Add(_ringStrips[i]);
        }

        for (int j = 0; j < _sim.Cones.Length; j++)
        {
            ShockWaveSim.Cone cone = _sim.Cones[j];
            if (!cone.Alive || _coneTexture == null)
                continue;
            Fill(_coneStrips[j], new Vector3(cone.X, cone.Y, cone.Z), cone.Vertices, cone.Uvs, cone.Bottom, cone.Top);
            dest.Add(_coneStrips[j]);
        }
    }

    /// <summary>Pairs (first, second) in strip order; D3D v runs down the image, Unity's up.</summary>
    static void Fill(EffectBillboardBatch.Strip strip, Vector3 origin, float[] vertices, float[] uvs, uint first, uint second)
    {
        int count = vertices.Length / 3;
        Color32 a = EffectBillboardBatch.ToColor32(first), b = EffectBillboardBatch.ToColor32(second);
        for (int v = 0; v < count; v++)
        {
            strip.Positions[v] = origin + new Vector3(vertices[v * 3], vertices[v * 3 + 1], vertices[v * 3 + 2]);
            strip.Uvs[v] = new Vector2(uvs[v * 2], 1f - uvs[v * 2 + 1]);
            strip.Colors[v] = (v & 1) == 0 ? a : b;
        }
        strip.Count = count;
    }
}
