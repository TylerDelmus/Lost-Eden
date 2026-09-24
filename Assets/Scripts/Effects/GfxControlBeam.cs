using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// typeCode 3022 (0xbce), stock <c>GfxControlBeam_t</c> with its <c>GfxVisualBeam</c>. The mechanics
/// are <see cref="BeamSim"/> and the geometry <see cref="BeamBlades"/>; this binds them to a locator
/// and draws them.
///
/// The beam stands on the locator, turned by the locator's own rotation with its scale stripped
/// (stock orthonormalises the three basis rows at <c>10109afc</c> before making the quaternion),
/// optionally tipped a quarter turn about x (flag 0x20000), and then spun about its own y by field
/// 30 — or by a random angle re-picked for every take when that field is exactly 999.
///
/// Stock's Process runs the repeat machine and the anchor's fall per call, and both show on screen,
/// so the body is replayed on the fixed stock clock and the drawn state is interpolated between the
/// last two steps (Docs §3.7 group B). The base's own timer is left switched off — Beam never reads
/// <c>+0x14</c>, it only writes it, so the sim is the sole authority on when the control ends.
/// </summary>
public sealed class GfxControlBeam : GfxControl
{
    /// <summary>A stall must not spend a whole magazine of repeats in one frame.</summary>
    const int MaxStepsPerFrame = 4;

    readonly BeamSim _sim;
    readonly BeamBlades _blades;
    readonly Texture2D _texture;
    readonly Texture2D _capTexture;
    readonly EffectBillboardBatch.Strip _strip;
    readonly EffectBillboardBatch.Strip _capStrip;

    float _carry;
    bool _placed;
    Matrix4x4 _world = Matrix4x4.identity;

    public BeamSim Sim => _sim;
    public BeamBlades Blades => _blades;

    public GfxControlBeam(GfxTweakRecord record, EffectLocator locator, Texture2D texture, Texture2D capTexture)
        : base(record, locator)
    {
        _texture = texture;
        _capTexture = capTexture;
        _sim = new BeamSim(record?.Fields, () => Random.value);
        _blades = new BeamBlades(
            _sim.Flags, _sim.BladeCount, _sim.UMax, _sim.VSpan, _sim.SecondQuadHeight, () => Random.value);

        int n = _blades.VertexCount;
        _strip = new EffectBillboardBatch.Strip
        {
            Positions = new Vector3[n],
            Uvs = new Vector2[n],
            Colors = new Color32[n],
            Color = Color.white,
            Texture = texture,
            Additive = _sim.Additive,
            Quads = true,
        };

        if (_capTexture != null && _sim.SecondMaterial >= 0)
        {
            _capStrip = new EffectBillboardBatch.Strip
            {
                Positions = new Vector3[4],
                Uvs = new Vector2[4],
                Colors = new Color32[4],
                Color = Color.white,
                Texture = capTexture,
                Additive = _sim.Additive,
                Quads = true,
                Count = 4,
            };
        }

        // Beam decides its own end; the base timer must not cut it short mid-repeat.
        base.SetDuration(InfiniteDuration);
    }

    /// <summary>Stock slot 8 is the base's (<c>1010340b</c>): <c>+0x10</c> is the sim's duration.</summary>
    public override void SetDuration(float seconds) => _sim.SetDuration(seconds);

    /// <summary>
    /// Stock slot 6 (<c>101096c2</c>) is not an ending — it pulls the duration in to three seconds
    /// from now and makes the fade-out that long, so the beam leaves through its own end ramp.
    /// </summary>
    protected override void OnTerminateGracefully() => _sim.TerminateGracefully(_sim.CycleAge);

    /// <summary>Stock's body runs on the arming call too, at dt 0 — which is the only frame a 0.02 s take gets.</summary>
    protected override void OnArmed() => StepOnce(0f);

    protected override void OnProcess(float dt)
    {
        float step = EffectFrameRate.StockProcessSeconds;
        int steps = EffectFrameRate.TakeFixedSteps(ref _carry, dt, step, MaxStepsPerFrame);
        for (int i = 0; i < steps && !_sim.Dead; i++)
            StepOnce(step);

        if (_sim.Dead)
            ReadyFlag = true;
    }

    void StepOnce(float dt)
    {
        Matrix4x4 world = Matrix4x4.identity;
        bool valid = Locator != null && Locator.TryResolve(out world);
        _world = valid ? world : Matrix4x4.identity;
        Vector3 origin = valid ? (Vector3)world.GetColumn(3) : Vector3.zero;
        _sim.Step(dt, valid, origin.x, origin.y, origin.z);
        _placed = valid;
    }

    public override void CollectStrips(List<EffectBillboardBatch.Strip> dest, Camera camera)
    {
        if (!IsAlive || !_placed || dest == null || _texture == null || _blades.BladeCount <= 0)
            return;

        float blend = Mathf.Clamp01(_carry / EffectFrameRate.StockProcessSeconds);
        BeamSim.State state = _sim.Blanked
            ? _sim.Current
            : BeamSim.Blend(_sim.Previous, _sim.Current, blend);

        if (state.Colour1 == 0 && state.Colour2 == 0)
            return;

        _blades.Build(state.Length, state.Foot, state.Tip, state.Colour1, state.Colour2);

        // 1010a121: the locator's own turn with its scale stripped, then the optional quarter turn
        // about x, then the spin about y — applied in that order (1010a1ab multiplies the spin on the
        // left of what came before).
        // 10109afc normalises the three basis rows before making the quaternion; Matrix4x4.rotation
        // reads the same rotation out of a scaled matrix.
        Quaternion turn = _world.rotation;
        if (_sim.QuarterTurn)
            turn *= Quaternion.AngleAxis(90f, Vector3.right);
        turn = Quaternion.AngleAxis(state.Angle * Mathf.Rad2Deg, Vector3.up) * turn;

        var anchor = new Vector3(state.AnchorX, state.AnchorY, state.AnchorZ);
        bool fade = (_sim.Flags & BeamBlades.FlagCameraFade) != 0;
        Vector3 view = camera != null
            ? (anchor - camera.transform.position).normalized
            : Vector3.forward;

        Fill(_strip, _blades.X, _blades.Y, _blades.Z, _blades.U, _blades.V, _blades.Colors,
            _blades.VertexCount, anchor, turn, fade, view);
        dest.Add(_strip);

        if (_capStrip != null)
        {
            Fill(_capStrip, _blades.CapX, _blades.CapY, _blades.CapZ, _blades.CapU, _blades.CapV,
                _blades.CapColors, 4, anchor, turn, fade, view);
            dest.Add(_capStrip);
        }
    }

    void Fill(
        EffectBillboardBatch.Strip strip, float[] x, float[] y, float[] z, float[] u, float[] v,
        uint[] colors, int count, Vector3 anchor, Quaternion turn, bool fade, Vector3 view)
    {
        for (int i = 0; i < count; i += 4)
        {
            float factor = 1f;
            if (fade)
            {
                // 1000839d: |n · view|, the blade's own normal from its first three vertices, so a
                // blade seen edge-on fades to nothing.
                Vector3 p0 = new Vector3(x[i], y[i], z[i]);
                Vector3 e1 = (new Vector3(x[i + 1], y[i + 1], z[i + 1]) - p0).normalized;
                Vector3 e2 = (new Vector3(x[i + 2], y[i + 2], z[i + 2]) - p0).normalized;
                Vector3 normal = Vector3.Cross(e1, e2).normalized;
                // Port-only: a blade with a zero radius at both ends has no plane to face. Stock's
                // 10007cc2 guard is on the view vector, not this one, and 100051f8 would hand it
                // rubbish; leaving the alpha alone is the harmless reading.
                if (normal.sqrMagnitude > 0f)
                    factor = Mathf.Abs(Vector3.Dot(turn * normal, view));
            }

            for (int k = i; k < i + 4; k++)
            {
                strip.Positions[k] = anchor + turn * new Vector3(x[k], y[k], z[k]);
                // D3D tv runs down the image; Unity's v runs up.
                strip.Uvs[k] = new Vector2(u[k], 1f - v[k]);
                strip.Colors[k] = EffectBillboardBatch.ToColor32(
                    fade ? BeamBlades.ScaleAlpha(colors[k], factor) : colors[k]);
            }
        }
        strip.Count = count;
    }
}
