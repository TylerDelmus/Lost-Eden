using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// typeCode 3009 (0xbc1), stock <c>_GfxControlCrazyCone_t</c> with its <c>GfxVisualCone</c>s. The
/// mechanics are <see cref="CrazyConeSim"/> and the geometry <see cref="ConeFrustum"/>; this binds
/// them to a locator and draws them.
///
/// The build makes <c>field 10</c> cones, all standing on the one anchor, each turned about the
/// world y by its own share of a full turn plus the stage's spin, and each a little wider than the
/// last (fields 12 and 13 step the two radii per cone). What animates is the shared shape: the
/// stages integrate the radii, the height and the texture spans, and scroll the texture.
///
/// Stock's Process integrates per call and steps a stage machine, so the body is replayed on the
/// fixed stock clock and the drawn state interpolated between the last two steps (Docs §3.7 group B).
/// The base's timer is left off — CrazyCone ends itself when it runs out of stages.
/// </summary>
public sealed class GfxControlCrazyCone : GfxControl
{
    /// <summary>A stall must not run a whole stage list in one frame.</summary>
    const int MaxStepsPerFrame = 4;

    readonly CrazyConeSim _sim;
    readonly ConeFrustum[] _cones;
    readonly EffectBillboardBatch.Strip[] _strips;
    readonly Texture2D _texture;

    float _carry;
    bool _placed;

    public CrazyConeSim Sim => _sim;
    public ConeFrustum[] Cones => _cones;

    public GfxControlCrazyCone(GfxTweakRecord record, EffectLocator locator, Texture2D texture)
        : base(record, locator)
    {
        _texture = texture;
        _sim = new CrazyConeSim(record?.Fields);

        _cones = new ConeFrustum[_sim.ConeCount];
        _strips = new EffectBillboardBatch.Strip[_sim.ConeCount];
        for (int i = 0; i < _cones.Length; i++)
        {
            _cones[i] = new ConeFrustum(_sim.Segments, _sim.SwapUv);
            int n = _cones[i].VertexCount;
            _strips[i] = new EffectBillboardBatch.Strip
            {
                Positions = new Vector3[n],
                Uvs = new Vector2[n],
                Colors = new Color32[n],
                Color = Color.white,
                Texture = texture,
                Additive = _sim.Additive,
            };
        }

        // The stage list decides when this ends, not the base timer.
        base.SetDuration(InfiniteDuration);
    }

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
        Vector3 origin = valid ? (Vector3)world.GetColumn(3) : Vector3.zero;
        _sim.Step(dt, valid, origin.x, origin.y, origin.z);

        // 100d715e: flag 0x2000 keeps it alive after the locator is gone, so it stays drawable.
        _placed = valid || _sim.OutlivesLocator;
    }

    public override void CollectStrips(List<EffectBillboardBatch.Strip> dest, Camera camera)
    {
        if (!IsAlive || !_placed || dest == null || _texture == null || _cones.Length == 0)
            return;

        float blend = Mathf.Clamp01(_carry / EffectFrameRate.StockProcessSeconds);
        CrazyConeSim.State s = CrazyConeSim.Blend(_sim.Previous, _sim.Current, blend);

        var anchor = new Vector3(s.AnchorX, s.AnchorY, s.AnchorZ);

        for (int i = 0; i < _cones.Length; i++)
        {
            ConeFrustum cone = _cones[i];
            int n = cone.VertexCount;
            if (n < 4)
                continue;

            cone.Build(
                _sim.ConeBottomRadius(i, s.BottomRadius),
                _sim.ConeTopRadius(i, s.TopRadius),
                s.Height,
                s.ColourB, s.ColourA,
                s.USpan, s.VSpan, s.UScroll, s.VScroll);

            // 100d71c6: its own share of a full turn, plus whatever the stage has spun.
            float angle = _sim.ConeAngle(i, s.Spin);
            Quaternion turn = Quaternion.AngleAxis(angle * Mathf.Rad2Deg, Vector3.up);

            EffectBillboardBatch.Strip strip = _strips[i];
            for (int v = 0; v < n; v++)
            {
                strip.Positions[v] = anchor + turn * new Vector3(cone.X[v], cone.Y[v], cone.Z[v]);
                // D3D tv runs down the image; Unity's v runs up.
                strip.Uvs[v] = new Vector2(cone.U[v], 1f - cone.V[v]);
                strip.Colors[v] = EffectBillboardBatch.ToColor32(cone.Colors[v]);
            }
            strip.Count = n;
            dest.Add(strip);
        }
    }
}
