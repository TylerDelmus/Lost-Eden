using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// typeCode 3033 (0xbd9), stock <c>GfxControlSpiral2_t</c> with its <c>GfxVisualSpiral2</c> ribbons.
/// The mechanics are <see cref="Spiral2Sim"/> and <see cref="Spiral2Ribbon"/>; this binds them to a
/// locator and draws them.
///
/// Every ribbon sits at the locator's world-mode position (zero in local mode, as stock's
/// <c>1010640a</c>) and shares one turn about the world y (<c>100520d9</c>), so what separates them
/// is only the start angle their helix is wound from. Each is one triangle strip carrying its own
/// vertex colours, textured by the field 9 material, which wraps along the ribbon.
///
/// The turn is the one part of stock's Process that integrates per call
/// (<c>angle += spin · dt</c>, then <c>spin += acceleration · dt</c>), so its error depends on the
/// frame rate: at 150 FPS a record with a strong acceleration ends up tens of degrees ahead of where
/// stock left it at ~30. It is therefore replayed on the fixed stock clock and the drawn angle is
/// interpolated between the last two steps, which keeps stock's timing and still turns smoothly
/// (Docs §3.7 group B). The phase, the colour and the radius are read from the age every frame.
/// </summary>
public sealed class GfxControlSpiral2 : GfxControl
{
    /// <summary>A stall must not spin the helix through a whole turn in one frame.</summary>
    const int MaxStepsPerFrame = 4;

    readonly Spiral2Sim _sim;
    readonly Texture2D _texture;
    readonly EffectBillboardBatch.Strip[] _strips;

    float _carry;
    float _previousAngle;
    Vector3 _origin;
    bool _placed;

    public Spiral2Sim Sim => _sim;

    public GfxControlSpiral2(GfxTweakRecord record, EffectLocator locator, Texture2D texture)
        : base(record, locator)
    {
        _texture = texture;
        _sim = new Spiral2Sim(record?.Fields, () => Random.value);

        _strips = new EffectBillboardBatch.Strip[_sim.Ribbons.Length];
        for (int i = 0; i < _strips.Length; i++)
        {
            int n = _sim.Ribbons[i].VertexCount;
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

        base.SetDuration(_sim.Duration > 0f ? _sim.Duration : InfiniteDuration);
    }

    /// <summary>Stock slot 8 is the base's: <c>+0x10</c> is the duration both the timer and the curves read.</summary>
    public override void SetDuration(float seconds)
    {
        _sim.SetDuration(seconds);
        base.SetDuration(seconds);
    }

    /// <summary>
    /// Stock slot 6 (<c>10111d75</c>) is not an ending — it pulls the duration in to a second from now
    /// and rewrites both curves so the ribbons fade out over it. Past that point it does nothing and
    /// the timer runs out on its own.
    /// </summary>
    protected override void OnTerminateGracefully()
    {
        _sim.TerminateGracefully(Age);
        base.SetDuration(_sim.Duration);
    }

    protected override void OnArmed() => Place();

    protected override void OnProcess(float dt)
    {
        if (!Place())
            return;

        float step = EffectFrameRate.StockProcessSeconds;
        int steps = EffectFrameRate.TakeFixedSteps(ref _carry, dt, step, MaxStepsPerFrame);
        for (int i = 0; i < steps; i++)
        {
            _previousAngle = _sim.Angle;
            _sim.Step(Age, step);
        }
    }

    /// <summary>
    /// <c>10111c62</c>: a locator that no longer resolves stops the control outright (<c>+0x14</c>),
    /// before anything is placed or stepped.
    /// </summary>
    bool Place()
    {
        if (Locator == null || !Locator.TryResolve(out Matrix4x4 world))
        {
            ReadyFlag = true;
            return false;
        }

        bool local = Record != null && (Record.FieldInt(0, 0) & 2) != 0;
        _origin = local ? Vector3.zero : (Vector3)world.GetColumn(3);
        _placed = true;
        return true;
    }

    public override void CollectStrips(List<EffectBillboardBatch.Strip> dest, Camera camera)
    {
        if (!IsAlive || !_placed || dest == null || _texture == null)
            return;

        float blend = Mathf.Clamp01(_carry / EffectFrameRate.StockProcessSeconds);
        float angle = Mathf.LerpUnclamped(_previousAngle, _sim.Angle, blend);

        // 100520d9, the same quaternion builder Spiral turns with.
        float half = SpiralSim.HalfAngle(angle);
        var turn = new Quaternion(0f, Mathf.Sin(half), 0f, Mathf.Cos(half));

        float t = _sim.Phase(Age);
        uint colour = _sim.ColourAt(Age);
        float radius = _sim.RadiusAt(Age);

        for (int r = 0; r < _sim.Ribbons.Length; r++)
        {
            Spiral2Ribbon ribbon = _sim.Ribbons[r];
            int n = ribbon.VertexCount;
            if (n < 4)
                continue;

            ribbon.Build(t, radius, colour);

            EffectBillboardBatch.Strip strip = _strips[r];
            for (int i = 0; i < n; i++)
            {
                strip.Positions[i] = _origin + turn * new Vector3(ribbon.X[i], ribbon.Y[i], ribbon.Z[i]);
                // D3D tv runs down the image; Unity's v runs up.
                strip.Uvs[i] = new Vector2(ribbon.U[i], 1f - ribbon.V[i]);
                strip.Colors[i] = EffectBillboardBatch.ToColor32(ribbon.Colors[i]);
            }
            strip.Count = n;
            dest.Add(strip);
        }
    }
}
