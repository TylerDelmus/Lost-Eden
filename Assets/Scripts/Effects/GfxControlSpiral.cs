using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// typeCode 2001 (0x7d1), stock <c>_GfxControlSpiral_t</c> with its two <c>GfxVisualSpiral</c> ribbons. The
/// mechanics are <see cref="SpiralSim"/> and <see cref="SpiralRibbon"/>; this binds them to a locator and
/// draws them.
///
/// Both ribbons sit at the locator's world-mode position (zero in local mode, as stock's
/// <c>1010640a</c>), turned about the world y. They are white, additive, textured by the field 9 material
/// (which wraps along the ribbon), and fade to colour 0 at their first and last vertex pairs. Each
/// ribbon is one strip carrying those vertex colours.
/// </summary>
public sealed class GfxControlSpiral : GfxControl
{
    readonly SpiralSim _sim;
    readonly SpiralRibbon[] _ribbons = new SpiralRibbon[SpiralSim.RibbonCount];
    readonly Texture2D _texture;
    readonly EffectBillboardBatch.Strip[] _strips = new EffectBillboardBatch.Strip[SpiralSim.RibbonCount];
    Vector3 _origin;
    bool _placed;

    public SpiralSim Sim => _sim;
    public IReadOnlyList<SpiralRibbon> Ribbons => _ribbons;

    public GfxControlSpiral(GfxTweakRecord record, EffectLocator locator, Texture2D texture)
        : base(record, locator)
    {
        _texture = texture;
        _sim = new SpiralSim(record != null ? record.Field(8, -1f) : -1f);
        for (int i = 0; i < _ribbons.Length; i++)
            _ribbons[i] = new SpiralRibbon(SpiralSim.RibbonOffset(i));
        for (int i = 0; i < _strips.Length; i++)
        {
            _strips[i] = new EffectBillboardBatch.Strip
            {
                Positions = new Vector3[SpiralRibbon.VertexCount],
                Uvs = new Vector2[SpiralRibbon.VertexCount],
                Colors = new Color32[SpiralRibbon.VertexCount],
                Color = Color.white,
                Texture = texture,
                Additive = true,
            };
        }
        // Expiry is the stock rule on the sim's duration (see Body), not the base timer.
        base.SetDuration(InfiniteDuration);
    }

    /// <summary>Stock slot 8 (<c>100f4dea</c>): +0x10 = seconds.</summary>
    public override void SetDuration(float seconds)
    {
        if (_sim != null)
            _sim.Duration = seconds;
    }

    // Slots 11-13 (100f4df7 / 100f4e1b / 100f4e3f) only set the colour ramp, which nothing reads.

    protected override void OnTerminateGracefully() => _sim.TerminateGracefully(Age);

    protected override void OnArmed() => Body();

    protected override void OnProcess(float dt) => Body();

    void Body()
    {
        // _GfxControl_t::Process (100d2a86): ready once 0 <= duration < age.
        if (0f <= _sim.Duration && _sim.Duration < Age)
        {
            ReadyFlag = true;
            return;
        }

        if (Locator == null || !Locator.TryResolve(out Matrix4x4 world))
        {
            // 100f4cd9: a lost locator ends it on the next call.
            _sim.TerminateGracefully(Age);
        }
        else
        {
            bool local = Record != null && (Record.FieldInt(0, 0) & 2) != 0;
            _origin = local ? Vector3.zero : (Vector3)world.GetColumn(3);
            _placed = true;
        }

        _sim.Step(Age);
    }

    public override void CollectStrips(List<EffectBillboardBatch.Strip> dest, Camera camera)
    {
        if (!IsAlive || !_placed || dest == null || _texture == null)
            return;

        float half = SpiralSim.HalfAngle(_sim.Spin);
        var turn = new Quaternion(0f, Mathf.Sin(half), 0f, Mathf.Cos(half));
        for (int r = 0; r < _ribbons.Length; r++)
        {
            SpiralRibbon ribbon = _ribbons[r];
            ribbon.Build(Age, _sim.Start, _sim.End);
            int count = ribbon.End - ribbon.First;
            if (count < 4)
                continue;

            EffectBillboardBatch.Strip strip = _strips[r];
            for (int i = 0; i < count; i++)
            {
                int v = ribbon.First + i;
                strip.Positions[i] = _origin + turn * new Vector3(ribbon.X[v], ribbon.Y[v], ribbon.Z[v]);
                // D3D tv runs down the image; Unity's v runs up.
                strip.Uvs[i] = new Vector2(ribbon.U[v], 1f - ribbon.V[v]);
                // The end pairs take colour 0; every other vertex the visual's white (+0x18c).
                strip.Colors[i] = ribbon.Alpha[v] > 0f ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
            }
            strip.Count = count;
            dest.Add(strip);
        }
    }
}
