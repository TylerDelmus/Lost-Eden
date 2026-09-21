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

    /// <summary>
    /// Fallbacks for a casting hand, in descending order of preference. A missing hand must not drop
    /// the endpoint to the model root, which sits on the ground — the effect is supposed to leave the
    /// caster's hands.
    /// </summary>
    static readonly int[] SourceFallbacks =
    {
        EffectAttachIds.RightHand,
        EffectAttachIds.BoneRHand,
        EffectAttachIds.BoneLHand,
        EffectAttachIds.BoneSpine2,
        EffectAttachIds.BonePelvis,
    };

    /// <summary>
    /// Fallbacks for the impact point. Same reasoning: degrade towards the middle of the body rather
    /// than to attach 0, which is the mesh's own frame down at the feet.
    /// </summary>
    static readonly int[] TargetFallbacks =
    {
        EffectAttachIds.Head,
        EffectAttachIds.BoneNeck,
        EffectAttachIds.BoneSpine2,
        EffectAttachIds.BonePelvis,
    };

    public bool TryGetEndpoints(out Vector3 start, out Vector3 end)
    {
        if (!TryResolveWithFallbacks(
                _sourceDynel, _sourceVisual, _sourceAttachId, SourceFallbacks, out start))
        {
            end = default;
            return false;
        }

        return TryResolveWithFallbacks(
            _targetDynel, _targetVisual, _targetAttachId, TargetFallbacks, out end);
    }

    static bool TryResolveWithFallbacks(
        Dynel dynel, VisualDynel visual, int attachId, int[] fallbacks, out Vector3 position)
    {
        if (TryResolvePoint(dynel, visual, attachId, out position, out _))
            return true;

        for (int i = 0; i < fallbacks.Length; i++)
        {
            if (fallbacks[i] != attachId
                && TryResolvePoint(dynel, visual, fallbacks[i], out position, out _))
                return true;
        }

        // Last resort: the mesh frame, so the effect still plays somewhere rather than vanishing.
        return TryResolvePoint(dynel, visual, EffectAttachIds.MeshFrame, out position, out _);
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
