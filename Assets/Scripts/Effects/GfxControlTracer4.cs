using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// typeCode 1024 (0x400), stock <c>_GfxControlTracer4_t</c>: three twisting ribbons along the line
/// from a hit location's start to its end. The mechanics are <see cref="Tracer4Sim"/>; each ribbon is
/// drawn the way GfxVisualCord4 draws its links (<see cref="Cord4Strip"/>), additive.
/// </summary>
public sealed class GfxControlTracer4 : GfxControl
{
    readonly Tracer4Sim _sim;
    readonly Texture2D _texture;
    readonly EffectBillboardBatch.Strip[] _strips = new EffectBillboardBatch.Strip[Tracer4Sim.Ribbons];
    readonly float[] _camera = new float[Tracer4Sim.Links * 3];
    readonly float[] _sizes = new float[Tracer4Sim.Links];
    readonly float[] _lives = new float[Tracer4Sim.Links];
    readonly float[] _positions = new float[2 * (Tracer4Sim.Links - 1) * 3];
    readonly float[] _uvs = new float[2 * (Tracer4Sim.Links - 1) * 2];
    Matrix4x4 _visual = Matrix4x4.identity;

    public Tracer4Sim Sim => _sim;

    public GfxControlTracer4(
        GfxTweakRecord record,
        EffectLocator locator,
        Vector3 start,
        Vector3 end,
        Texture2D texture)
        : base(record, locator)
    {
        _texture = texture;
        _sim = new Tracer4Sim(record?.Fields, start.x, start.y, start.z, end.x, end.y, end.z);
        base.SetDuration(record != null ? record.Field(8, InfiniteDuration) : InfiniteDuration);

        for (int i = 0; i < Tracer4Sim.Links; i++)
        {
            _sizes[i] = _sim.LinkSize;
            _lives[i] = Tracer4Sim.LinkLife;
        }
        for (int k = 0; k < _strips.Length; k++)
        {
            _strips[k] = new EffectBillboardBatch.Strip
            {
                Positions = new Vector3[2 * (Tracer4Sim.Links - 1)],
                Uvs = new Vector2[2 * (Tracer4Sim.Links - 1)],
                Texture = texture,
                Additive = true,
            };
        }

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

        // Stock's first Process only arms the base timer (dt 0) and still runs this body.
        Step(0f);
    }

    /// <summary>Stock slot 8 on this class is a no-op.</summary>
    public override void SetDuration(float seconds) { }

    /// <summary>Stock slot 6 (<c>100ffca7</c>) readies the control at once.</summary>
    protected override void OnTerminateGracefully() => ReadyFlag = true;

    protected override void OnProcess(float dt)
    {
        // _GfxControl_t::Process readies the control before the body once 0 <= duration < age.
        if (Duration >= 0f && Duration < Age)
        {
            ReadyFlag = true;
            return;
        }
        Step(dt);
    }

    void Step(float dt) => _sim.Step(dt, () => Random.Range(0, 0x8000), () => Random.value);

    public override void CollectStrips(List<EffectBillboardBatch.Strip> dest, Camera camera)
    {
        if (!IsAlive || dest == null || camera == null || _texture == null)
            return;

        Matrix4x4 toCamera = camera.worldToCameraMatrix * _visual;
        Matrix4x4 fromWorld = _visual.inverse;
        Vector3 right = fromWorld.MultiplyVector(camera.transform.right);
        Vector3 up = fromWorld.MultiplyVector(camera.transform.up);
        uint argb = _sim.Argb;
        var color = new Color(
            ((argb >> 16) & 0xff) / 255f,
            ((argb >> 8) & 0xff) / 255f,
            (argb & 0xff) / 255f,
            ((argb >> 24) & 0xff) / 255f);

        for (int k = 0; k < _strips.Length; k++)
        {
            float[] links = _sim.LinkPositions(k);
            for (int i = 0; i < Tracer4Sim.Links; i++)
            {
                // Unity camera space looks down -z; stock's D3D view space looks down +z.
                Vector3 c = toCamera.MultiplyPoint3x4(new Vector3(links[i * 3], links[i * 3 + 1], links[i * 3 + 2]));
                _camera[i * 3] = c.x;
                _camera[i * 3 + 1] = c.y;
                _camera[i * 3 + 2] = -c.z;
            }

            int count = Cord4Strip.Build(
                Tracer4Sim.Links, links, _camera,
                right.x, right.y, right.z, up.x, up.y, up.z,
                _sizes, _lives, 1f, lifeV: true,
                _positions, _uvs);

            EffectBillboardBatch.Strip strip = _strips[k];
            for (int v = 0; v < count; v++)
            {
                strip.Positions[v] = _visual.MultiplyPoint3x4(
                    new Vector3(_positions[v * 3], _positions[v * 3 + 1], _positions[v * 3 + 2]));
                // D3D tv runs down the image; Unity's v runs up.
                strip.Uvs[v] = new Vector2(_uvs[v * 2], 1f - _uvs[v * 2 + 1]);
            }
            strip.Count = count;
            strip.Color = color;
            dest.Add(strip);
        }
    }
}
