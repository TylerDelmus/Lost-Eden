using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// typeCode 1009 (0x3f1), stock <c>_GfxControlSmoke_t</c> driving its <c>GfxVisualSprite2Type0</c>. The
/// mechanics are <see cref="SmokeSim"/>; this binds them to a locator and draws the result.
///
/// Field 0 bit 1 puts the sprites in the locator's frame: stock then spawns them through an identity
/// matrix (<c>101063d9</c>) and places the visual on the locator (<c>10106306</c> / <c>101062d5</c>).
/// Otherwise they are spawned through the locator's world matrix and stay where they were emitted.
///
/// Each sprite is an upright camera-facing quad (<c>10025391</c>) of width × height, centred on the
/// sprite, coloured by its ARGB and textured with its cell. The visual alpha-blends (SrcAlpha,
/// InvSrcAlpha) except for effect 80005, which is additive; no Z-write, no culling. Stock Smoke has no
/// light.
/// </summary>
public sealed class GfxControlSmoke : GfxControl
{
    readonly SmokeSim _sim;
    readonly Texture2D _atlas;
    readonly EffectAtlasFrames _frames;
    readonly int _cols;
    readonly int _rows;
    readonly float[] _axes = new float[9];
    Matrix4x4 _visual = Matrix4x4.identity;

    public SmokeSim Sim => _sim;

    public GfxControlSmoke(
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
        _sim = new SmokeSim(
            record?.Fields,
            record != null ? record.Id : 0,
            firstFrame,
            lastFrame,
            () => Random.value);
        // Expiry is the stock rule on the sim's duration (see Body), not the base timer.
        base.SetDuration(InfiniteDuration);
    }

    /// <summary>Stock slot 8 (<c>100f08b1</c>): +0x10 = seconds.</summary>
    public override void SetDuration(float seconds)
    {
        if (_sim != null)
            _sim.Duration = seconds;
    }

    public override void SetStartColor(float a, float r, float g, float b) => _sim.SetStartColor(a, r, g, b);

    public override void SetStopColor(float a, float r, float g, float b) => _sim.SetStopColor(a, r, g, b);

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
            // 100f04e4: a lost locator lets the sprites in flight finish, unless flag 0x400 keeps it going.
            if (!_sim.KeepsRunningWhenLost)
                _sim.TerminateGracefully(Age);
            world = _visual;
        }

        // ProcessSprites gets the handler's GetSmoothWind (100f081a, +0x30).
        Vector3 wind = EffectWind.Smooth;
        if (_sim.Local)
        {
            _visual = world;
            _sim.Step(Age, dt, 0f, 0f, 0f, null, wind.x, wind.y, wind.z);
        }
        else
        {
            _visual = Matrix4x4.identity;
            Vector3 x = world.GetColumn(0), y = world.GetColumn(1), z = world.GetColumn(2);
            _axes[0] = x.x; _axes[1] = x.y; _axes[2] = x.z;
            _axes[3] = y.x; _axes[4] = y.y; _axes[5] = y.z;
            _axes[6] = z.x; _axes[7] = z.y; _axes[8] = z.z;
            Vector3 o = world.GetColumn(3);
            _sim.Step(Age, dt, o.x, o.y, o.z, _axes, wind.x, wind.y, wind.z);
        }
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
