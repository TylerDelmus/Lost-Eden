using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sprite2Type0 / Nano0 / Sparks / Flare family — sequential atlas billboard over duration.
/// Additive variants also drive a pooled HDRP point light.
/// </summary>
public sealed class GfxControlBillboard : GfxControl
{
    readonly Texture2D _atlas;
    readonly EffectAtlasFrames _frames;
    readonly bool _additive;
    readonly float _startScale;
    readonly float _endScale;
    readonly Color _startColor;
    readonly Color _endColor;
    readonly int _firstFrame;
    readonly int _lastFrame;
    readonly int _cols;
    readonly int _rows;
    readonly EffectLightPool _lights;
    EffectLightLease _lightLease;
    bool _holdAppearanceWhileFading;
    float _fadeSeconds = 0.55f;

    public GfxControlBillboard(
        GfxTweakRecord record,
        EffectLocator locator,
        Texture2D atlas,
        EffectAtlasFrames frames,
        int cols,
        int rows,
        int firstFrame,
        int lastFrame,
        Color tint,
        bool additive,
        EffectLightPool lights = null,
        int durationIndex = 8)
        : base(record, locator)
    {
        _atlas = atlas;
        _frames = frames;
        _cols = Mathf.Max(1, cols);
        _rows = Mathf.Max(1, rows);
        _firstFrame = firstFrame;
        _lastFrame = lastFrame >= firstFrame ? lastFrame : firstFrame;
        _additive = additive;
        _lights = lights;
        ReadScales(record, out _startScale, out _endScale);

        Color start = ReadStartColor(record, tint);
        Color end = ReadEndColor(record, start);
        _startColor = start;
        _endColor = end;

        SetDurationFromTemplate(durationIndex, 1.5f);

        if (_additive && _lights != null)
            _lights.TryAcquire(out _lightLease);
    }

    /// <summary>
    /// Flare/Nano/Cord templates store two size channels at idx 12–15 (start0,end0,start1,end1).
    /// Billboard approx uses the larger channel so elongated flares aren't stuck at the thin axis (0.05).
    /// Older layouts may use idx 2/3 when 12–15 are empty.
    /// </summary>
    static void ReadScales(GfxTweakRecord record, out float startScale, out float endScale)
    {
        const float fallbackStart = 0.75f;
        const float fallbackEnd = 1.4f;
        if (record != null && record.FieldCount > 15)
        {
            float a0 = record.Field(12, 0f);
            float a1 = record.Field(13, 0f);
            float b0 = record.Field(14, 0f);
            float b1 = record.Field(15, 0f);
            float start = Mathf.Max(a0, b0);
            float end = Mathf.Max(a1, b1);
            if (start > 0f || end > 0f)
            {
                startScale = ClampScale(start > 0f ? start : end, fallbackStart);
                endScale = ClampScale(end > 0f ? end : start, fallbackEnd);
                return;
            }
        }

        startScale = ClampScale(record != null ? record.Field(2, fallbackStart) : fallbackStart, fallbackStart);
        endScale = ClampScale(record != null ? record.Field(3, fallbackEnd) : fallbackEnd, fallbackEnd);
    }

    static Color ReadStartColor(GfxTweakRecord record, Color tint)
    {
        // Spell1 / nano paths pass a real tint that is the draw color.
        // Color.white from CreateEffect2 must not wipe template ARGB.
        if (EffectColors.IsOverrideTint(tint))
            return tint;

        if (record != null && record.FieldCount > 19)
        {
            Color c = EffectColors.ReadArgbBlock(record, 16, Color.clear);
            if (c.r + c.g + c.b > 0.01f)
                return c;
        }

        return Color.white;
    }

    static Color ReadEndColor(GfxTweakRecord record, Color start)
    {
        if (record != null && record.FieldCount > 23)
        {
            Color c = EffectColors.ReadArgbBlock(record, 20, Color.clear);
            if (c.r + c.g + c.b > 0.01f)
                return c;
        }
        return start;
    }

    protected override void OnTerminateGracefully()
    {
        // Stock Nano0 shortens remaining life to a brief fade window.
        // Infinite-duration cast children (hand glow) hold their look and fade alpha out —
        // assigning Duration=Age+ε alone would snap t01≈1 and pop to end appearance.
        const float FadeSeconds = 0.55f;
        if (Duration < 0f)
        {
            _holdAppearanceWhileFading = true;
            _fadeSeconds = FadeSeconds;
            SetDuration(Age + FadeSeconds);
            return;
        }

        if (Age >= Duration)
        {
            ReadyFlag = true;
            ReleaseLight();
            return;
        }

        float remaining = Duration - Age;
        if (remaining > FadeSeconds)
            SetDuration(Age + FadeSeconds);
    }

    protected override void OnReleased(bool immediate)
    {
        ReleaseLight();
    }

    void ReleaseLight()
    {
        _lightLease?.Release();
        _lightLease = null;
    }

    public override void CollectBillboards(List<EffectBillboardBatch.Quad> dest, Camera camera)
    {
        if (!IsAlive || dest == null)
        {
            ReleaseLight();
            return;
        }

        float fadeMul = 1f;
        float scale;
        Color color;
        int frame;

        if (_holdAppearanceWhileFading)
        {
            // Keep the sustained cast look; only alpha/light ramp down.
            scale = Mathf.Max(_startScale, _endScale);
            color = _startColor;
            fadeMul = Duration > Age
                ? Mathf.Clamp01((Duration - Age) / Mathf.Max(0.01f, _fadeSeconds))
                : 0f;
            frame = _firstFrame;
        }
        else
        {
            float t01 = Duration > 0f ? Mathf.Clamp01(Age / Duration) : 0f;
            // Infinite-duration templates (duration −1) never advance t01 — use the larger size.
            scale = Duration > 0f
                ? Mathf.Lerp(_startScale, _endScale, t01)
                : Mathf.Max(_startScale, _endScale);
            color = Duration > 0f
                ? Color.Lerp(_startColor, _endColor, t01)
                : _startColor;
            int frameCount = _lastFrame - _firstFrame + 1;
            frame = frameCount <= 1
                ? _firstFrame
                : _firstFrame + Mathf.Clamp(Mathf.FloorToInt(t01 * frameCount), 0, frameCount - 1);
        }

        color.a *= fadeMul;

        if (_lightLease != null)
        {
            Vector3 pos = WorldMatrix.GetColumn(3);
            _lightLease.Update(
                pos,
                color,
                EffectLightPool.NanoIntensity(color),
                EffectLightPool.NanoRange(scale));
        }

        if (color.a < 0.01f || _atlas == null)
            return;

        Texture2D frameTex = _frames != null
            ? _frames.GetFrame(_atlas, _cols, _rows, frame)
            : _atlas;
        if (frameTex == null)
            return;

        dest.Add(new EffectBillboardBatch.Quad
        {
            Matrix = WorldMatrix,
            Scale = scale,
            Color = color,
            Texture = frameTex,
            Additive = _additive,
        });
    }
}
