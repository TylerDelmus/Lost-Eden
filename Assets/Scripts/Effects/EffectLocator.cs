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

    // Stock _GfxLocator_t on an attach (init 10106be2, update 10106193): the template rotation
    // (fields 4-6) turns the attach basis (rows = R x attach) and the template offset (fields 1-3)
    // moves the point along the turned rows. UpdatePosition (101064f7) does nothing on an attach.
    bool _hasLocal;
    float[] _rows;
    Vector3 _templateOffset;

    public static EffectLocator WorldPoint(Vector3 position, Quaternion rotation)
    {
        return new EffectLocator
        {
            _kind = Kind.World,
            _world = Matrix4x4.TRS(position, rotation, Vector3.one),
        };
    }

    /// <summary>Update a <see cref="WorldPoint"/> locator each frame (Spell1's window 3 mid point).</summary>
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

    /// <summary>
    /// Copy of this locator bound to a different attach id, for kinds that have one. Stock's
    /// <c>InitDynelTemplate</c> falls back to the template's field 7 when the caller passes attach
    /// 0, so a record can place itself on a specific bone (hit 2710 asks for Bip01 Spine3_ac).
    /// Kinds without an attach — world points, hosts and hit locations — are returned unchanged.
    /// </summary>
    public EffectLocator WithAttach(int attachId)
    {
        if (attachId == _attachId)
            return this;

        switch (_kind)
        {
            case Kind.Dynel:
                return OnDynel(_source, attachId);
            case Kind.Visual:
                return OnVisual(_visual, attachId);
            case Kind.Beam:
                return Beam(_source, _target, attachId);
            default:
                return this;
        }
    }

    /// <summary>True for a locator placed by a bare position (stock locator mode 1).</summary>
    public bool IsWorldPoint => _kind == Kind.World;

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

    public bool TryGetHitLocation(out EffectHitLocation hitLocation)
    {
        hitLocation = _hitLocation;
        return hitLocation != null && _kind == Kind.HitLocation;
    }

    public bool TryGetVisual(out VisualDynel visual)
    {
        visual = _visual;
        return visual != null && _kind == Kind.Visual;
    }

    /// <summary>
    /// A copy carrying <paramref name="record"/>'s locator template (fields 0-6), for attach locators whose
    /// template rotates or offsets. Other kinds, and templates with neither, return this.
    /// <paramref name="offsetZ"/> stands in for field 3 when stock takes it from a per-body table
    /// (<see cref="EffectBodyTable"/>).
    /// </summary>
    public EffectLocator WithTemplate(GfxTweakRecord record, float? offsetZ = null)
    {
        if (record == null || (_kind != Kind.Dynel && _kind != Kind.Visual))
            return this;

        int flags = record.FieldInt(0, 0);
        var offset = new Vector3(record.Field(1, 0f), record.Field(2, 0f), offsetZ ?? record.Field(3, 0f));
        bool rotates = record.Field(4, 0f) != 0f || record.Field(5, 0f) != 0f || record.Field(6, 0f) != 0f;
        if (!rotates && offset == Vector3.zero)
        {
            // Every control builds its own locator from its own template: none of a parent's carries over.
            if (!_hasLocal)
                return this;
            var plain = (EffectLocator)MemberwiseClone();
            plain._hasLocal = false;
            plain._rows = null;
            return plain;
        }

        var copy = (EffectLocator)MemberwiseClone();
        copy._hasLocal = true;
        copy._templateOffset = offset;
        copy._rows = new float[] { 1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f };
        Tracer1Sim.ApplyLocatorRotation(record.Fields, flags, copy._rows);
        return copy;
    }

    void ApplyLocal(ref Matrix4x4 matrix)
    {
        if (!_hasLocal)
            return;

        Vector3 p = _templateOffset;
        var r0 = new Vector3(_rows[0], _rows[1], _rows[2]);
        var r1 = new Vector3(_rows[3], _rows[4], _rows[5]);
        var r2 = new Vector3(_rows[6], _rows[7], _rows[8]);
        var local = new Matrix4x4(
            new Vector4(r0.x, r0.y, r0.z, 0f),
            new Vector4(r1.x, r1.y, r1.z, 0f),
            new Vector4(r2.x, r2.y, r2.z, 0f),
            (Vector4)(r0 * p.x + r1 * p.y + r2 * p.z) + new Vector4(0f, 0f, 0f, 1f));
        matrix *= local;
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
                if (!TryResolveVisual(_visual, _attachId, out matrix))
                    return false;
                ApplyLocal(ref matrix);
                return true;
            case Kind.Dynel:
                if (!TryResolveDynel(_source, _attachId, out matrix))
                    return false;
                ApplyLocal(ref matrix);
                return true;
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

        // 10106744: an attach that can't be found leaves the locator on the mesh frame (attach 0).
        if (visual.TryGetAttachMatrix(attachId, out matrix) || visual.TryGetAttachMatrix(0, out matrix))
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
            && (character.Visual.TryGetAttachMatrix(attachId, out matrix) || character.Visual.TryGetAttachMatrix(0, out matrix)))
            return true;

        matrix = Matrix4x4.TRS(dynel.transform.position, dynel.transform.rotation, Vector3.one);
        return true;
    }
}
