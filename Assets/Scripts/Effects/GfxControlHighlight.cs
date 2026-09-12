using UnityEngine;

/// <summary>
/// typeCode 2011 — <c>_GfxControlHighlight_t</c>: tint the target's existing mesh
/// (emissive / transparency / optional specular). No GfxVisual is spawned.
/// </summary>
public sealed class GfxControlHighlight : GfxControl
{
    readonly int _variant;
    readonly float _pulseRate;
    readonly Color _start;
    readonly Color _stop;
    readonly EffectMeshTint _tint = new EffectMeshTint();
    bool _fadingOut;

    public GfxControlHighlight(GfxTweakRecord record, EffectLocator locator)
        : base(record, locator)
    {
        // idx0 flags ignored for v1 (no independent position / ground-snap).
        _variant = record != null ? Mathf.Clamp(record.FieldInt(1, 0), 0, 3) : 0;
        float duration = record != null ? record.Field(2, 1f) : 1f;
        if (float.IsNaN(duration) || float.IsInfinity(duration) || duration < 0f)
            duration = 1f;

        _start = EffectColors.ReadArgbBlock(record, 3, new Color(0f, 0f, 0f, 0f));
        _stop = EffectColors.ReadArgbBlock(record, 7, Color.white);

        if (_variant == 2)
        {
            // Stock: pulseRate = duration; duration forced infinite until TerminateGracefully.
            _pulseRate = Mathf.Max(0.05f, duration);
            SetDuration(InfiniteDuration);
        }
        else
        {
            _pulseRate = Mathf.Max(0.05f, duration);
            SetDuration(Mathf.Max(0.05f, duration));
        }
    }

    protected override void OnProcess(float dt)
    {
        if (Locator == null || !Locator.TryGetHighlightRoot(out GameObject root) || root == null)
        {
            _tint.Clear();
            ReadyFlag = true;
            return;
        }

        _tint.Bind(root);

        float t = EvaluatePhase();
        Color c = Color.Lerp(_start, _stop, Mathf.Clamp01(t));
        _tint.Apply(new Color(c.r, c.g, c.b, 1f), c.a, writeSpecular: _variant == 3);
    }

    float EvaluatePhase()
    {
        float duration = Duration;
        float age = Age;

        switch (_variant)
        {
            case 0:
            {
                float d = duration > 0f ? duration : _pulseRate;
                return d > 0f ? Mathf.Clamp01(age / d) : 1f;
            }
            case 1:
            case 3:
            {
                float d = duration > 0f ? duration : _pulseRate;
                float u = d > 0f ? Mathf.Clamp01(age / d) : 1f;
                float x = 2f * u - 1f;
                return 1f - x * x;
            }
            case 2:
                if (!_fadingOut)
                    return Mathf.Min(age / _pulseRate, 1f);
                // Ramp down after TerminateGracefully scheduled a real end time.
                if (duration < 0f)
                    return 1f;
                return Mathf.Clamp01((duration - age) / _pulseRate);
            default:
                return 0f;
        }
    }

    protected override void OnTerminateGracefully()
    {
        if (_variant == 2)
        {
            // Schedule fade-out window of one pulseRate from now.
            SetDuration(Age + _pulseRate);
            _fadingOut = true;
            return;
        }

        ReadyFlag = true;
        _tint.Clear();
    }

    protected override void OnReleased(bool immediate)
    {
        _tint.Clear();
    }
}
