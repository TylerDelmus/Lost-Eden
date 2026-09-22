using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// typeCode 1019 (0x3fb), stock <c>_GfxControlTracer1_t</c>: the flying projectile of a nano
/// (28612 flies tracer 45694). The mechanics are <see cref="Tracer1Sim"/>; this binds them to the
/// two points stock reads from the hit location when the control is built and draws the result
/// through the same GfxVisualFlareType0 as Flare (additive, SRCALPHA / ONE).
///
/// The locator is fixed at build (a kind 2 matrix locator never moves or fails), so the control
/// only uses its own start and end. Stock slot 8 SetDuration is a no-op (<c>10079931</c>) and slot 6
/// TerminateGracefully sets duration = age (<c>100fe3a9</c>).
/// </summary>
public sealed class GfxControlTracer1 : GfxControl
{
    readonly Tracer1Sim _sim;
    readonly Texture2D _atlas;
    readonly EffectAtlasFrames _frames;
    readonly int _cols;
    readonly int _rows;
    readonly float[] _corners = new float[12];
    Matrix4x4 _visual = Matrix4x4.identity;

    public Tracer1Sim Sim => _sim;

    /// <summary>The visual's world matrix (the frame the sprites are in). For debug tooling.</summary>
    public Matrix4x4 VisualMatrix => _visual;

    public GfxControlTracer1(
        GfxTweakRecord record,
        EffectLocator locator,
        Vector3 start,
        Vector3 end,
        Texture2D atlas,
        EffectAtlasFrames frames,
        int cols,
        int rows,
        int firstFrame)
        : base(record, locator)
    {
        _atlas = atlas;
        _frames = frames;
        _cols = Mathf.Max(1, cols);
        _rows = Mathf.Max(1, rows);
        _sim = new Tracer1Sim(record?.Fields, start.x, start.y, start.z, end.x, end.y, end.z, firstFrame);

        // +0x10 = field 8; 45694 has -1, which never expires.
        base.SetDuration(record != null ? record.Field(8, InfiniteDuration) : InfiniteDuration);

        if (_sim.Degenerate)
        {
            ReadyFlag = true;
            return;
        }

        // Stock's first Process only arms the base timer (age 0, dt 0) and still runs the tracer's
        // body; the port's base skips OnProcess on that call, so run it here.
        _sim.Step(0f, 0f);
        PlaceVisual();
    }

    /// <summary>Stock slot 8 on this class is a no-op.</summary>
    public override void SetDuration(float seconds) { }

    /// <summary>Stock slot 6: duration = age, so the next Process readies it.</summary>
    protected override void OnTerminateGracefully() => base.SetDuration(Age);

    protected override void OnProcess(float dt)
    {
        if (_sim.Step(Age, dt))
            ReadyFlag = true;
        PlaceVisual();
    }

    /// <summary>
    /// The visual's rotation is the locator's matrix in local mode and identity otherwise
    /// (<c>101062d5</c>); its position is set by the flight. Stock rows are Unity columns.
    /// </summary>
    void PlaceVisual()
    {
        float[] b = _sim.Basis;
        var m = Matrix4x4.identity;
        if (_sim.Local)
        {
            m.SetColumn(0, new Vector4(b[0], b[1], b[2], 0f));
            m.SetColumn(1, new Vector4(b[3], b[4], b[5], 0f));
            m.SetColumn(2, new Vector4(b[6], b[7], b[8], 0f));
        }
        m.SetColumn(3, new Vector4(_sim.VisualX, _sim.VisualY, _sim.VisualZ, 1f));
        _visual = m;
    }

    public override void CollectBillboards(List<EffectBillboardBatch.Quad> dest, Camera camera)
    {
        if (!IsAlive || _sim.Visual.LiveCount == 0)
            return;
        GfxControlFlareType0.CollectSprites(
            dest, camera, _visual, _sim.Visual.Sprites, _atlas, _frames, _cols, _rows, _corners);
    }
}
