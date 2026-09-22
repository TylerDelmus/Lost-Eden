using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// typeCode 2002 (0x7d2), stock <c>_GfxControlPlasma_t</c>: a wavy energy strip between a hit
/// location's two ends, redrawn every frame. The mechanics are <see cref="PlasmaSim"/>; this follows
/// the hit location and hands the strip to the batch.
/// </summary>
public sealed class GfxControlPlasma : GfxControl
{
    readonly PlasmaSim _sim;
    readonly EffectHitLocation _hitLocation;
    readonly float[] _positions = new float[PlasmaSim.VertexCount * 3];
    readonly float[] _uvs = new float[PlasmaSim.VertexCount * 2];
    readonly EffectBillboardBatch.Strip _strip;

    Vector3 _start;
    Vector3 _extent;
    float _time;
    uint _argb;
    bool _placed;

    public PlasmaSim Sim => _sim;
    public Vector3 Start => _start;
    public Vector3 Extent => _extent;

    public GfxControlPlasma(
        GfxTweakRecord record,
        EffectLocator locator,
        EffectHitLocation hitLocation,
        Texture2D texture)
        : base(record, locator)
    {
        _hitLocation = hitLocation;
        _sim = new PlasmaSim(record?.Fields);
        base.SetDuration(_sim.Duration);
        _strip = new EffectBillboardBatch.Strip
        {
            Positions = new Vector3[PlasmaSim.VertexCount],
            Uvs = new Vector2[PlasmaSim.VertexCount],
            Texture = texture,
            Additive = true,
        };

        // Stock's first Process only arms the base timer (age 0) and still runs this body; the port's
        // base skips OnProcess on that call.
        Place(0f);
    }

    /// <summary>Stock slot 8: +0x10 = seconds.</summary>
    public override void SetDuration(float seconds)
    {
        base.SetDuration(seconds);
        _sim.Duration = seconds;
    }

    /// <summary>Stock slot 6 (<c>100ec3bb</c>): duration = 0, so the next Process readies it.</summary>
    protected override void OnTerminateGracefully()
    {
        base.SetDuration(0f);
        _sim.Duration = 0f;
    }

    public override void SetStartColor(float a, float r, float g, float b) => _sim.SetStartColor(a, r, g, b);

    public override void SetStopColor(float a, float r, float g, float b) => _sim.SetStopColor(a, r, g, b);

    protected override void OnProcess(float dt)
    {
        // _GfxControl_t::Process readies the control before the body once 0 <= duration < age.
        if (Duration >= 0f && Duration < Age)
        {
            ReadyFlag = true;
            return;
        }

        Place(Age);
    }

    void Place(float age)
    {
        if (_hitLocation == null || !_hitLocation.TryGetEndpoints(out Vector3 start, out Vector3 end))
        {
            ReadyFlag = true;
            return;
        }

        _start = start;
        _extent = end - start;
        _time = age;
        _argb = _sim.ColorAt(age);
        _placed = true;
    }

    public override void CollectStrips(List<EffectBillboardBatch.Strip> dest, Camera camera)
    {
        if (!IsAlive || !_placed || dest == null || camera == null || _strip.Texture == null)
            return;

        // The point one unit in front of the camera, in the visual's frame (placed at the start,
        // unrotated): D3D camera space looks down +z, which is the Unity camera's forward.
        Transform cam = camera.transform;
        Vector3 q = cam.position + cam.forward - _start;
        _sim.BuildStrip(
            _time,
            _extent.x, _extent.y, _extent.z,
            q.x, q.y, q.z,
            () => Random.Range(0, 0x8000),
            _positions,
            _uvs);

        for (int i = 0; i < PlasmaSim.VertexCount; i++)
        {
            _strip.Positions[i] = new Vector3(
                _start.x + _positions[i * 3],
                _start.y + _positions[i * 3 + 1],
                _start.z + _positions[i * 3 + 2]);
            // D3D tv runs down the image; Unity's v runs up.
            _strip.Uvs[i] = new Vector2(_uvs[i * 2], 1f - _uvs[i * 2 + 1]);
        }

        _strip.Count = PlasmaSim.VertexCount;
        _strip.Color = new Color(
            ((_argb >> 16) & 0xff) / 255f,
            ((_argb >> 8) & 0xff) / 255f,
            (_argb & 0xff) / 255f,
            ((_argb >> 24) & 0xff) / 255f);
        dest.Add(_strip);
    }
}
