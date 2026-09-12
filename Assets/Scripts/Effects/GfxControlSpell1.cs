using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stock typeCode 0x3f2 (_GfxControlSpell1_t): caster L/R hands + target chest/head,
/// window-2 camera-facing sprite chain along hands, age-gated child effects.
/// </summary>
public sealed class GfxControlSpell1 : GfxControl
{
    const float SafetyDuration = 60f;
    const int MaxTrailSprites = 50;

    readonly Dynel _caster;
    readonly Dynel _target;
    readonly VisualDynel _casterVisual;
    readonly VisualDynel _targetVisual;
    readonly IEffectSpawnFactory _factory;
    readonly Texture2D _texture;
    readonly Color _tint;
    readonly int _handAttachA;
    readonly int _handAttachB;

    readonly float _w1Start;
    readonly float _w1Mid;
    readonly float _w1End;
    readonly float _w2Start;
    readonly float _w2End;
    readonly float _w3Start;
    readonly float _w3End;
    readonly float _w4Start;
    readonly float _w4End;
    readonly int _childId27;
    readonly int _childId28;
    readonly int _childId29;
    readonly int _childId30;
    readonly int _childId31;
    readonly int _childId32;
    readonly bool _skipLateWindows;
    readonly Color _colorA;
    readonly Color _colorB;

    bool _broken;
    bool _active;
    Vector3 _posA;
    Vector3 _posB;
    Vector3 _posC;
    Quaternion _rotA;
    float _scale;
    float _scaleBase = 1f;
    float _scaleMax = float.MaxValue;
    Vector3 _midStored;
    Color _trailColor = Color.white;

    bool _w1Entered;
    bool _w1MidFired;
    bool _w1Exited;
    bool _w2Entered;
    bool _w2Past;
    bool _w3Entered;
    bool _w3Exited;
    bool _w4Entered;
    bool _w4Exited;

    EffectHandle _child140;
    EffectHandle _child144;
    EffectHandle _child148;
    EffectHandle _child14c;
    EffectHandle _child150;
    EffectHandle _child154;
    EffectLocator _loc140;
    EffectLocator _loc144;
    EffectLocator _loc148;
    EffectLocator _loc14c;
    EffectLocator _loc150;

    struct TrailSprite
    {
        public Vector3 Pos;
        public float Size;
        public Color Color;
    }

    readonly List<TrailSprite> _trail = new List<TrailSprite>(64);

    public GfxControlSpell1(
        GfxTweakRecord record,
        EffectLocator locator,
        Dynel caster,
        Dynel target,
        IEffectSpawnFactory factory,
        Texture2D texture,
        Color tint,
        int attachOverride = 0,
        VisualDynel casterVisual = null,
        VisualDynel targetVisual = null)
        : base(record, locator)
    {
        _caster = caster;
        _target = target != null ? target : caster;
        _factory = factory;
        _texture = texture;
        _tint = EffectColors.IsOverrideTint(tint) ? tint : Color.white;

        _handAttachA = attachOverride != 0 ? attachOverride : EffectAttachIds.LeftHand;
        _handAttachB = attachOverride != 0 ? attachOverride : EffectAttachIds.RightHand;

        _casterVisual = casterVisual != null ? casterVisual : GetVisual(_caster);
        _targetVisual = targetVisual != null ? targetVisual : (GetVisual(_target) ?? _casterVisual);

        _colorA = EffectColors.ReadArgbBlock(record, 10, Color.white);
        _colorB = EffectColors.ReadArgbBlock(record, 14, _colorA);
        _w1Start = record != null ? record.Field(18, 0f) : 0f;
        _w1Mid = record != null ? record.Field(19, _w1Start) : 0f;
        _w1End = record != null ? record.Field(20, _w1Start) : 0f;
        _w2Start = record != null ? record.Field(21, 0f) : 0f;
        _w2End = record != null ? record.Field(22, 0f) : 0f;
        _w3Start = record != null ? record.Field(23, 0f) : 0f;
        _w3End = record != null ? record.Field(24, 0f) : 0f;
        _w4Start = record != null ? record.Field(25, 0f) : 0f;
        _w4End = record != null ? record.Field(26, 0f) : 0f;
        _childId27 = record != null ? record.FieldInt(27, 0) : 0;
        _childId28 = record != null ? record.FieldInt(28, 0) : 0;
        _childId29 = record != null ? record.FieldInt(29, 0) : 0;
        _childId30 = record != null ? record.FieldInt(30, 0) : 0;
        _childId31 = record != null ? record.FieldInt(31, 0) : 0;
        _childId32 = record != null ? record.FieldInt(32, 0) : 0;
        _skipLateWindows = record != null && record.FieldCount > 33 && record.FieldInt(33, 0) != 0;

        if (!TryResolveAttach(_casterVisual, _caster, _handAttachA, out _posA, out _rotA)
            || !TryResolveAttach(_casterVisual, _caster, _handAttachB, out _posB, out _)
            || !TryResolveLocatorC(out _posC, out _))
        {
            _broken = true;
            ReadyFlag = true;
            return;
        }

        RecomputeScale();
        SetDuration(SafetyDuration);
        _active = true;
        _trailColor = ApplyTint(_colorA);
    }

    static VisualDynel GetVisual(Dynel dynel)
    {
        if (dynel is Character character)
            return character.Visual;
        return null;
    }

    Color ApplyTint(Color c) => EffectColors.ApplyTint(c, _tint);

    bool TryResolveLocatorC(out Vector3 pos, out Quaternion rot)
    {
        if (TryResolveAttach(_targetVisual, _target, EffectAttachIds.BoneSpine2, out pos, out rot))
            return true;
        return TryResolveAttach(_targetVisual, _target, EffectAttachIds.Head, out pos, out rot);
    }

    static bool TryResolveAttach(
        VisualDynel visual,
        Dynel dynel,
        int attachId,
        out Vector3 pos,
        out Quaternion rot)
    {
        pos = default;
        rot = Quaternion.identity;
        Matrix4x4 m;
        if (visual != null && visual.TryGetAttachMatrixStrict(attachId, out m))
        {
            pos = m.GetColumn(3);
            rot = m.rotation;
            return true;
        }

        if (dynel != null)
        {
            // Strict: stock fails when the named attach is missing on a dynel with a visual.
            if (dynel is Character character && character.Visual != null)
                return false;
            pos = dynel.transform.position;
            rot = dynel.transform.rotation;
            return attachId == 0;
        }

        if (visual != null)
        {
            Transform t = visual.VisualRoot != null ? visual.VisualRoot.transform : visual.transform;
            pos = t.position;
            rot = t.rotation;
            return false;
        }

        return false;
    }

    void RecomputeScale()
    {
        Vector3 mid = Vector3.Lerp(_posA, _posB, 0.5f);
        float dist = Vector3.Distance(mid, _posC);
        _scale = dist * _scaleBase;
        if (_scale > _scaleMax)
            _scale = _scaleMax;
    }

    protected override void OnProcess(float dt)
    {
        _trail.Clear();
        if (_broken || !_active)
            return;

        if (!TryResolveAttach(_casterVisual, _caster, _handAttachA, out _posA, out _rotA)
            || !TryResolveAttach(_casterVisual, _caster, _handAttachB, out _posB, out _)
            || !TryResolveLocatorC(out _posC, out _))
        {
            _broken = true;
            return;
        }

        RecomputeScale();
        DispatchWindow1();
        DispatchWindow2();
        if (!_skipLateWindows)
        {
            DispatchWindow3(dt);
            DispatchWindow4();
        }
    }

    void DispatchWindow1()
    {
        float age = Age;
        if (_w1Start <= age && age <= _w1End)
        {
            if (!_w1Entered)
            {
                _w1Entered = true;
                SpawnWindow1Children();
            }
            else
            {
                if (!_w1MidFired && _w1Mid < age)
                {
                    _w1MidFired = true;
                    TerminateWindow1ChildrenGracefully();
                }

                PushWindow1Positions();
            }

            return;
        }

        if (_w1Entered && !_w1Exited && age > _w1End)
        {
            _w1Exited = true;
            DestroyWindow1Children();
        }
    }

    void SpawnWindow1Children()
    {
        if (_factory == null)
            return;

        _loc140 = EffectLocator.WorldPoint(_posA, _rotA);
        _loc144 = EffectLocator.WorldPoint(_posB, Quaternion.identity);
        _loc148 = EffectLocator.WorldPoint(_posA, _rotA);
        _loc14c = EffectLocator.WorldPoint(_posB, Quaternion.identity);

        _child140 = SpawnWorld(_childId29, _loc140, ApplyTint(_colorA));
        _child144 = SpawnWorld(_childId30, _loc144, ApplyTint(_colorA));
        _child148 = SpawnWorld(_childId27, _loc148, ApplyTint(_colorB));
        _child14c = SpawnWorld(_childId28, _loc14c, ApplyTint(_colorB));
    }

    EffectHandle SpawnWorld(int effectId, EffectLocator locator, Color tint)
    {
        if (effectId <= 0 || locator == null)
            return null;
        return _factory.SpawnChild(effectId, locator, tint);
    }

    void PushWindow1Positions()
    {
        _loc140?.SetWorldPoint(_posA, _rotA);
        _loc144?.SetWorldPoint(_posB, Quaternion.identity);
        _loc148?.SetWorldPoint(_posA, _rotA);
        _loc14c?.SetWorldPoint(_posB, Quaternion.identity);
    }

    void TerminateWindow1ChildrenGracefully()
    {
        _factory?.TerminateEffectGracefully(_child140);
        _factory?.TerminateEffectGracefully(_child144);
        _factory?.TerminateEffectGracefully(_child148);
        _factory?.TerminateEffectGracefully(_child14c);
    }

    void DestroyWindow1Children()
    {
        Delete(ref _child140);
        Delete(ref _child144);
        Delete(ref _child148);
        Delete(ref _child14c);
        _loc140 = _loc144 = _loc148 = _loc14c = null;
    }

    void DispatchWindow2()
    {
        float age = Age;
        if (_w2Start <= age && age <= _w2End)
        {
            if (!_w2Entered)
            {
                _w2Entered = true;
                _trailColor = ApplyTint(_colorA);
            }

            EmitWindow2Trail(age);
            return;
        }

        if (_w2Entered && !_w2Past && age > _w2End)
            _w2Past = true;
    }

    void EmitWindow2Trail(float age)
    {
        float w = _w2End - _w2Start;
        if (w <= 1e-6f)
            return;

        float half = w * 0.5f;
        float t = age - _w2Start;

        if (t < half)
        {
            float u = (t / half) * 0.5f;
            NewSpritePass(0f, 0.75f - u, u, 0.25f);
            NewSpritePass(1f - u, 0.75f - u, 1f - u, 0.25f);
        }
        else if (t < w)
        {
            float u = (t - half) / half;
            float s = u * 0.5f;
            float c = u * 0.75f + 0.25f;
            NewSpritePass(s, 0.25f, 0.5f, c);
            NewSpritePass(1f - s, 0.25f, 0.5f, c);
        }
    }

    /// <summary>Stock FUN_100f2fde(t0, size0, t1, size1).</summary>
    void NewSpritePass(float t0, float size0, float t1, float size1)
    {
        Vector3 delta = _posB - _posA;
        float len = delta.magnitude;
        float span = Mathf.Abs(t1 - t0);
        int count = Mathf.RoundToInt(len * span * 50f);
        if (count < 2)
            count = 2;
        if (count > MaxTrailSprites)
            count = MaxTrailSprites;

        float t = t0;
        float size = size0;
        float tStep = (count <= 1) ? 0f : (t1 - t0) / (count - 1);
        float sizeStep = (count <= 1) ? 0f : (size1 - size0) / (count - 1);

        for (int i = 0; i < count; i++)
        {
            float jittered = t + (Random.value * 0.02f - 0.01f);
            Vector3 pos = Vector3.Lerp(_posA, _posB, Mathf.Clamp01(jittered));
            _trail.Add(new TrailSprite
            {
                Pos = pos,
                Size = Mathf.Max(0.01f, size),
                Color = _trailColor,
            });
            t += tStep;
            size += sizeStep;
        }
    }

    void DispatchWindow3(float dt)
    {
        float age = Age;
        if (_w3Start <= age && age <= _w3End)
        {
            if (!_w3Entered)
            {
                _w3Entered = true;
                _midStored = Vector3.Lerp(_posA, _posB, 0.5f);
                _scaleBase = 5f;
                _scaleMax = 10f;
                RecomputeScale();

                Color tint = ApplyTint(_colorA);
                _loc150 = EffectLocator.WorldPoint(_midStored, Quaternion.identity);
                _child150 = SpawnWorld(_childId31, _loc150, tint);
            }
            else
            {
                // FUN_100f2e49 — grow scale toward mid→C distance, move mid by dt * scale.
                Vector3 toC = _posC - _midStored;
                float dist = toC.magnitude;
                if (dist < _scale * Mathf.Max(dt, 1e-6f) && !_w4Entered)
                {
                    // Pull window 4 earlier (stock closes w3 end / shifts w4).
                    // Soften by ending w3 at current age conceptually — just allow w4 next.
                }

                float grown = dist * _scaleBase;
                if (grown > _scaleMax)
                    grown = _scaleMax;
                if (grown > _scale)
                    _scale = grown;

                if (dist > 1e-6f)
                    _midStored += toC.normalized * (_scale * dt);

                _loc150?.SetWorldPoint(_midStored, Quaternion.identity);
            }

            return;
        }

        if (_w3Entered && !_w3Exited && age > _w3End)
        {
            _w3Exited = true;
            Delete(ref _child150);
            _loc150 = null;
        }
    }

    void DispatchWindow4()
    {
        float age = Age;
        if (_w4Start <= age && age <= _w4End)
        {
            if (!_w4Entered)
            {
                _w4Entered = true;
                if (_factory != null && _childId32 > 0 && _target != null)
                {
                    var loc = EffectLocator.OnDynel(_target, EffectAttachIds.BoneSpine2);
                    _child154 = _factory.SpawnChild(_childId32, loc, ApplyTint(_colorA));
                    if (_child154 == null)
                    {
                        loc = EffectLocator.OnDynel(_target, EffectAttachIds.Head);
                        _child154 = _factory.SpawnChild(_childId32, loc, ApplyTint(_colorA));
                    }
                }
            }

            return;
        }

        if (_w4Entered && !_w4Exited && age > _w4End)
        {
            _w4Exited = true;
            Delete(ref _child154);
        }
    }

    void Delete(ref EffectHandle handle)
    {
        if (handle == null)
            return;
        _factory?.DeleteEffect(handle);
        handle = null;
    }

    protected override void OnTerminateGracefully()
    {
        // Cast finished / interrupted: fade hand children instead of hard-killing them.
        // Detach refs so OnReleased does not DeleteEffect while they are still fading
        // (children are independently registered handles).
        TerminateWindow1ChildrenGracefully();
        _factory?.TerminateEffectGracefully(_child150);
        _factory?.TerminateEffectGracefully(_child154);
        _child140 = _child144 = _child148 = _child14c = null;
        _child150 = null;
        _child154 = null;
        _loc140 = _loc144 = _loc148 = _loc14c = null;
        _loc150 = null;
        ReadyFlag = true;
    }

    protected override void OnReleased(bool immediate)
    {
        DestroyWindow1Children();
        Delete(ref _child150);
        Delete(ref _child154);
        _loc150 = null;
        _trail.Clear();
    }

    public override void CollectBillboards(List<EffectBillboardBatch.Quad> dest, Camera camera)
    {
        if (!_active || _broken || dest == null || _texture == null)
            return;

        for (int i = 0; i < _trail.Count; i++)
        {
            TrailSprite s = _trail[i];
            dest.Add(new EffectBillboardBatch.Quad
            {
                Matrix = Matrix4x4.TRS(s.Pos, Quaternion.identity, Vector3.one),
                // Unit quad is ±0.5; Scale == stock NewSprite size (draw uses size*0.5 half-extents).
                Scale = s.Size,
                Color = s.Color,
                Texture = _texture,
                Additive = true,
            });
        }
    }
}
