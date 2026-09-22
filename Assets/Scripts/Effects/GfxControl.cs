using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Base live gfx control. Stock advances appearance from age inside Process; no separate Advance(dt) API.
/// </summary>
public abstract class GfxControl
{
    public const float FirstFrameDt = EffectFrameRate.StockFrameDt;
    public const float WatchdogSeconds = 60f;
    public const float InfiniteDuration = -1f;

    /// <summary>
    /// How many stock frames this delta covers, for scaling stock's per-Process constants. See
    /// <see cref="EffectFrameRate"/>.
    /// </summary>
    protected static float StockFrameSteps(float dt) => EffectFrameRate.FrameSteps(dt);

    protected readonly GfxTweakRecord Record;
    protected readonly EffectLocator Locator;

    float _age;
    float _dt;
    float _duration = InfiniteDuration;
    bool _armed;
    bool _firstDtClamped;
    bool _released;
    bool _terminating;
    Matrix4x4 _matrix;

    public bool ReadyFlag { get; protected set; }
    public bool IsTerminating => _terminating;
    public bool IsAlive => !_released && !ReadyFlag;
    public float Age => _age;
    public float Dt => _dt;
    public float Duration => _duration;
    public int TypeCode => Record != null ? Record.TypeCode : 0;
    public GfxTweakRecord Template => Record;

    protected Matrix4x4 WorldMatrix => _matrix;

    protected GfxControl(GfxTweakRecord record, EffectLocator locator)
    {
        Record = record;
        Locator = locator;
    }

    /// <summary>Stock vftable slot 8, reached through <c>_EffectHandler_t::SetDuration</c> (100ce1ed).</summary>
    public virtual void SetDuration(float seconds)
    {
        _duration = seconds;
    }

    protected void SetDurationFromTemplate(int index = 8, float fallback = 1.5f)
    {
        float life = Record != null ? Record.Field(index, fallback) : fallback;
        if (life < 0f)
        {
            _duration = InfiniteDuration;
            return;
        }

        if (life < 0.05f || float.IsNaN(life) || float.IsInfinity(life))
            life = fallback;
        _duration = life;
    }

    /// <summary>Hard kill (stock Release(true)).</summary>
    public virtual void Release(bool immediate = true)
    {
        if (_released)
            return;
        _released = true;
        ReadyFlag = true;
        OnReleased(immediate);
    }

    /// <summary>Graceful wind-down (stock TerminateGracefully).</summary>
    public virtual void TerminateGracefully()
    {
        if (_released || ReadyFlag)
            return;
        _terminating = true;
        OnTerminateGracefully();
    }

    /// <summary>Legacy alias for hard kill.</summary>
    public void Kill() => Release(true);

    /// <summary>
    /// Stock slot 4 for position-template controls: <c>_GfxLocator_t</c> <c>FUN_101064f7</c>. A
    /// locator placed by a bare position only follows a new one when the template's field 0 bit 0
    /// (track) is set. Spell1 calls this on its hand children every frame.
    /// </summary>
    public virtual void UpdatePosition(Vector3 position)
    {
        if (Locator == null || !Locator.IsWorldPoint)
            return;
        if (Record == null || (Record.FieldInt(0, 0) & 1) == 0)
            return;
        Locator.SetWorldPoint(position);
    }

    /// <summary>
    /// Stock slot 10. The base <c>_GfxControl_t::NextState</c> (<c>100a76f9</c>) is a graceful
    /// terminate; Spell1 overrides it. The nano cast calls this when its result arrives.
    /// </summary>
    public virtual void NextState() => TerminateGracefully();

    /// <summary>Stock slot 11: start colour A,R,G,B. Only controls that have one override it.</summary>
    public virtual void SetStartColor(float a, float r, float g, float b) { }

    /// <summary>Stock slot 12: end colour A,R,G,B.</summary>
    public virtual void SetStopColor(float a, float r, float g, float b) { }

    public bool Process(float dt)
    {
        if (_released || ReadyFlag)
            return false;

        // Stock _GfxControl_t::Process: first call only arms and zeros age/dt.
        if (!_armed)
        {
            _armed = true;
            _dt = 0f;
            _age = 0f;
            return true;
        }

        _dt = Mathf.Max(0f, dt);
        if (!_firstDtClamped)
        {
            if (_dt > FirstFrameDt)
                _dt = FirstFrameDt;
            _firstDtClamped = true;
        }

        _age += _dt;

        if (Locator == null || !Locator.TryResolve(out _matrix))
        {
            ReadyFlag = true;
            return false;
        }

        OnProcess(_dt);

        if (!ReadyFlag && _duration >= 0f && _age >= _duration)
            ReadyFlag = true;

        if (!ReadyFlag && _age > WatchdogSeconds)
            ReadyFlag = true;

        return !ReadyFlag && !_released;
    }

    /// <summary>Collect camera-facing quads for this frame (0..N).</summary>
    public virtual void CollectBillboards(List<EffectBillboardBatch.Quad> dest, Camera camera)
    {
    }

    /// <summary>Collect triangle strips for this frame, for visuals that build their own geometry.</summary>
    public virtual void CollectStrips(List<EffectBillboardBatch.Strip> dest, Camera camera)
    {
    }

    protected virtual void OnProcess(float dt) { }
    protected virtual void OnTerminateGracefully() => ReadyFlag = true;
    protected virtual void OnReleased(bool immediate) { }

    protected static float ClampScale(float value, float fallback)
    {
        // Stock Flare/Halo templates use sizes as small as 0.05; reject only non-positive / huge.
        if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f || value > 8f)
            return fallback;
        return value;
    }

    protected static Color ReadRgb01(GfxTweakRecord record, int startIndex, Color fallback)
    {
        if (record == null || record.FieldCount <= startIndex + 2)
            return fallback;
        return new Color(
            Mathf.Clamp01(record.Field(startIndex)),
            Mathf.Clamp01(record.Field(startIndex + 1)),
            Mathf.Clamp01(record.Field(startIndex + 2)),
            1f);
    }
}
