using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// typeCode 3017 (0xbc9), stock <c>_GfxControlGlobalSmoke_t</c>. The rules are
/// <see cref="GlobalSmokeSim"/>; this runs them at stock's call rate and draws the 64 sprites the way
/// <c>GfxVisualSol</c> does — the same camera-facing quad Suns uses (§5.10), so the axis maths here is
/// the same as <see cref="GfxControlSuns"/>'s.
///
/// Stock spawns a fixed number of particles **per Process call**, so the emitter's density depends on
/// how often it is called. The port replays it at <see cref="EffectFrameRate.StockProcessHz"/> for the
/// same reason BParticle and Suns do.
/// </summary>
public sealed class GfxControlGlobalSmoke : GfxControl
{
    const int MaxStockStepsPerFrame = 8;

    readonly GlobalSmokeSim _sim;
    readonly Texture2D _atlas;
    readonly EffectAtlasFrames _frames;
    readonly int _cols, _rows;
    float _carry;
    float _age;
    Vector3 _emitter;

    public GlobalSmokeSim Sim => _sim;

    public GfxControlGlobalSmoke(
        GfxTweakRecord record,
        EffectLocator locator,
        Texture2D atlas,
        EffectAtlasFrames frames,
        int cols,
        int rows)
        : base(record, locator)
    {
        _atlas = atlas;
        _frames = frames;
        _cols = Mathf.Max(1, cols);
        _rows = Mathf.Max(1, rows);
        _sim = new GlobalSmokeSim(record?.Fields, () => Random.value)
        {
            // 100e0841: the build reads the material's frame count once and scales the particle's age
            // by it, so a one-cell material simply stays on frame 0.
            FrameCount = _cols * _rows,
        };
        base.SetDuration(_sim.Duration);
    }

    protected override void OnArmed() => Step(0f);

    protected override void OnProcess(float dt) => Step(dt);

    void Step(float dt)
    {
        if (!_sim.Supported)
        {
            ReadyFlag = true;
            return;
        }

        if (Locator != null && Locator.TryResolve(out Matrix4x4 m))
            _emitter = m.GetColumn(3);
        else if ((_sim.Flags & 1) == 0)
        {
            // 100dfc7f: a locator that has gone readies the control.
            ReadyFlag = true;
            return;
        }

        float step = EffectFrameRate.StockProcessSeconds;
        int steps = EffectFrameRate.TakeFixedSteps(ref _carry, dt, step, MaxStockStepsPerFrame);
        for (int s = 0; s < steps; s++)
        {
            _age += step;
            _sim.Process(_age, step, _emitter.x, _emitter.y, _emitter.z);
        }

        // The base's own expiry still applies; stock just stops spawning first (100dfd15).
        if (_sim.Duration >= 0f && _sim.Duration < _age)
            ReadyFlag = true;
    }

    public override void CollectBillboards(List<EffectBillboardBatch.Quad> dest, Camera camera)
    {
        if (!IsAlive || dest == null || _atlas == null || camera == null || !_sim.Supported)
            return;

        Transform cam = camera.transform;
        Vector3 right = cam.right, up = cam.up;
        GlobalSmokeSim.Particle[] particles = _sim.Particles;
        for (int i = 0; i < particles.Length; i++)
        {
            ref GlobalSmokeSim.Particle p = ref particles[i];
            if (!p.Alive)
                continue;

            Texture2D frameTex = _frames != null
                ? _frames.GetFrame(_atlas, _cols, _rows, (int)p.Frame)
                : _atlas;
            if (frameTex == null)
                continue;

            // 10020831, the same quad Suns draws: the sprite leans by its own screen angle.
            float sin = Mathf.Sin(p.Angle), cos = Mathf.Cos(p.Angle);
            float hw = p.Width * 0.5f, hh = p.Height * 0.5f;
            Vector3 a = right * (sin * hw) + up * (cos * hh);
            Vector3 b = right * (-cos * hh) + up * (sin * hw);

            uint argb = p.Argb;
            dest.Add(new EffectBillboardBatch.Quad
            {
                Matrix = Matrix4x4.TRS(new Vector3(p.X, p.Y, p.Z), Quaternion.identity, Vector3.one),
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
