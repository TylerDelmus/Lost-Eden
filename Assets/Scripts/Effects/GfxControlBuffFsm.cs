using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// typeCode 1001 (0x3e9), stock <c>_GfxControlBPHFSM_t</c> (vftable <c>Gamecode 1016c444</c>, loader
/// <c>100d3f12</c>, Process <c>100d4066</c>): a buff's effect that keeps re-spawning two others on the
/// host (1060, nano 75347's Wooden Skin buff).
///
/// Fields: 0 flags, 8 duration, 10/11 effects A and B, 12-15 start colour, 16-19 stop colour (A,R,G,B),
/// 20 interval, 21 spawn count (int; a NaN pattern in the data, so effectively endless).
///
/// Process: a countdown (starting at 0) runs down by the frame delta; when it is out, and at least 5 s
/// of game time have passed since any BPHFSM last fired on this host (a shared per-dynel map at
/// <c>0x102ea67c</c>), it restarts at field 20 and, while the count is not 0, creates A and B on the host
/// (<c>CreateEffect2(id, dynel, 0)</c>) with the two colours pushed in, then counts down, ready at 0.
/// Too soon after the last one, it retries in (rand() &amp; 0x7fff) / 16384 seconds. The children are not
/// kept. Slot 6 readies it; slot 8 alternates between the duration and field 20's interval.
/// </summary>
public sealed class GfxControlBuffFsm : GfxControl
{
    /// <summary>100d40df: 5 s between triggers per host.</summary>
    const float HostCooldown = 5f;

    static readonly Dictionary<object, float> LastTrigger = new Dictionary<object, float>();

    readonly IEffectSpawnFactory _factory;
    readonly int _effectA;
    readonly int _effectB;
    readonly float[] _start = new float[4];
    readonly float[] _stop = new float[4];
    float _interval;
    int _count;
    float _timer;
    bool _durationNext = true;

    public GfxControlBuffFsm(GfxTweakRecord record, EffectLocator locator, IEffectSpawnFactory factory)
        : base(record, locator)
    {
        _factory = factory;
        _effectA = record != null ? record.FieldInt(10, 0) : 0;
        _effectB = record != null ? record.FieldInt(11, 0) : 0;
        for (int c = 0; c < 4; c++)
        {
            _start[c] = record != null ? record.Field(12 + c, 0f) : 0f;
            _stop[c] = record != null ? record.Field(16 + c, 0f) : 0f;
        }
        _interval = record != null ? record.Field(20, 0f) : 0f;
        _count = record != null ? record.FieldInt(21, 0) : 0;
        base.SetDuration(record != null ? record.Field(8, -1f) : -1f);
    }

    /// <summary>Stock slot 8 (<c>100d3dbb</c>): the first call sets the duration, the next the interval, and so on.</summary>
    public override void SetDuration(float seconds)
    {
        if (_durationNext)
            base.SetDuration(seconds);
        else
            _interval = seconds;
        _durationNext = !_durationNext;
    }

    /// <summary>Stock slot 11 (<c>100d3de9</c>): the start colour (+0x48) handed to later children.</summary>
    public override void SetStartColor(float a, float r, float g, float b)
    {
        _start[0] = a; _start[1] = r; _start[2] = g; _start[3] = b;
    }

    /// <summary>Stock slot 12 (<c>100d3e08</c>): the stop colour (+0x58) handed to later children.</summary>
    public override void SetStopColor(float a, float r, float g, float b)
    {
        _stop[0] = a; _stop[1] = r; _stop[2] = g; _stop[3] = b;
    }

    /// <summary>Stock slot 13 (<c>100d3e27</c>).</summary>
    public override void SetColor(uint argb) => SetColorAsStartAndFadeOut(argb);

    /// <summary>Stock slot 6 (<c>100d3db6</c>): ready at once.</summary>
    protected override void OnTerminateGracefully() => ReadyFlag = true;

    protected override void OnProcess(float dt)
    {
        _timer -= dt;
        if (0f < _timer)
            return;

        object host = Host();
        float now = Time.time;
        if (host != null && LastTrigger.TryGetValue(host, out float last) && now < last + HostCooldown)
        {
            _timer = (UnityEngine.Random.Range(0, 0x8000) & 0x7fff) * 6.103515625e-05f;
            return;
        }

        if (host != null)
            LastTrigger[host] = now;
        _timer = _interval;

        if (_count != 0)
        {
            if (host == null)
            {
                ReadyFlag = true;
                return;
            }

            Spawn(_effectA);
            Spawn(_effectB);
        }

        if (_count > 0 && --_count <= 0)
            ReadyFlag = true;
    }

    void Spawn(int effectId)
    {
        if (_factory == null || effectId <= 0)
            return;
        EffectHandle child = _factory.SpawnChild(effectId, Locator, Color.white);
        if (child == null)
            return;
        child.SetStartColor(_start[0], _start[1], _start[2], _start[3]);
        child.SetStopColor(_stop[0], _stop[1], _stop[2], _stop[3]);
        if (IgnoreWatchdog && child.Control != null)
            child.Control.IgnoreWatchdog = true;
    }

    object Host()
    {
        if (Locator == null)
            return null;
        if (Locator.TryGetSourceDynel(out Dynel dynel) && dynel != null)
            return dynel;
        if (Locator.TryGetVisual(out VisualDynel visual) && visual != null)
            return visual;
        return null;
    }
}
