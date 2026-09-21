using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// typeCode 1003 — stock <c>_GfxControlCord_t</c>. A glowing ribbon threaded through the emitter's
/// recent positions, not a pool of sprites.
///
/// Stock gives this control its own visual, <c>GfxVisualCord4</c>, which has no NewSprite at all.
/// The control calls <c>GetLinkList</c> and appends to a <c>Cord4CircularLinkList</c>, and the visual
/// joins consecutive links into a strip. Each <c>Cord4Link</c> the append at Gamecode FUN_100d60fc
/// writes holds:
///
/// <code>
/// +0x00 Vector3    first point
/// +0x0c Vector3    second point
/// +0x18 float      width,  from control +0x44 = field 12
/// +0x1c D3DCOLOR   packed A,R,G,B from +0x54..+0x60 = fields 16-19
/// +0x20 float      life,   from control +0xa0 = field 35
/// </code>
///
/// and the Cord's spawn at FUN_100d67b1 pushes the same pointer for both of those points:
///
/// <code>
/// PUSH dword ptr [EBP + 0x8]
/// PUSH dword ptr [EBP + 0x8]
/// CALL 0x100d60fc
/// </code>
///
/// So a link is one sampled point, and the ribbon's whole shape comes from the emitter's motion
/// between samples. That is why all 24 Cord templates set magnitude (fields 29/30) to 0, and why
/// their spawn cone and second size channel go unread — a cord has no spread of its own. 8000/8001
/// sample the caster's two hands 60 times a second on a 1 second life, so each hand trails a ribbon
/// of its last second of movement, 0.05 wide, in a warm white.
///
/// Running these through the pooled sprite emitter stacks every filament on the attach point, which
/// draws a bar standing on the hand instead of a trail behind it.
///
/// The colour is captured once per link rather than animated, so the ribbon does not fade along its
/// length; a link simply disappears when its life runs out.
/// </summary>
public sealed class GfxControlCord : GfxControl
{
    /// <summary>field0 bit: control finishes once every link has expired.</summary>
    const int FlagDieWhenEmpty = 0x200;

    /// <summary>Links stock would never need, since a cord samples at a bounded rate over a bounded life.</summary>
    const int MaxLinks = 256;

    /// <summary>
    /// Separation below which two samples are treated as the same point. A ribbon segment shorter
    /// than this is thinner than the cord is wide, so it can only add a smear of overdraw on the
    /// attach point — which is exactly what a still hand produces, 60 times a second.
    /// </summary>
    const float MinSegment = 1e-3f;

    struct Link
    {
        public Vector3 Point;
        public float Age;
    }

    readonly Texture2D _atlas;
    readonly EffectAtlasFrames _frames;
    readonly int _cols;
    readonly int _rows;
    readonly int _frame;
    readonly EffectLightPool _lights;
    EffectLightLease _lightLease;

    readonly int _flags;
    readonly bool _additive;
    readonly float _rate;
    readonly int _randMask;
    readonly float _width;
    readonly float _life;
    readonly Color _color;

    // Oldest to newest, wrapping. Links expire in the order they were added, so the live run is
    // always contiguous and can be retired from its tail.
    readonly Link[] _links;
    int _head;
    int _count;
    int _spawnCounter;

    public GfxControlCord(
        GfxTweakRecord record,
        EffectLocator locator,
        Texture2D atlas,
        EffectAtlasFrames frames,
        int cols,
        int rows,
        int firstFrame,
        int lastFrame,
        Color tint,
        EffectLightPool lights = null)
        : base(record, locator)
    {
        _atlas = atlas;
        _frames = frames;
        _cols = Mathf.Max(1, cols);
        _rows = Mathf.Max(1, rows);
        // A link carries no frame index, so a cord is drawn with one texture for its whole life.
        _frame = firstFrame;
        _lights = lights;

        _flags = record != null ? record.FieldInt(0, 0) : 0;
        _rate = record != null ? record.Field(10, 0f) : 0f;
        _randMask = record != null ? record.FieldInt(24, 0) : 0;
        _width = record != null ? record.Field(12, 0.05f) : 0.05f;
        _life = record != null ? Mathf.Max(0.02f, record.Field(35, 1f)) : 1f;

        Color color = EffectColors.ReadArgbBlock(record, 16, Color.white);
        _color = EffectColors.IsOverrideTint(tint) ? EffectColors.ApplyTint(color, tint) : color;

        int burstCount = record != null ? record.FieldInt(31, 1) : 1;
        _links = new Link[SpriteEmitterMath.PoolCapacity(burstCount, _rate, _life, MaxLinks)];
        _spawnCounter = SpriteEmitterMath.InitialSpawnCounter(burstCount);

        SetDurationFromTemplate(8, 4f);

        _additive = SpriteEmitterMath.IsAdditive(_flags);
        if (_additive && _lights != null)
            _lights.TryAcquire(out _lightLease);
    }

    int IndexFromOldest(int k) => (_head - _count + k + _links.Length * 2) % _links.Length;

    protected override void OnProcess(float dt)
    {
        for (int k = 0; k < _count; k++)
            _links[IndexFromOldest(k)].Age += dt;

        while (_count > 0 && _links[IndexFromOldest(0)].Age >= _life)
            _count--;

        bool gateOpen = SpriteEmitterMath.RandMaskPasses(
                _randMask, Random.Range(int.MinValue, int.MaxValue))
            && SpriteEmitterMath.WithinEmitWindow(Duration, Age, _life);

        if (gateOpen && !IsTerminating)
        {
            int target = SpriteEmitterMath.TargetSpawnCount(_rate, Age);
            Vector3 point = WorldMatrix.GetColumn(3);

            // Appending more links than the ring holds only overwrites the ones just written, so a
            // long frame cannot be made to sample the hand in more places than it has been.
            int budget = _links.Length;
            while (_spawnCounter < target && budget-- > 0)
            {
                _links[_head] = new Link { Point = point, Age = 0f };
                _head = (_head + 1) % _links.Length;
                if (_count < _links.Length)
                    _count++;
                _spawnCounter++;
            }
            if (_spawnCounter < target)
                _spawnCounter = target;
        }

        if ((_flags & FlagDieWhenEmpty) != 0 && _count == 0 && _spawnCounter >= 0)
            ReadyFlag = true;
    }

    protected override void OnTerminateGracefully()
    {
        // Stock lets the ribbon run out rather than cutting it: duration = age + link life.
        SetDuration(Age + _life);
    }

    protected override void OnReleased(bool immediate)
    {
        _lightLease?.Release();
        _lightLease = null;
    }

    public override void CollectBillboards(List<EffectBillboardBatch.Quad> dest, Camera camera)
    {
        if (!IsAlive || dest == null)
        {
            _lightLease?.Release();
            _lightLease = null;
            return;
        }

        Color color = _color;
        color.a = Mathf.Clamp01(color.a);
        if (color.a < 0.01f || _count < 2)
        {
            UpdateLight(Color.black, 0f);
            return;
        }

        Texture2D tex = _frames != null
            ? _frames.GetFrame(_atlas, _cols, _rows, _frame)
            : _atlas;
        if (tex == null)
            return;

        // One quad per consecutive pair of samples, laid along the gap between them, which is what
        // turns the chain of points into a ribbon.
        for (int k = 1; k < _count; k++)
        {
            Vector3 from = _links[IndexFromOldest(k - 1)].Point;
            Vector3 to = _links[IndexFromOldest(k)].Point;
            Vector3 span = to - from;
            if (span.sqrMagnitude < MinSegment * MinSegment)
                continue;

            dest.Add(new EffectBillboardBatch.Quad
            {
                Matrix = Matrix4x4.TRS((from + to) * 0.5f, Quaternion.identity, Vector3.one),
                Scale = _width,
                Stretch = span,
                Color = color,
                Texture = tex,
                Additive = _additive,
            });
        }

        UpdateLight(color, _width);
    }

    /// <summary>
    /// A cord glows from the attach point it is anchored to, so the light stays on the emitter rather
    /// than chasing the far end of the ribbon.
    /// </summary>
    void UpdateLight(Color color, float scale)
    {
        if (_lightLease == null)
            return;

        if (scale <= 0f)
        {
            if (_count == 0 && !SpriteEmitterMath.WithinEmitWindow(Duration, Age, _life))
            {
                _lightLease.Release();
                _lightLease = null;
                return;
            }

            _lightLease.Update(WorldMatrix.GetColumn(3), Color.black, 0f, 1f);
            return;
        }

        _lightLease.Update(
            WorldMatrix.GetColumn(3),
            color,
            EffectLightPool.NanoIntensity(color),
            EffectLightPool.NanoRange(scale));
    }
}
