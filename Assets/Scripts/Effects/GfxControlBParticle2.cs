using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// typeCode 3028 — N independent sub-emitter billboards (stock GfxVisualBParticle2 × N).
/// </summary>
public sealed class GfxControlBParticle2 : GfxControl
{
    const int MaxSubs = 32;

    struct Sub
    {
        public Vector3 LocalOffset;
        public Vector3 Velocity;
        public float Scale;
        public float Spin;
        public Color Color;
        public int Frame;
        public float Phase;
    }

    readonly Texture2D _atlas;
    readonly EffectAtlasFrames _frames;
    readonly int _cols;
    readonly int _rows;
    readonly int _firstFrame;
    readonly int _lastFrame;
    readonly bool _additive;
    readonly bool _randomFrame;
    readonly bool _spin;
    readonly Sub[] _subs;
    readonly Color _tint;

    public GfxControlBParticle2(
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
        _tint = tint.a > 0.01f ? tint : Color.white;
        _additive = true;

        int flags = record != null ? record.FieldInt(0, 0) : 0;
        _randomFrame = (flags & 0x20000) != 0;
        _spin = (flags & 0x4000) == 0;

        int n = record != null ? record.FieldInt(11, 4) : 4;
        n = Mathf.Clamp(n, 1, MaxSubs);

        float radius = record != null ? Mathf.Max(0.05f, record.Field(13, 0.5f)) : 0.5f;
        float minAx = record != null ? record.Field(17, -0.2f) : -0.2f;
        float maxAx = record != null ? record.Field(18, 0.2f) : 0.2f;
        float minAy = record != null ? record.Field(19, 0.1f) : 0.1f;
        float maxAy = record != null ? record.Field(20, 0.6f) : 0.6f;
        float minAz = record != null ? record.Field(21, -0.2f) : -0.2f;
        float maxAz = record != null ? record.Field(22, 0.2f) : 0.2f;
        float minRot = record != null ? record.Field(23, 0f) : 0f;
        float maxRot = record != null ? record.Field(24, 0f) : 0f;
        float minSpeed = record != null ? record.Field(27, 0.2f) : 0.2f;
        float maxSpeed = record != null ? record.Field(28, 0.8f) : 0.8f;
        float minSize = record != null ? record.Field(25, 0.3f) : 0.3f;
        float maxSize = record != null ? record.Field(26, 0.9f) : 0.9f;

        _subs = new Sub[n];
        int frameCount = _lastFrame - _firstFrame + 1;
        for (int i = 0; i < n; i++)
        {
            Vector3 offset = Random.insideUnitSphere * radius;
            if ((flags & 0x400) != 0)
                offset.y = Mathf.Min(offset.y, 0f);

            Vector3 vel = new Vector3(
                Random.Range(minAx, maxAx),
                Random.Range(minAy, maxAy),
                Random.Range(minAz, maxAz));
            float speed = Random.Range(minSpeed, maxSpeed);
            if (vel.sqrMagnitude > 1e-6f)
                vel = vel.normalized * speed;
            else
                vel = Vector3.up * speed;

            int frame = _firstFrame;
            if (_randomFrame && frameCount > 1)
                frame = _firstFrame + Random.Range(0, frameCount);

            float spin = 0f;
            if (_spin)
            {
                // Stock: degrees→radians via × π/4 ÷ 45.
                float deg = Random.Range(minRot, maxRot);
                spin = deg * (Mathf.PI / 4f) / 45f;
            }

            _subs[i] = new Sub
            {
                LocalOffset = offset,
                Velocity = vel,
                Scale = ClampScale(Random.Range(minSize, maxSize), 0.5f),
                Spin = spin,
                Color = _tint,
                Frame = frame,
                Phase = Random.value,
            };
        }

        SetDurationFromTemplate(8, 1.5f);
    }

    protected override void OnProcess(float dt)
    {
        for (int i = 0; i < _subs.Length; i++)
        {
            ref Sub s = ref _subs[i];
            s.LocalOffset += s.Velocity * dt;
            // Mild gravity-like settle matching Sparks-style arcs when velocity has upward component.
            s.Velocity += Vector3.down * (2.5f * dt);
            if (_spin)
                s.Phase += s.Spin * dt;
        }
    }

    protected override void OnTerminateGracefully()
    {
        if (Duration < 0f || Age >= Duration)
        {
            ReadyFlag = true;
            return;
        }

        float remaining = Duration - Age;
        if (remaining > 0.35f)
            SetDuration(Age + 0.35f);
    }

    public override void CollectBillboards(List<EffectBillboardBatch.Quad> dest, Camera camera)
    {
        if (!IsAlive || dest == null)
            return;

        float t01 = Duration > 0f ? Mathf.Clamp01(Age / Duration) : 0f;
        float fade = 1f - t01;
        Matrix4x4 world = WorldMatrix;

        if (_atlas == null)
            return;

        for (int i = 0; i < _subs.Length; i++)
        {
            ref Sub s = ref _subs[i];
            Vector3 pos = world.MultiplyPoint3x4(s.LocalOffset);
            Color c = s.Color;
            c.a *= Mathf.Clamp01(fade);
            if (c.a < 0.01f)
                continue;

            Texture2D frameTex = _frames != null
                ? _frames.GetFrame(_atlas, _cols, _rows, s.Frame)
                : _atlas;
            if (frameTex == null)
                continue;

            dest.Add(new EffectBillboardBatch.Quad
            {
                Matrix = Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one),
                Scale = s.Scale * (0.85f + 0.3f * fade),
                Color = c,
                Texture = frameTex,
                Additive = _additive,
            });
        }
    }
}
