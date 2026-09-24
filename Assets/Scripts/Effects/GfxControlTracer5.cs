using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// typeCode 1025 (0x401), stock <c>_GfxControlTracer5_t</c>: one streak flying along the line from a hit
/// location's start to its end. The mechanics are <see cref="Tracer5Sim"/>; the streak is drawn the way
/// GfxVisualCord4 draws its three links (<see cref="Cord4Strip"/>), additive, with v from 0 at the head to
/// ~1 at the tail.
/// </summary>
public sealed class GfxControlTracer5 : GfxControl
{
    readonly Tracer5Sim _sim;
    readonly Texture2D _texture;
    readonly EffectBillboardBatch.Strip _strip;
    readonly float[] _camera = new float[Tracer5Sim.Links * 3];
    readonly float[] _sizes = new float[Tracer5Sim.Links];
    readonly float[] _positions = new float[2 * (Tracer5Sim.Links - 1) * 3];
    readonly float[] _uvs = new float[2 * (Tracer5Sim.Links - 1) * 2];
    Matrix4x4 _visual = Matrix4x4.identity;

    public Tracer5Sim Sim => _sim;

    public GfxControlTracer5(
        GfxTweakRecord record,
        EffectLocator locator,
        Vector3 start,
        Vector3 end,
        Texture2D texture)
        : base(record, locator)
    {
        _texture = texture;
        _sim = new Tracer5Sim(record?.Fields, start.x, start.y, start.z, end.x, end.y, end.z);
        base.SetDuration(record != null ? record.Field(8, InfiniteDuration) : InfiniteDuration);

        for (int i = 0; i < Tracer5Sim.Links; i++)
            _sizes[i] = _sim.Width;
        _strip = new EffectBillboardBatch.Strip
        {
            Positions = new Vector3[2 * (Tracer5Sim.Links - 1)],
            Uvs = new Vector2[2 * (Tracer5Sim.Links - 1)],
            Texture = texture,
            Additive = true,
        };

        if (_sim.Degenerate)
        {
            ReadyFlag = true;
            return;
        }

        // The visual turns and sits with the locator in local mode (101062d5 / 10106306).
        if (_sim.Local)
        {
            float[] b = _sim.Basis;
            _visual.SetColumn(0, new Vector4(b[0], b[1], b[2], 0f));
            _visual.SetColumn(1, new Vector4(b[3], b[4], b[5], 0f));
            _visual.SetColumn(2, new Vector4(b[6], b[7], b[8], 0f));
            _visual.SetColumn(3, new Vector4(_sim.OriginX, _sim.OriginY, _sim.OriginZ, 1f));
        }
    }

    /// <summary>Stock slot 8 on this class is a no-op.</summary>
    public override void SetDuration(float seconds) { }

    /// <summary>Stock slot 6 (<c>101003f4</c>) readies the control at once.</summary>
    protected override void OnTerminateGracefully() => ReadyFlag = true;

    protected override void OnArmed() => _sim.Step(0f);

    protected override void OnProcess(float dt)
    {
        // _GfxControl_t::Process readies the control before the body once 0 <= duration < age.
        if (Duration >= 0f && Duration < Age)
        {
            ReadyFlag = true;
            return;
        }
        _sim.Step(Age);
        if (_sim.Arrived)
            ReadyFlag = true;
    }

    public override void CollectStrips(List<EffectBillboardBatch.Strip> dest, Camera camera)
    {
        if (!IsAlive || dest == null || camera == null || _texture == null)
            return;

        Matrix4x4 toCamera = camera.worldToCameraMatrix * _visual;
        Matrix4x4 fromWorld = _visual.inverse;
        Vector3 right = fromWorld.MultiplyVector(camera.transform.right);
        Vector3 up = fromWorld.MultiplyVector(camera.transform.up);
        uint argb = _sim.Argb;

        float[] links = _sim.LinkPositions;
        for (int i = 0; i < Tracer5Sim.Links; i++)
        {
            // Unity camera space looks down -z; stock's D3D view space looks down +z.
            Vector3 c = toCamera.MultiplyPoint3x4(new Vector3(links[i * 3], links[i * 3 + 1], links[i * 3 + 2]));
            _camera[i * 3] = c.x;
            _camera[i * 3 + 1] = c.y;
            _camera[i * 3 + 2] = -c.z;
        }

        int count = Cord4Strip.Build(
            Tracer5Sim.Links, links, _camera,
            right.x, right.y, right.z, up.x, up.y, up.z,
            _sizes, Tracer5Sim.LinkLives, 1f, lifeV: true,
            _positions, _uvs);

        for (int v = 0; v < count; v++)
        {
            _strip.Positions[v] = _visual.MultiplyPoint3x4(
                new Vector3(_positions[v * 3], _positions[v * 3 + 1], _positions[v * 3 + 2]));
            // D3D tv runs down the image; Unity's v runs up.
            _strip.Uvs[v] = new Vector2(_uvs[v * 2], 1f - _uvs[v * 2 + 1]);
        }
        _strip.Count = count;
        _strip.Color = new Color(
            ((argb >> 16) & 0xff) / 255f,
            ((argb >> 8) & 0xff) / 255f,
            (argb & 0xff) / 255f,
            ((argb >> 24) & 0xff) / 255f);
        dest.Add(_strip);
    }
}
