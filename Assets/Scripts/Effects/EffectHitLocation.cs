using UnityEngine;

/// <summary>
/// Stock <c>_GfxHitLocation_t</c>: projectile aim from caster attach → target attach.
/// Created by <c>NewHitLocation(caster, 2001 LHand, target, …)</c>; tracers bind via
/// <see cref="EffectLocator.OnHitLocation"/>.
/// </summary>
public sealed class EffectHitLocation
{
    readonly Dynel _sourceDynel;
    readonly Dynel _targetDynel;
    readonly VisualDynel _sourceVisual;
    readonly VisualDynel _targetVisual;
    readonly int _sourceAttachId;
    readonly int _targetAttachId;
    readonly float _travelSeconds;
    readonly bool _holdUntilComplete;

    float _age;
    bool _completed;

    public int Id { get; }

    /// <summary>
    /// When <see cref="_holdUntilComplete"/>, progress approaches the target but does not land
    /// until <see cref="Complete"/> (FinishNanoCasting).
    /// </summary>
    public float Progress
    {
        get
        {
            if (_completed)
                return 1f;
            float t = _travelSeconds <= 1e-6f ? 1f : Mathf.Clamp01(_age / _travelSeconds);
            return _holdUntilComplete ? Mathf.Min(t, 0.92f) : t;
        }
    }

    public bool IsCompleted => _completed;

    public EffectHitLocation(
        int id,
        Dynel source,
        Dynel target,
        int sourceAttachId,
        int targetAttachId,
        float travelSeconds,
        VisualDynel sourceVisual = null,
        VisualDynel targetVisual = null,
        bool holdUntilComplete = false)
    {
        Id = id;
        _sourceDynel = source;
        _targetDynel = target != null ? target : source;
        _sourceVisual = sourceVisual;
        _targetVisual = targetVisual != null ? targetVisual : sourceVisual;
        _sourceAttachId = sourceAttachId;
        _targetAttachId = targetAttachId;
        _travelSeconds = Mathf.Max(0.05f, travelSeconds);
        _holdUntilComplete = holdUntilComplete;
    }

    public void Tick(float dt)
    {
        if (_completed)
            return;
        _age += Mathf.Max(0f, dt);
        if (!_holdUntilComplete && _age >= _travelSeconds)
            _completed = true;
    }

    public void Complete()
    {
        _completed = true;
        _age = _travelSeconds;
    }

    public bool TryGetEndpoints(out Vector3 start, out Vector3 end)
    {
        if (!TryResolvePoint(_sourceDynel, _sourceVisual, _sourceAttachId, out start, out _))
        {
            end = default;
            return false;
        }

        if (!TryResolvePoint(_targetDynel, _targetVisual, _targetAttachId, out end, out _)
            && !TryResolvePoint(_targetDynel, _targetVisual, EffectAttachIds.Head, out end, out _)
            && !TryResolvePoint(_targetDynel, _targetVisual, 0, out end, out _))
            return false;

        return true;
    }

    public bool TryGetCurrent(out Vector3 position, out Quaternion rotation)
    {
        if (!TryGetEndpoints(out Vector3 start, out Vector3 end))
        {
            position = default;
            rotation = Quaternion.identity;
            return false;
        }

        float t = Progress;
        position = Vector3.Lerp(start, end, t);
        Vector3 delta = end - start;
        rotation = delta.sqrMagnitude > 1e-8f
            ? Quaternion.LookRotation(delta.normalized, Vector3.up)
            : Quaternion.identity;
        return true;
    }

    static bool TryResolvePoint(
        Dynel dynel,
        VisualDynel visual,
        int attachId,
        out Vector3 position,
        out Quaternion rotation)
    {
        position = default;
        rotation = Quaternion.identity;

        if (visual != null && visual.TryGetAttachMatrix(attachId, out Matrix4x4 m))
        {
            position = m.GetColumn(3);
            rotation = m.rotation;
            return true;
        }

        if (dynel is Character character && character.Visual != null
            && character.Visual.TryGetAttachMatrix(attachId, out m))
        {
            position = m.GetColumn(3);
            rotation = m.rotation;
            return true;
        }

        if (dynel != null)
        {
            position = dynel.transform.position;
            rotation = dynel.transform.rotation;
            return attachId == 0;
        }

        if (visual != null)
        {
            Transform t = visual.VisualRoot != null ? visual.VisualRoot.transform : visual.transform;
            position = t.position;
            rotation = t.rotation;
            return attachId == 0;
        }

        return false;
    }
}
