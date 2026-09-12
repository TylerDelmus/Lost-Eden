using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Base live gfx control. Stock advances appearance from age inside Process; no separate Advance(dt) API.
/// </summary>
public abstract class GfxControl
{
    public const float FirstFrameDt = 0.033f;
    public const float WatchdogSeconds = 60f;
    public const float InfiniteDuration = -1f;

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

    public void SetDuration(float seconds)
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
