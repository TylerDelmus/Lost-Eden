using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// typeCode 1007 (0x3ef), stock <c>_GfxControlNano0_t</c> driving its <c>GfxVisualSprite2Type0</c>. The
/// mechanics are <see cref="Nano0Sim"/>; this binds them to a locator and draws the result as
/// <see cref="GfxControlSparks"/> does. Stock builds it by position (<c>100e73ea</c>) for Tracer3's
/// trail, and the tracer moves it with slot 4 (the locator's <c>101064f7</c>, the base
/// <see cref="GfxControl.UpdatePosition"/>).
///
/// Field 0 bit 1 puts the sprites in the locator's frame, with the visual placed on the locator
/// (<c>10106306</c> / <c>101062d5</c>); otherwise they are spawned in world space and stay where they
/// were emitted. No record that reaches a nano uses it.
/// </summary>
public sealed class GfxControlNano0 : GfxControl
{
    readonly Nano0Sim _sim;
    readonly Texture2D _atlas;
    readonly EffectAtlasFrames _frames;
    readonly int _cols;
    readonly int _rows;
    readonly float[] _axes = new float[9];
    Matrix4x4 _visual = Matrix4x4.identity;

    public Nano0Sim Sim => _sim;

    public GfxControlNano0(
        GfxTweakRecord record,
        EffectLocator locator,
        Texture2D atlas,
        EffectAtlasFrames frames,
        int cols,
        int rows,
        int firstFrame,
        int lastFrame)
        : base(record, locator)
    {
        _atlas = atlas;
        _frames = frames;
        _cols = Mathf.Max(1, cols);
        _rows = Mathf.Max(1, rows);

        bool local = record != null && (record.FieldInt(0, 0) & Nano0Sim.FlagLocal) != 0;
        Matrix4x4 world = Matrix4x4.identity;
        bool resolved = locator != null && locator.TryResolve(out world);
        Vector3 origin = local || !resolved ? Vector3.zero : (Vector3)world.GetColumn(3);
        float[] axes = local || !resolved ? null : Axes(world);
        _sim = new Nano0Sim(
            record?.Fields,
            firstFrame,
            lastFrame,
            () => Random.value,
            () => Random.Range(0, 0x8000),
            origin.x, origin.y, origin.z,
            axes);
        // Expiry is the stock rule on the sim's duration (see Body), not the base timer.
        base.SetDuration(InfiniteDuration);
    }

    float[] Axes(Matrix4x4 world)
    {
        Vector3 x = world.GetColumn(0), y = world.GetColumn(1), z = world.GetColumn(2);
        _axes[0] = x.x; _axes[1] = x.y; _axes[2] = x.z;
        _axes[3] = y.x; _axes[4] = y.y; _axes[5] = y.z;
        _axes[6] = z.x; _axes[7] = z.y; _axes[8] = z.z;
        return _axes;
    }

    /// <summary>Stock slot 8 (<c>100e66c2</c>): +0x10 = seconds.</summary>
    public override void SetDuration(float seconds)
    {
        if (_sim != null)
            _sim.Duration = seconds;
    }

    public override void SetStartColor(float a, float r, float g, float b) => _sim.SetStartColor(a, r, g, b);

    public override void SetStopColor(float a, float r, float g, float b) => _sim.SetStopColor(a, r, g, b);

    /// <summary>Stock slot 13 (<c>100e670d</c>): start = the colour, stop = the colour at alpha 0.</summary>
    public override void SetColor(uint argb) => SetColorAsStartAndFadeOut(argb);

    protected override void OnTerminateGracefully() => _sim.TerminateGracefully(Age);

    protected override void OnArmed() => Body(0f);

    protected override void OnProcess(float dt) => Body(dt);

    void Body(float dt)
    {
        // _GfxControl_t::Process (100d2a86): ready once 0 <= duration < age.
        if (0f <= _sim.Duration && _sim.Duration < Age)
        {
            ReadyFlag = true;
            return;
        }

        if (Locator == null || !Locator.TryResolve(out Matrix4x4 world))
        {
            // 100e6fa3: every call with the locator lost, duration = age + field 35.
            _sim.TerminateGracefully(Age);
            world = _visual;
        }

        Vector3 wind = EffectWind.Raw;
        bool ready;
        if (_sim.Local)
        {
            _visual = world;
            ready = _sim.Step(Age, dt, 0f, 0f, 0f, null, wind.x, wind.y, wind.z);
        }
        else
        {
            _visual = Matrix4x4.identity;
            Vector3 o = world.GetColumn(3);
            ready = _sim.Step(Age, dt, o.x, o.y, o.z, Axes(world), wind.x, wind.y, wind.z);
        }

        if (ready)
            ReadyFlag = true;
    }

    public override void CollectBillboards(List<EffectBillboardBatch.Quad> dest, Camera camera)
    {
        if (!IsAlive || dest == null || camera == null || _atlas == null || _sim.LiveCount == 0)
            return;

        Vector3 right = camera.transform.right;
        Vector3 up = camera.transform.up;
        Sprite2Type0Visual.Sprite[] sprites = _sim.Sprites;
        for (int i = 0; i < sprites.Length; i++)
        {
            ref Sprite2Type0Visual.Sprite s = ref sprites[i];
            if (!s.Alive)
                continue;

            Texture2D frameTex = _frames != null
                ? _frames.GetFrame(_atlas, _cols, _rows, Sprite2Type0Visual.Cell(s.Frame, _cols, _rows))
                : _atlas;
            if (frameTex == null)
                continue;

            Vector3 p = _visual.MultiplyPoint3x4(new Vector3(s.X, s.Y, s.Z));
            uint argb = s.Argb;
            dest.Add(new EffectBillboardBatch.Quad
            {
                Matrix = Matrix4x4.Translate(p),
                UseAxes = true,
                AxisX = right * s.Width,
                AxisY = up * s.Height,
                Color = new Color(
                    ((argb >> 16) & 0xff) / 255f,
                    ((argb >> 8) & 0xff) / 255f,
                    (argb & 0xff) / 255f,
                    ((argb >> 24) & 0xff) / 255f),
                Texture = frameTex,
                Additive = _sim.Additive,
            });
        }
    }
}
