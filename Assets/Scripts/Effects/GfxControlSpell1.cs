using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// typeCode 0x3f2 — stock <c>_GfxControlSpell1_t</c>, the nano cast effect (ctor
/// <c>Gamecode 100f39ec</c>, vftable <c>1016dbec</c>, Process <c>100f3f59</c>). The only control
/// <c>CreateEffect2(id, caster, target, attach)</c> will build (<c>100d1705</c>).
///
/// Locators: A = caster left hand (2001), B = caster right hand (2000), both replaced by the attach
/// override when one is given; C = target <c>Bip01 Spine2_ac</c> (1003), else its head attractor
/// (2002). If any of them cannot be resolved the control is ready at once.
///
/// Fields (loader <c>100f2789</c>): 0 flags, 8 duration, 9 material, 10-13 colour A (A,R,G,B),
/// 14-17 colour B, 18-26 window times, 27-32 child ids, 33 "no late windows".
///
/// Everything runs off the control's age in four windows (dispatch <c>100f3f30</c>):
/// <list type="bullet">
/// <item>Window 1, [f18, f20]: on entry build children f29 and f27 on hand A and f30 and f28 on hand B
/// as owned controls, and push colour A / colour B into them as start / stop colour (<c>100f29ea</c>).
/// Each later frame move them to the hands and run them (<c>100f2bab</c>); from f19 on, terminate them
/// gracefully once. Past f20 delete them (<c>100f2c66</c>).</item>
/// <item>Window 2, [f21, f22]: a trail of sprites between the hands, redrawn every frame
/// (<see cref="Spell1Trail"/>), in colour A.</item>
/// <item>Windows 3 and 4 only when field 33 is 0. UNVERIFIED: their bodies below are the earlier
/// port's model and have not been checked against 100f2d63 / 100f3070 / 100f2e49 / 100f31b1.</item>
/// </list>
/// Stock SetDuration is a no-op here (slot 8 is a stub), TerminateGracefully readies the control
/// outright (slot 6, <c>100f2612</c>), and the destructor deletes the window-1 children
/// (<c>100f3c6c</c>). The cast's result calls <see cref="NextState"/> (slot 10, <c>100f2617</c>).
/// </summary>
public sealed class GfxControlSpell1 : GfxControl
{
    /// <summary>Window-1 child order, stock +0x140..+0x14c: fields 29, 30, 27, 28.</summary>
    static readonly int[] Window1Field = { 29, 30, 27, 28 };
    static readonly bool[] Window1OnB = { false, true, false, true };

    readonly Dynel _caster;
    readonly Dynel _target;
    readonly VisualDynel _casterVisual;
    readonly VisualDynel _targetVisual;
    readonly IEffectSpawnFactory _factory;
    readonly Texture2D _texture;
    readonly int _handAttachA;
    readonly int _handAttachB;

    readonly float _durationField;
    readonly float[] _colorA = new float[4];
    readonly float[] _colorB = new float[4];
    /// <summary>Fields 18-26 (+0x64..+0x84). NextState rewrites some of them.</summary>
    readonly float[] _w = new float[9];
    readonly bool _skipLateWindows;

    Vector3 _posA;
    Vector3 _posB;
    Vector3 _posC;

    readonly GfxControl[] _window1 = new GfxControl[4];
    bool _w1Entered;
    bool _w1MidFired;
    bool _w1Exited;
    bool _w2Entered;
    bool _w2Past;
    uint _trailArgb;
    readonly float[] _passes = new float[8];

    struct TrailSprite
    {
        public Vector3 Pos;
        public float Size;
    }

    readonly List<TrailSprite> _trail = new List<TrailSprite>(128);

    /// <summary>Window-1 child <paramref name="i"/> (stock +0x140 + 4i), or null. For debug tooling.</summary>
    public GfxControl Window1Child(int i) => i >= 0 && i < _window1.Length ? _window1[i] : null;

    /// <summary>Trail sprites drawn this frame. For debug tooling.</summary>
    public int TrailSpriteCount => _trail.Count;

    /// <summary>Locator A / B positions from the last Process. For debug tooling.</summary>
    public Vector3 HandA => _posA;
    public Vector3 HandB => _posB;

    // Late windows (UNVERIFIED model).
    readonly int _childId31;
    readonly int _childId32;
    float _scale;
    float _scaleBase = 1f;
    float _scaleMax = float.MaxValue;
    Vector3 _midStored;
    bool _w3Entered;
    bool _w3Exited;
    bool _w4Entered;
    bool _w4Exited;
    EffectHandle _child150;
    EffectHandle _child154;
    EffectLocator _loc150;

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
        // Stock Spell1 takes no tint; colour comes from fields 10-17 only.
        _caster = caster;
        _target = target != null ? target : caster;
        _factory = factory;
        _texture = texture;

        _handAttachA = attachOverride != 0 ? attachOverride : EffectAttachIds.LeftHand;
        _handAttachB = attachOverride != 0 ? attachOverride : EffectAttachIds.RightHand;

        _casterVisual = casterVisual != null ? casterVisual : GetVisual(_caster);
        _targetVisual = targetVisual != null ? targetVisual : (GetVisual(_target) ?? _casterVisual);

        _durationField = record != null ? record.Field(8, -1f) : -1f;
        for (int c = 0; c < 4; c++)
        {
            _colorA[c] = record != null ? record.Field(10 + c) : 0f;
            _colorB[c] = record != null ? record.Field(14 + c) : 0f;
        }
        for (int i = 0; i < 9; i++)
            _w[i] = record != null ? record.Field(18 + i) : 0f;
        _childId31 = record != null ? record.FieldInt(31, 0) : 0;
        _childId32 = record != null ? record.FieldInt(32, 0) : 0;
        _skipLateWindows = record != null && record.FieldInt(33, 0) != 0;

        base.SetDuration(InfiniteDuration);

        if (!ResolveLocators())
            ReadyFlag = true;
        else
            RecomputeScale();
    }

    static VisualDynel GetVisual(Dynel dynel)
    {
        if (dynel is Character character)
            return character.Visual;
        return null;
    }

    int ChildId(int field) => Record != null ? Record.FieldInt(field, 0) : 0;

    bool ResolveLocators()
    {
        return TryResolveAttach(_casterVisual, _caster, _handAttachA, out _posA)
            && TryResolveAttach(_casterVisual, _caster, _handAttachB, out _posB)
            && (TryResolveAttach(_targetVisual, _target, EffectAttachIds.BoneSpine2, out _posC)
                || TryResolveAttach(_targetVisual, _target, EffectAttachIds.Head, out _posC));
    }

    static bool TryResolveAttach(VisualDynel visual, Dynel dynel, int attachId, out Vector3 pos)
    {
        pos = default;
        if (visual != null && visual.TryGetAttachMatrixStrict(attachId, out Matrix4x4 m))
        {
            pos = m.GetColumn(3);
            return true;
        }

        if (dynel != null)
        {
            // Strict: stock fails when the named attach is missing on a dynel with a visual.
            if (dynel is Character character && character.Visual != null)
                return false;
            pos = dynel.transform.position;
            return attachId == 0;
        }

        return false;
    }

    /// <summary>Stock slot 8 is a stub (<c>10079931 RET 4</c>): the cast's SetDuration(6000) does nothing.</summary>
    public override void SetDuration(float seconds)
    {
    }

    /// <summary>
    /// Stock slot 10 (<c>100f2617</c>). With field 33 clear and window 1 not yet over, pull the
    /// timeline to now: f20 = f19 + age - f20, then f19 = f21 = age, f22 = age + 0.5,
    /// f23 = age + 0.35. With field 33 set it does nothing.
    /// </summary>
    public override void NextState()
    {
        if (_skipLateWindows || ReadyFlag)
            return;
        if (!(_w[2] > Age))
            return;

        _w[2] = _w[1] + Age - _w[2];
        _w[1] = Age;
        _w[3] = Age;
        _w[4] = (float)(Age + 0.5);
        _w[5] = (float)(Age + 0.3499999940395355);
    }

    /// <summary>Stock slot 6 (<c>100f2612</c>): ready at once; the destructor deletes the children.</summary>
    protected override void OnTerminateGracefully() => ReadyFlag = true;

    protected override void OnProcess(float dt)
    {
        _trail.Clear();

        // _GfxControl_t::Process: ready once 0 < duration < age.
        if (_durationField > 0f && _durationField < Age)
        {
            ReadyFlag = true;
            return;
        }

        if (!ResolveLocators())
        {
            ReadyFlag = true;
            return;
        }

        RecomputeScale();
        Window1(dt);
        Window2();
        if (!_skipLateWindows)
        {
            Window3(dt);
            Window4();
        }
    }

    void Window1(float dt)
    {
        float age = Age;
        if (!(_w[0] <= age))
            return;

        if (age <= _w[2])
        {
            if (!_w1Entered)
            {
                _w1Entered = true;
                SpawnWindow1();
                return;
            }

            if (_w[1] <= age && !_w1MidFired)
            {
                _w1MidFired = true;
                for (int i = 0; i < _window1.Length; i++)
                    _window1[i]?.TerminateGracefully();
            }

            for (int i = 0; i < _window1.Length; i++)
            {
                GfxControl child = _window1[i];
                if (child == null)
                    continue;
                child.UpdatePosition(Window1OnB[i] ? _posB : _posA);
                child.Process(dt);
            }
            return;
        }

        if (!_w1Exited)
        {
            _w1Exited = true;
            DeleteWindow1();
        }
    }

    void SpawnWindow1()
    {
        if (_factory == null)
            return;

        for (int i = 0; i < _window1.Length; i++)
        {
            int id = ChildId(Window1Field[i]);
            if (id <= 0)
                continue;
            Vector3 pos = Window1OnB[i] ? _posB : _posA;
            _window1[i] = _factory.CreateOwnedControl(id, EffectLocator.WorldPoint(pos, Quaternion.identity));
        }

        for (int i = 0; i < _window1.Length; i++)
        {
            GfxControl child = _window1[i];
            if (child == null)
                continue;
            child.SetStartColor(_colorA[0], _colorA[1], _colorA[2], _colorA[3]);
            child.SetStopColor(_colorB[0], _colorB[1], _colorB[2], _colorB[3]);
        }
    }

    void DeleteWindow1()
    {
        for (int i = 0; i < _window1.Length; i++)
        {
            _window1[i]?.Release(true);
            _window1[i] = null;
        }
    }

    void Window2()
    {
        float age = Age;
        if (!(_w[3] <= age))
            return;

        if (age <= _w[4])
        {
            if (!_w2Entered)
            {
                _w2Entered = true;
                _trailArgb = Spell1Trail.PackColor(_colorA[0], _colorA[1], _colorA[2], _colorA[3]);
                return;
            }

            int passes = Spell1Trail.WindowPasses(age, _w[3], _w[4], _passes);
            Vector3 delta = _posB - _posA;
            float distance = delta.magnitude;
            for (int p = 0; p < passes; p++)
            {
                Spell1Trail.Pass(
                    distance,
                    _passes[p * 4], _passes[p * 4 + 1], _passes[p * 4 + 2], _passes[p * 4 + 3],
                    () => Random.value,
                    (t, size) =>
                    {
                        // GfxVisualSprite2Type2::Init(256) caps the sprites a frame can hold.
                        if (_trail.Count < 256)
                            _trail.Add(new TrailSprite { Pos = _posA + delta * t, Size = size });
                    });
            }
            return;
        }

        if (!_w2Past)
            _w2Past = true;
    }

    // ---- Late windows: UNVERIFIED (earlier port model, only reached when field 33 is 0) ----------

    void RecomputeScale()
    {
        Vector3 mid = Vector3.Lerp(_posA, _posB, 0.5f);
        float dist = Vector3.Distance(mid, _posC);
        _scale = dist * _scaleBase;
        if (_scale > _scaleMax)
            _scale = _scaleMax;
    }

    void Window3(float dt)
    {
        float age = Age;
        if (_w[5] <= age && age <= _w[6])
        {
            if (!_w3Entered)
            {
                _w3Entered = true;
                _midStored = Vector3.Lerp(_posA, _posB, 0.5f);
                _scaleBase = 5f;
                _scaleMax = 10f;
                RecomputeScale();
                _loc150 = EffectLocator.WorldPoint(_midStored, Quaternion.identity);
                if (_factory != null && _childId31 > 0)
                    _child150 = _factory.SpawnChild(_childId31, _loc150, Color.white);
            }
            else
            {
                Vector3 toC = _posC - _midStored;
                float dist = toC.magnitude;
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

        if (_w3Entered && !_w3Exited && age > _w[6])
        {
            _w3Exited = true;
            Delete(ref _child150);
            _loc150 = null;
        }
    }

    void Window4()
    {
        float age = Age;
        if (_w[7] <= age && age <= _w[8])
        {
            if (!_w4Entered)
            {
                _w4Entered = true;
                if (_factory != null && _childId32 > 0 && _target != null)
                {
                    var loc = EffectLocator.OnDynel(_target, EffectAttachIds.BoneSpine2);
                    _child154 = _factory.SpawnChild(_childId32, loc, Color.white);
                    if (_child154 == null)
                    {
                        loc = EffectLocator.OnDynel(_target, EffectAttachIds.Head);
                        _child154 = _factory.SpawnChild(_childId32, loc, Color.white);
                    }
                }
            }
            return;
        }

        if (_w4Entered && !_w4Exited && age > _w[8])
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

    protected override void OnReleased(bool immediate)
    {
        DeleteWindow1();
        Delete(ref _child150);
        Delete(ref _child154);
        _loc150 = null;
        _trail.Clear();
    }

    public override void CollectBillboards(List<EffectBillboardBatch.Quad> dest, Camera camera)
    {
        if (!IsAlive || dest == null)
            return;

        for (int i = 0; i < _window1.Length; i++)
            _window1[i]?.CollectBillboards(dest, camera);

        if (_texture == null || _trail.Count == 0)
            return;

        uint argb = _trailArgb;
        var color = new Color(
            ((argb >> 16) & 0xff) / 255f,
            ((argb >> 8) & 0xff) / 255f,
            (argb & 0xff) / 255f,
            ((argb >> 24) & 0xff) / 255f);
        for (int i = 0; i < _trail.Count; i++)
        {
            TrailSprite s = _trail[i];
            dest.Add(new EffectBillboardBatch.Quad
            {
                // GfxVisualSprite2Type2 (10027df8): camera-facing square, full side = size.
                Matrix = Matrix4x4.TRS(s.Pos, Quaternion.identity, Vector3.one),
                Scale = s.Size,
                Color = color,
                Texture = _texture,
                // GfxVisualSprite2Type2(material, null, true): SRCALPHA / ONE.
                Additive = true,
            });
        }
    }
}
