using UnityEngine;

/// <summary>
/// Re-resolves a world matrix each frame. Kill the effect if resolve fails.
/// Highlight (2011) also needs a host GameObject whose mesh materials are tinted.
/// </summary>
public sealed class EffectLocator
{
    public enum Kind
    {
        World,
        Dynel,
        Beam,
        Host,
        Visual,
        HitLocation
    }

    Kind _kind;
    Matrix4x4 _world;
    Dynel _source;
    Dynel _target;
    GameObject _host;
    VisualDynel _visual;
    EffectHitLocation _hitLocation;
    int _attachId;

    public static EffectLocator WorldPoint(Vector3 position, Quaternion rotation)
    {
        return new EffectLocator
        {
            _kind = Kind.World,
            _world = Matrix4x4.TRS(position, rotation, Vector3.one),
        };
    }

    /// <summary>Update a <see cref="WorldPoint"/> locator each frame (Spell1 hand children).</summary>
    public void SetWorldPoint(Vector3 position, Quaternion rotation)
    {
        _kind = Kind.World;
        _world = Matrix4x4.TRS(position, rotation, Vector3.one);
    }

    public void SetWorldPoint(Vector3 position)
        => SetWorldPoint(position, Quaternion.identity);

    /// <summary>World-anchored locator that can receive mesh-tint highlights (GfxTest proxy, etc.).</summary>
    public static EffectLocator OnHost(GameObject host)
    {
        return new EffectLocator
        {
            _kind = Kind.Host,
            _host = host,
        };
    }

    /// <summary>Attach to a VisualDynel attractor/bone (GfxTest CatMeshes without Character).</summary>
    public static EffectLocator OnVisual(VisualDynel visual, int attachId)
    {
        return new EffectLocator
        {
            _kind = Kind.Visual,
            _visual = visual,
            _attachId = attachId,
        };
    }

    public static EffectLocator OnDynel(Dynel dynel, int attachId)
    {
        return new EffectLocator
        {
            _kind = Kind.Dynel,
            _source = dynel,
            _attachId = attachId,
        };
    }

    public static EffectLocator Beam(Dynel source, Dynel target, int sourceAttachId)
    {
        return new EffectLocator
        {
            _kind = Kind.Beam,
            _source = source,
            _target = target,
            _attachId = sourceAttachId,
        };
    }

    /// <summary>Stock CreateEffect2(effectId, hitLocHandle) — tracer follows <see cref="EffectHitLocation"/>.</summary>
    public static EffectLocator OnHitLocation(EffectHitLocation hitLocation)
    {
        return new EffectLocator
        {
            _kind = Kind.HitLocation,
            _hitLocation = hitLocation,
        };
    }

    public bool TryGetSourceDynel(out Dynel dynel)
    {
        dynel = _source;
        return dynel != null && (_kind == Kind.Dynel || _kind == Kind.Beam);
    }

    public bool TryGetTargetDynel(out Dynel dynel)
    {
        dynel = _target;
        return dynel != null && _kind == Kind.Beam;
    }

    public bool TryGetVisual(out VisualDynel visual)
    {
        visual = _visual;
        return visual != null && _kind == Kind.Visual;
    }

    public bool TryResolve(out Matrix4x4 matrix)
    {
        switch (_kind)
        {
            case Kind.World:
                matrix = _world;
                return true;
            case Kind.Host:
                if (_host == null)
                {
                    matrix = default;
                    return false;
                }
                matrix = Matrix4x4.TRS(_host.transform.position, _host.transform.rotation, Vector3.one);
                return true;
            case Kind.Visual:
                return TryResolveVisual(_visual, _attachId, out matrix);
            case Kind.Dynel:
                return TryResolveDynel(_source, _attachId, out matrix);
            case Kind.Beam:
                if (!TryResolveDynel(_source, _attachId, out Matrix4x4 start))
                {
                    matrix = default;
                    return false;
                }

                if (!TryResolveDynel(_target, EffectAttachIds.Head, out Matrix4x4 end)
                    && !TryResolveDynel(_target, 0, out end))
                {
                    matrix = default;
                    return false;
                }

                Vector3 from = start.GetColumn(3);
                Vector3 to = end.GetColumn(3);
                Vector3 delta = to - from;
                if (delta.sqrMagnitude < 1e-8f)
                    delta = start.MultiplyVector(Vector3.forward);

                matrix = Matrix4x4.TRS(from, Quaternion.LookRotation(delta.normalized, Vector3.up), Vector3.one);
                return true;
            case Kind.HitLocation:
                if (_hitLocation == null || !_hitLocation.TryGetCurrent(out Vector3 hitPos, out Quaternion hitRot))
                {
                    matrix = default;
                    return false;
                }

                matrix = Matrix4x4.TRS(hitPos, hitRot, Vector3.one);
                return true;
            default:
                matrix = default;
                return false;
        }
    }

    /// <summary>Root used by Highlight (2011) to find Renderer mesh parts.</summary>
    public bool TryGetHighlightRoot(out GameObject root)
    {
        root = null;
        switch (_kind)
        {
            case Kind.Host:
                root = _host;
                return root != null;
            case Kind.Visual:
                return TryGetVisualRoot(_visual, out root);
            case Kind.Dynel:
                return TryGetDynelVisualRoot(_source, out root);
            case Kind.Beam:
                return TryGetDynelVisualRoot(_source, out root);
            default:
                return false;
        }
    }

    static bool TryGetVisualRoot(VisualDynel visual, out GameObject root)
    {
        root = null;
        if (visual == null)
            return false;
        root = visual.VisualRoot != null ? visual.VisualRoot : visual.gameObject;
        return root != null;
    }

    static bool TryGetDynelVisualRoot(Dynel dynel, out GameObject root)
    {
        root = null;
        if (dynel == null)
            return false;

        if (dynel is Character character && character.Visual != null && character.Visual.VisualRoot != null)
        {
            root = character.Visual.VisualRoot;
            return true;
        }

        root = dynel.gameObject;
        return true;
    }

    static bool TryResolveVisual(VisualDynel visual, int attachId, out Matrix4x4 matrix)
    {
        matrix = default;
        if (visual == null)
            return false;

        if (visual.TryGetAttachMatrix(attachId, out matrix))
            return true;

        Transform t = visual.VisualRoot != null ? visual.VisualRoot.transform : visual.transform;
        matrix = Matrix4x4.TRS(t.position, t.rotation, Vector3.one);
        return true;
    }

    static bool TryResolveDynel(Dynel dynel, int attachId, out Matrix4x4 matrix)
    {
        matrix = default;
        if (dynel == null)
            return false;

        if (dynel is Character character && character.Visual != null
            && character.Visual.TryGetAttachMatrix(attachId, out matrix))
            return true;

        matrix = Matrix4x4.TRS(dynel.transform.position, dynel.transform.rotation, Vector3.one);
        return true;
    }
}
