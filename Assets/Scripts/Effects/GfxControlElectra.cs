using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// typeCode 2006 (0x7d6), stock <c>_GfxControlElectra_t</c>, mode 1: a shell of flat sparks around the
/// locator. The mechanics are <see cref="ElectraSim"/>; this feeds it the locator every frame and draws
/// the sparks as <c>GfxVisualElectra</c> does (DisplaySystem <c>100118d3</c>): for each visible sprite a
/// quad at centre ± axis A / 2 ± axis C / 2, atlas cell <c>frame % cols, frame / cols</c> from the top
/// left, the sprite's ARGB on every corner. Blending SRCALPHA/ONE (Gamecode builds the visual with its
/// additive flag set), no Z write, no culling.
/// </summary>
public sealed class GfxControlElectra : GfxControl
{
    readonly ElectraSim _sim;
    readonly EffectBillboardBatch.Strip _quads;
    readonly int _cols;
    readonly int _rows;

    public ElectraSim Sim => _sim;

    public GfxControlElectra(GfxTweakRecord record, EffectLocator locator, Texture2D texture, int cols, int rows)
        : base(record, locator)
    {
        _sim = new ElectraSim(record?.Fields, () => Random.Range(0, 0x8000));
        // Stock expiry, with its extra spark life, is the sim's.
        base.SetDuration(InfiniteDuration);

        EffectAtlasFrames.InferGrid(texture, ref cols, ref rows);
        _cols = Mathf.Max(1, cols);
        _rows = Mathf.Max(1, rows);
        _quads = new EffectBillboardBatch.Strip
        {
            Positions = new Vector3[ElectraSim.SlotCount * 4],
            Uvs = new Vector2[ElectraSim.SlotCount * 4],
            Texture = texture,
            Additive = true,
            Quads = true,
        };
    }

    /// <summary>Stock slot 8 (<c>100d9695</c>): +0x10 = seconds.</summary>
    public override void SetDuration(float seconds) => _sim.Duration = seconds;

    /// <summary>Stock slot 6 (<c>100d968d</c>): stop spawning; the sparks run out.</summary>
    protected override void OnTerminateGracefully() => _sim.Terminating = true;

    public override void SetStartColor(float a, float r, float g, float b) => _sim.SetStartColor(a, r, g, b);

    public override void SetStopColor(float a, float r, float g, float b) => _sim.SetStopColor(a, r, g, b);

    protected override void OnProcess(float dt)
    {
        if (_sim.Expire(Age))
        {
            ReadyFlag = true;
            return;
        }

        // Every Electra template is world mode (field 0 bit 1 clear): the sparks sit at the locator's
        // position (FUN_1010640a) plus their offsets, and the visual stays at the origin, unrotated.
        Vector3 origin = WorldMatrix.GetColumn(3);
        _sim.Step(Age, origin.x, origin.y, origin.z);
    }

    public override void CollectStrips(List<EffectBillboardBatch.Strip> dest, Camera camera)
    {
        if (!IsAlive || dest == null || _quads.Texture == null)
            return;

        float du = 1f / _cols, dv = 1f / _rows;
        int count = 0;
        uint argb = 0;
        ElectraSim.Sprite[] sprites = _sim.Sprites;
        for (int i = 0; i < sprites.Length; i++)
        {
            ref ElectraSim.Sprite s = ref sprites[i];
            if (!s.Visible)
                continue;

            var p = new Vector3(s.X, s.Y, s.Z);
            var a = new Vector3(s.Ax, s.Ay, s.Az) * 0.5f;
            var c = new Vector3(s.Cx, s.Cy, s.Cz) * 0.5f;
            int col = s.Frame % _cols, row = s.Frame / _cols;
            // D3D v runs down the atlas from the top row; Unity's runs up from the bottom.
            float u0 = col * du, u1 = u0 + du;
            float vTop = 1f - row * dv, vBottom = vTop - dv;

            int o = count * 4;
            _quads.Positions[o] = p - a - c;
            _quads.Uvs[o] = new Vector2(u0, vBottom);
            _quads.Positions[o + 1] = p + a - c;
            _quads.Uvs[o + 1] = new Vector2(u1, vBottom);
            _quads.Positions[o + 2] = p - a + c;
            _quads.Uvs[o + 2] = new Vector2(u0, vTop);
            _quads.Positions[o + 3] = p + a + c;
            _quads.Uvs[o + 3] = new Vector2(u1, vTop);
            argb = s.Argb;
            count++;
        }

        if (count == 0)
            return;

        // Every spark takes the ramp at the control's progress, so one colour serves them all.
        _quads.Count = count * 4;
        _quads.Color = new Color(
            ((argb >> 16) & 0xff) / 255f,
            ((argb >> 8) & 0xff) / 255f,
            (argb & 0xff) / 255f,
            ((argb >> 24) & 0xff) / 255f);
        dest.Add(_quads);
    }
}
