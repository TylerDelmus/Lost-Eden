using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// typeCode 3020 — single trail/particle visual. v1 uses one animated billboard (stock: one GfxVisualTParticle).
/// </summary>
public sealed class GfxControlTParticle : GfxControl
{
    readonly Texture2D _atlas;
    readonly EffectAtlasFrames _frames;
    readonly int _cols;
    readonly int _rows;
    readonly int _firstFrame;
    readonly int _lastFrame;
    readonly float _startScale;
    readonly float _endScale;
    readonly Color _startColor;
    readonly Color _endColor;
    readonly Vector3 _drift;

    public GfxControlTParticle(
        GfxTweakRecord record,
        EffectLocator locator,
        Texture2D atlas,
        EffectAtlasFrames frames,
        int cols,
        int rows,
        int firstFrame,
        int lastFrame,
        Color tint)
        : base(record, locator)
    {
        _atlas = atlas;
        _frames = frames;
        _cols = Mathf.Max(1, cols);
        _rows = Mathf.Max(1, rows);
        _firstFrame = firstFrame;
        _lastFrame = lastFrame >= firstFrame ? lastFrame : firstFrame;
        _startScale = ClampScale(record != null ? record.Field(12, 0.5f) : 0.5f, 0.5f);
        _endScale = ClampScale(record != null ? record.Field(13, 1.2f) : 1.2f, 1.2f);

        Color start = tint.a > 0.01f && (tint.r + tint.g + tint.b) > 0.01f
            ? tint
            : ReadRgb01(record, 14, Color.white);
        _startColor = start;
        _endColor = ReadRgb01(record, 17, start);
        _drift = new Vector3(
            record != null ? record.Field(20, 0f) : 0f,
            record != null ? record.Field(21, 0.4f) : 0.4f,
            record != null ? record.Field(22, 0f) : 0f);

        SetDurationFromTemplate(8, 2f);
    }

    Vector3 _offset;

    protected override void OnProcess(float dt)
    {
        _offset += _drift * dt;
    }

    protected override void OnTerminateGracefully()
    {
        if (Duration < 0f || Age >= Duration)
        {
            ReadyFlag = true;
            return;
        }

        if (Duration - Age > 0.35f)
            SetDuration(Age + 0.35f);
    }

    public override void CollectBillboards(List<EffectBillboardBatch.Quad> dest, Camera camera)
    {
        if (!IsAlive || dest == null)
            return;

        float t01 = Duration > 0f ? Mathf.Clamp01(Age / Duration) : 0f;
        Matrix4x4 m = WorldMatrix;
        Vector3 pos = m.MultiplyPoint3x4(_offset);
        int frameCount = _lastFrame - _firstFrame + 1;
        int frame = frameCount <= 1
            ? _firstFrame
            : _firstFrame + Mathf.Clamp(Mathf.FloorToInt(t01 * frameCount), 0, frameCount - 1);

        if (_atlas == null)
            return;

        Texture2D frameTex = _frames != null
            ? _frames.GetFrame(_atlas, _cols, _rows, frame)
            : _atlas;
        if (frameTex == null)
            return;

        dest.Add(new EffectBillboardBatch.Quad
        {
            Matrix = Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one),
            Scale = Mathf.Lerp(_startScale, _endScale, t01),
            Color = Color.Lerp(_startColor, _endColor, t01),
            Texture = frameTex,
            Additive = true,
        });
    }
}
