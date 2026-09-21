using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracer / beam-ribbon types (1013, 1024, 1025, 1026), built in stock only by
/// <c>_EffectHandler_t::CreateGfxControlTracer(id, from, to)</c>.
///
/// Stock's init (typeCode 0x401 at 0x10100554) makes it clear these are stretched strips, not
/// billboards: it computes <c>dir = to - from</c>, normalises it, stores the segment length, marks
/// the control broken when the segment is degenerate, then derives two perpendicular axes by cross
/// product and feeds start + dir + perpendiculars into the geometry builder. The textures match —
/// material 15 (<c>s_bullet.png</c>) is a 16x64 streak tapering to a point, so it only reads
/// correctly when its long axis runs along the direction of travel.
///
/// Drawing these as square camera-facing quads squashed the streak into a blob and left the taper
/// pointing an arbitrary way, which looked like a floating cone.
///
/// The template field layout is its own thing, documented in <see cref="TracerMath"/> — width and
/// length come from fields 12 and 11, and the colour from 13..16.
/// </summary>
public sealed class GfxControlTracer : GfxControl
{
    readonly Texture2D _atlas;
    readonly EffectAtlasFrames _frames;
    readonly int _cols;
    readonly int _rows;
    readonly int _firstFrame;
    readonly int _lastFrame;
    readonly bool _additive;

    /// <summary>Single constant colour; the family has no start/end pair. See <see cref="TracerMath"/>.</summary>
    readonly Color _color;

    readonly float _width;
    readonly float _length;

    public GfxControlTracer(
        GfxTweakRecord record,
        EffectLocator locator,
        Texture2D atlas,
        EffectAtlasFrames frames,
        int cols,
        int rows,
        int firstFrame,
        int lastFrame,
        Color tint,
        bool additive)
        : base(record, locator)
    {
        _atlas = atlas;
        _frames = frames;
        _cols = Mathf.Max(1, cols);
        _rows = Mathf.Max(1, rows);
        _firstFrame = firstFrame;
        _lastFrame = lastFrame >= firstFrame ? lastFrame : firstFrame;
        _additive = additive;

        _width = TracerMath.Width(record != null ? record.Field(TracerMath.FieldWidth, 0f) : 0f);
        _length = TracerMath.Length(record != null ? record.Field(TracerMath.FieldLength, 0f) : 0f);

        Color color = ReadColor(record);
        if (EffectColors.IsOverrideTint(tint))
            color = EffectColors.ApplyTint(color, tint);
        _color = color;

        SetDurationFromTemplate(TracerMath.FieldDuration, 1.5f);
    }

    /// <summary>
    /// Fields 13..16 as alpha, red, green, blue — the order stock packs them into a D3DCOLOR.
    /// Reading this block at the sprite family's field 16 instead put template field 17, an integer,
    /// through a float getter, which yielded a near-black colour and made every tracer invisible.
    /// </summary>
    static Color ReadColor(GfxTweakRecord record)
    {
        if (record == null || record.FieldCount < TracerMath.MinFieldCount)
            return Color.white;

        return new Color(
            Mathf.Clamp01(record.Field(TracerMath.FieldRed, 1f)),
            Mathf.Clamp01(record.Field(TracerMath.FieldGreen, 1f)),
            Mathf.Clamp01(record.Field(TracerMath.FieldBlue, 1f)),
            Mathf.Clamp01(record.Field(TracerMath.FieldAlpha, 1f)));
    }

    public override void CollectBillboards(List<EffectBillboardBatch.Quad> dest, Camera camera)
    {
        if (!IsAlive || dest == null || _atlas == null)
            return;

        // Records such as 45705 are deliberately blank: zero width, length and alpha.
        if (!TracerMath.IsVisible(_width, _length, _color.a))
            return;

        float t01 = Duration > 0f ? Mathf.Clamp01(Age / Duration) : 0f;

        Matrix4x4 world = WorldMatrix;

        // The hit-location locator points local +Z along the travel direction.
        Vector3 axis = world.MultiplyVector(Vector3.forward);
        if (axis.sqrMagnitude < 1e-8f)
            return;
        axis.Normalize();

        // Lead with the bright end at the current travel position so the taper trails behind.
        Vector3 head = world.GetColumn(3);
        Vector3 centre = head - axis * (_length * 0.5f);

        int frameCount = _lastFrame - _firstFrame + 1;
        int frame = frameCount <= 1
            ? _firstFrame
            : _firstFrame + Mathf.Clamp(Mathf.FloorToInt(t01 * frameCount), 0, frameCount - 1);

        Texture2D tex = _frames != null
            ? _frames.GetFrame(_atlas, _cols, _rows, frame)
            : _atlas;
        if (tex == null)
            return;

        dest.Add(new EffectBillboardBatch.Quad
        {
            Matrix = Matrix4x4.TRS(centre, Quaternion.identity, Vector3.one),
            Scale = _width,
            Stretch = axis * _length,
            Color = _color,
            Texture = tex,
            Additive = _additive,
        });
    }
}
