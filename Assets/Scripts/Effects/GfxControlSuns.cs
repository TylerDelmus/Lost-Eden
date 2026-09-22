using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// typeCode 2005 (0x7d5), stock <c>_GfxControlSuns_t</c>, sunType 4 (still sparks along a hit location) and
/// sunTypes 0 and 1 (a glow over the locator and a ring round it, stepped every frame since they depend on
/// the age alone).
/// The mechanics are <see cref="SunsSim"/>, replayed call for call at
/// <see cref="EffectFrameRate.StockProcessHz"/>. The sparks are drawn the way <c>GfxVisualSol</c> does
/// (DisplaySystem <c>10020831</c>): a camera-facing quad per sprite, centre P plus Offset along the
/// screen's x, corners P ± A ± B with A = (sin a * w/2, cos a * h/2) and B = (-cos a * h/2,
/// sin a * w/2) in screen axes, texture u along A and v up along B, atlas cell
/// <c>frame % cols, frame / cols</c>, the sprite's ARGB. Additive (the visual is built with its flag
/// set), no Z write, no culling.
/// </summary>
public sealed class GfxControlSuns : GfxControl
{
    const int MaxStockStepsPerFrame = 8;

    readonly SunsSim _sim;
    readonly EffectHitLocation _hitLocation;
    readonly Texture2D _atlas;
    readonly EffectAtlasFrames _frames;
    readonly int _cols;
    readonly int _rows;
    float _carry;
    float _age;
    bool _armed;
    Vector3 _origin;

    public SunsSim Sim => _sim;

    /// <summary>Built on a hit location: stock sets it no duration and lets it run.</summary>
    public bool IsHitLocationTracer => _hitLocation != null;

    public GfxControlSuns(
        GfxTweakRecord record,
        EffectLocator locator,
        EffectHitLocation hitLocation,
        Texture2D atlas,
        EffectAtlasFrames frames,
        int cols,
        int rows)
        : base(record, locator)
    {
        _sim = new SunsSim(record?.Fields, () => Random.Range(0, 0x8000));
        _hitLocation = hitLocation;
        _atlas = atlas;
        _frames = frames;
        _cols = Mathf.Max(1, cols);
        _rows = Mathf.Max(1, rows);
        // Stock expiry is replayed with the steps, not by the base timer.
        base.SetDuration(InfiniteDuration);
    }

    /// <summary>Stock slot 8 (<c>100fd029</c>): +0x10 = seconds.</summary>
    public override void SetDuration(float seconds) => _sim.Duration = seconds;

    /// <summary>Stock slot 6 (<c>100fd021</c>): stop spawning; the sparks run out.</summary>
    protected override void OnTerminateGracefully() => _sim.Terminating = true;

    public override void SetStartColor(float a, float r, float g, float b) => _sim.SetStartColor(a, r, g, b);

    public override void SetStopColor(float a, float r, float g, float b) => _sim.SetStopColor(a, r, g, b);

    /// <summary>Stock slot 13 (<c>100fd09c</c>): the colour as both the start and the stop colour.</summary>
    public override void SetColor(uint argb) => SetColorAsStartAndStop(argb);

    /// <summary>Stock runs the body on the arming call too, at age 0.</summary>
    protected override void OnArmed()
    {
        if (_sim.AgeDriven)
            StepAgeDriven();
    }

    protected override void OnProcess(float dt)
    {
        if (_sim.AgeDriven)
        {
            // Types 0 and 1 depend on the age alone, so they run on every frame as stock's Process does,
            // not on the 30 Hz replay type 4 needs for its per-call spawns (Docs §3.7).
            if (0f <= _sim.Duration && _sim.Duration < Age)
            {
                ReadyFlag = true;
                return;
            }
            StepAgeDriven();
            return;
        }

        if (!_armed)
        {
            _armed = true;
            StepOnce();
            if (_sim.Done)
                ReadyFlag = true;
            return;
        }

        float step = EffectFrameRate.StockProcessSeconds;
        int steps = EffectFrameRate.TakeFixedSteps(ref _carry, dt, step, MaxStockStepsPerFrame);
        for (int s = 0; s < steps; s++)
        {
            _age += step;
            // _GfxControl_t::Process readies it once 0 <= duration < age; Suns then leaves (100fc43a).
            if (_sim.Duration >= 0f && _sim.Duration < _age)
            {
                ReadyFlag = true;
                return;
            }

            StepOnce();
            if (_sim.Done)
            {
                ReadyFlag = true;
                return;
            }
        }
    }

    void StepAgeDriven()
    {
        // 100fc440: a lost locator terminates it; the ring still runs this call at the last position.
        if (Locator != null && Locator.TryResolve(out Matrix4x4 world))
            _origin = (Record.FieldInt(0, 0) & 2) != 0 ? Vector3.zero : (Vector3)world.GetColumn(3);
        else
            _sim.Terminating = true;
        if (_sim.SunType == SunsSim.RingType)
            _sim.StepRing(Age, _origin.x, _origin.y, _origin.z);
        else
            _sim.StepHalo(Age, _origin.x, _origin.y, _origin.z);
        // 100fcfae: terminating readies it after this call.
        if (_sim.Done)
            ReadyFlag = true;
    }

    void StepOnce()
    {
        Vector3 start = default, end = default;
        bool found = _hitLocation != null && _hitLocation.TryGetEndpoints(out start, out end);
        _sim.Step(_age, found, start.x, start.y, start.z, end.x, end.y, end.z);
    }

    public override void CollectBillboards(List<EffectBillboardBatch.Quad> dest, Camera camera)
    {
        if (!IsAlive || dest == null || _atlas == null || camera == null)
            return;

        Transform cam = camera.transform;
        Vector3 right = cam.right, up = cam.up;
        SunsSim.Sprite[] sprites = _sim.Sprites;
        for (int i = 0; i < sprites.Length; i++)
        {
            ref SunsSim.Sprite s = ref sprites[i];
            if (!s.Visible)
                continue;

            Texture2D frameTex = _frames != null ? _frames.GetFrame(_atlas, _cols, _rows, s.Frame) : _atlas;
            if (frameTex == null)
                continue;

            float sin = Mathf.Sin(s.Angle), cos = Mathf.Cos(s.Angle);
            float hw = s.Width * 0.5f, hh = s.Height * 0.5f;
            Vector3 a = right * (sin * hw) + up * (cos * hh);
            Vector3 b = right * (-cos * hh) + up * (sin * hw);
            Vector3 centre = new Vector3(s.X, s.Y, s.Z) + right * s.Offset;

            uint argb = s.Argb;
            dest.Add(new EffectBillboardBatch.Quad
            {
                Matrix = Matrix4x4.TRS(centre, Quaternion.identity, Vector3.one),
                UseAxes = true,
                AxisX = a * 2f,
                AxisY = b * 2f,
                Color = new Color(
                    ((argb >> 16) & 0xff) / 255f,
                    ((argb >> 8) & 0xff) / 255f,
                    (argb & 0xff) / 255f,
                    ((argb >> 24) & 0xff) / 255f),
                Texture = frameTex,
                Additive = true,
            });
        }
    }
}
