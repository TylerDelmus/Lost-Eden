using System.Collections.Generic;

/// <summary>
/// Cancelable live-effect handle returned by <see cref="EffectHandler.CreateEffect2"/>.
/// </summary>
public sealed class EffectHandle
{
    GfxControl _control;
    bool _dead;

    public bool IsAlive => !_dead && _control != null && _control.IsAlive;
    public GfxControl Control => _control;

    internal void Bind(GfxControl control)
    {
        _control = control;
        _dead = control == null;
    }

    public void SetDuration(float seconds)
    {
        _control?.SetDuration(seconds);
    }

    public void TerminateGracefully()
    {
        if (_dead)
            return;
        _control?.TerminateGracefully();
    }

    /// <summary>Stock <c>_EffectHandler_t::NextState</c> (Gamecode 100ce288): the control's slot 10.</summary>
    public void NextState()
    {
        if (_dead)
            return;
        _control?.NextState();
    }

    public void UpdatePosition(UnityEngine.Vector3 position)
    {
        if (!_dead)
            _control?.UpdatePosition(position);
    }

    public void SetStartColor(float a, float r, float g, float b)
    {
        if (!_dead)
            _control?.SetStartColor(a, r, g, b);
    }

    public void SetStopColor(float a, float r, float g, float b)
    {
        if (!_dead)
            _control?.SetStopColor(a, r, g, b);
    }

    /// <summary>Stock <c>_EffectHandler_t</c> <c>100ce34e</c>: slot 13 with a packed ARGB colour.</summary>
    public void SetColor(uint argb)
    {
        if (!_dead)
            _control?.SetColor(argb);
    }

    public void Destroy()
    {
        if (_dead && _control == null)
            return;
        _dead = true;
        _control?.Release(true);
        _control = null;
    }

    /// <summary>Hard kill alias.</summary>
    public void Kill() => Destroy();

    /// <summary>If the root control finished, release it (cascades Meta/Sequencer/Delay children).</summary>
    internal bool Sweep()
    {
        if (_dead)
            return true;

        if (_control == null || !_control.IsAlive)
        {
            Destroy();
            return true;
        }

        return false;
    }

    internal void CollectLive(List<GfxControl> dest)
    {
        if (Sweep())
            return;
        dest.Add(_control);
    }
}
