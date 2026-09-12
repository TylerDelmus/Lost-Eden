using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stock-shaped effect factory: effectId → gfxtweak typeCode → concrete <see cref="GfxControl"/>.
/// Implements <see cref="IEffectSpawnFactory"/> for Meta / Sequencer / Delay children.
/// </summary>
public sealed class EffectHandler : IEffectSpawnFactory
{
    const int MaxSpawnDepth = 6;
    const float DefaultLifetime = 1.5f;
    /// <summary>How long the cast FX plays on a character that just received a buff.</summary>
    const float BuffCastEffectSeconds = 3f;
    /// <summary>Stock NewHitLocation target attach (Bip01 Head_ac).</summary>
    const int TracerTargetAttachId = EffectAttachIds.BoneHead;

    struct PendingTrace
    {
        public EffectHitLocation HitLoc;
        public EffectHandle Tracer;
    }

    struct PendingCast
    {
        public EffectHandle Handle;
        public Character Target;
    }

    readonly GfxTweakCatalog _catalog;
    readonly AoImageTextureCache _textures;
    readonly EffectTextureNames _textureNames;
    readonly EffectBillboardBatch _batch = new EffectBillboardBatch();
    readonly EffectAtlasFrames _atlasFrames = new EffectAtlasFrames();
    readonly EffectLightPool _lights = new EffectLightPool();
    readonly List<EffectHandle> _handles = new List<EffectHandle>(64);
    readonly List<GfxControl> _tick = new List<GfxControl>(64);
    readonly List<EffectBillboardBatch.Quad> _quads = new List<EffectBillboardBatch.Quad>(128);
    readonly Dictionary<int, Texture2D> _materialTextures = new Dictionary<int, Texture2D>();
    readonly HashSet<EffectHandle> _handleSet = new HashSet<EffectHandle>();
    readonly List<EffectHitLocation> _hitLocations = new List<EffectHitLocation>(16);
    readonly Dictionary<int, PendingTrace> _pendingTraces = new Dictionary<int, PendingTrace>();
    readonly Dictionary<int, PendingCast> _pendingCasts = new Dictionary<int, PendingCast>();

    int _spawnDepth;
    int _nextHitLocId = 1;

    public bool ShowOthersEffects { get; set; } = true;

    public EffectHandler(
        GfxTweakCatalog catalog,
        AoImageTextureCache textures,
        EffectTextureNames textureNames)
    {
        _catalog = catalog ?? new GfxTweakCatalog(null);
        _textures = textures;
        _textureNames = textureNames;
    }

    /// <summary>Parent pooled FX lights under a live scene object (avoids DDOL culling quirks).</summary>
    public void SetLightParent(Transform parent) => _lights.SetParent(parent);

    public EffectHandle CreateEffect2(int effectId, EffectLocator locator, Color? tint = null)
    {
        if (effectId == EffectTypeTags.RejectedEffectId || effectId <= 0 || locator == null)
            return null;

        return SpawnInternal(effectId, locator, tint ?? Color.white, register: true, caster: null, target: null, attachOverride: 0, casterVisual: null, targetVisual: null);
    }

    /// <summary>Stock CreateEffect2(effectId, hitLocHandle) — tracer path.</summary>
    public EffectHandle CreateEffect2(int effectId, EffectHitLocation hitLocation, Color? tint = null)
    {
        if (effectId == EffectTypeTags.RejectedEffectId || effectId <= 0 || hitLocation == null)
            return null;

        return CreateEffect2(effectId, EffectLocator.OnHitLocation(hitLocation), tint);
    }

    /// <summary>Stock CreateEffect2(effectId, caster, target, attachOverride) — Spell1 cast path.</summary>
    public EffectHandle CreateEffect2(
        int effectId,
        Dynel caster,
        Dynel target,
        Color? tint = null,
        int attachOverride = 0)
    {
        if (effectId == EffectTypeTags.RejectedEffectId || effectId <= 0 || caster == null)
            return null;

        Dynel resolvedTarget = target != null ? target : caster;
        var locator = EffectLocator.OnDynel(caster, 0);
        return SpawnInternal(
            effectId,
            locator,
            tint ?? Color.white,
            register: true,
            caster,
            resolvedTarget,
            attachOverride,
            casterVisual: null,
            targetVisual: null);
    }

    /// <summary>GfxTest / visual-only cast span (no Character dynel).</summary>
    public EffectHandle CreateEffect2(
        int effectId,
        VisualDynel casterVisual,
        VisualDynel targetVisual,
        Color? tint = null,
        int attachOverride = 0)
    {
        if (effectId == EffectTypeTags.RejectedEffectId || effectId <= 0 || casterVisual == null)
            return null;

        VisualDynel resolvedTarget = targetVisual != null ? targetVisual : casterVisual;
        var locator = EffectLocator.OnVisual(casterVisual, 0);
        return SpawnInternal(
            effectId,
            locator,
            tint ?? Color.white,
            register: true,
            caster: null,
            target: null,
            attachOverride,
            casterVisual,
            resolvedTarget);
    }

    /// <summary>
    /// Stock <c>NewHitLocation(source, LHand, target, Head, true)</c>.
    /// When <paramref name="holdUntilComplete"/>, lands only on <see cref="EffectHitLocation.Complete"/>
    /// (FinishNanoCasting) — hit FX must not apply mid-cast.
    /// </summary>
    public EffectHitLocation NewHitLocation(
        Dynel source,
        Dynel target,
        float travelSeconds,
        int sourceAttachId = EffectAttachIds.LeftHand,
        int targetAttachId = TracerTargetAttachId,
        VisualDynel sourceVisual = null,
        VisualDynel targetVisual = null,
        bool holdUntilComplete = false)
    {
        int id = _nextHitLocId++;
        var hitLoc = new EffectHitLocation(
            id,
            source,
            target != null ? target : source,
            sourceAttachId,
            targetAttachId,
            travelSeconds,
            sourceVisual,
            targetVisual,
            holdUntilComplete);
        _hitLocations.Add(hitLoc);
        return hitLoc;
    }

    public EffectHitLocation NewHitLocation(
        VisualDynel sourceVisual,
        VisualDynel targetVisual,
        float travelSeconds,
        int sourceAttachId = EffectAttachIds.LeftHand,
        int targetAttachId = TracerTargetAttachId,
        bool holdUntilComplete = false)
    {
        return NewHitLocation(
            source: null,
            target: null,
            travelSeconds,
            sourceAttachId,
            targetAttachId,
            sourceVisual,
            targetVisual != null ? targetVisual : sourceVisual,
            holdUntilComplete);
    }

    EffectHandle IEffectSpawnFactory.SpawnChild(int effectId, EffectLocator locator, Color tint)
        => SpawnInternal(effectId, locator, tint, register: true, null, null, 0, null, null);

    bool IEffectSpawnFactory.IsRunning(EffectHandle handle)
        => handle != null && handle.IsAlive;

    void IEffectSpawnFactory.DeleteEffect(EffectHandle handle)
    {
        if (handle == null)
            return;
        handle.Destroy();
        Unregister(handle);
    }

    void IEffectSpawnFactory.TerminateEffectGracefully(EffectHandle handle)
        => handle?.TerminateGracefully();

    EffectHandle SpawnInternal(
        int effectId,
        EffectLocator locator,
        Color tint,
        bool register,
        Dynel caster,
        Dynel target,
        int attachOverride,
        VisualDynel casterVisual,
        VisualDynel targetVisual)
    {
        if (effectId == EffectTypeTags.RejectedEffectId || effectId <= 0 || locator == null)
            return null;
        if (_spawnDepth > MaxSpawnDepth)
            return null;

        _spawnDepth++;
        GfxControl control;
        try
        {
            control = CreateControl(effectId, locator, tint, caster, target, attachOverride, casterVisual, targetVisual);
        }
        finally
        {
            _spawnDepth--;
        }

        if (control == null)
            return null;

        var handle = new EffectHandle();
        handle.Bind(control);
        // Unsupported controls finish immediately — don't register a dead handle.
        if (!handle.IsAlive)
            return null;

        if (register)
            Register(handle);
        return handle;
    }

    void Register(EffectHandle handle)
    {
        if (handle == null || !_handleSet.Add(handle))
            return;
        _handles.Add(handle);
    }

    void Unregister(EffectHandle handle)
    {
        if (handle == null || !_handleSet.Remove(handle))
            return;
        _handles.Remove(handle);
    }

    public EffectHandle PlayNanoCast(Character caster, Character target, int nanoId, bool isLocal, NanoSpell nano)
    {
        if (caster == null)
            return null;
        if (!isLocal && !ShowOthersEffects)
            return null;

        EffectHandle castHandle = null;
        if (nano != null && NanoEffectResolver.TryResolveCast(nano, out NanoEffectResolver.SpellFx castFx) && castFx.EffectId != 0)
        {
            castHandle = PlayNanoEffect(
                castFx.EffectId,
                host: caster,
                beamTarget: target,
                tint: castFx.HasColor ? castFx.Color : Color.white,
                nanoId: nanoId,
                phase: "cast",
                isLocal: isLocal,
                spellTarget: target);
        }
        else
        {
            Debug.LogWarning(
                $"[Effects] nano={nanoId} missing CastEffectType — cast FX skipped.");
        }

        // Recorded even without a cast handle: the hit site needs the target either way.
        RememberPendingCast(caster.Identity.Instance, castHandle, target);

        // Stock anim 0x42: NewHitLocation(LHand→Head) + CreateEffect2(tracereffecttype, hitLoc).
        // Hold until FinishNanoCasting — do not land / play HitEffectType mid-cast.
        if (target != null && nano != null
            && NanoEffectResolver.TryResolveTrace(nano, out NanoEffectResolver.SpellFx traceFx)
            && traceFx.EffectId != 0)
        {
            float travel = Mathf.Max(EstimateTravelSeconds(caster, target, traceFx.EffectId), 8f);
            StartTracer(
                caster.Identity.Instance,
                caster,
                target,
                null,
                null,
                traceFx.EffectId,
                travel,
                nanoId,
                isLocal,
                holdUntilComplete: true);
        }

        return castHandle;
    }

    /// <summary>
    /// Stock impact site: kill tracer, then <c>impacteffecttype</c> (414) then <c>hiteffecttype</c> (361)
    /// on the target with attach 0 (CAT mesh frame / mid-body — not dynel feet).
    /// </summary>
    public EffectHandle PlayNanoHit(Character caster, Character target, int nanoId, bool isLocal, NanoSpell nano)
    {
        if (!isLocal && !ShowOthersEffects)
            return null;

        int casterKey = caster != null ? caster.Identity.Instance : 0;
        // Read before EndPendingCast clears the entry.
        if (target == null)
            TryGetPendingCastTarget(casterKey, out target);

        // Cast Spell1 is duration 60 / infinite — end it so hand children don't keep stacking over hit.
        EndPendingCast(casterKey);
        CompletePendingTrace(casterKey);

        Character host = target != null ? target : caster;
        if (host == null)
            return null;

        EffectHandle last = null;

        if (nano != null && NanoEffectResolver.TryResolveImpact(nano, out NanoEffectResolver.SpellFx impactFx) && impactFx.EffectId != 0)
        {
            last = PlayNanoEffect(
                impactFx.EffectId,
                host: host,
                beamTarget: null,
                tint: impactFx.HasColor ? impactFx.Color : Color.white,
                nanoId: nanoId,
                phase: "impact",
                isLocal: isLocal,
                spellTarget: null);
        }

        if (nano == null || !NanoEffectResolver.TryResolveHit(nano, out NanoEffectResolver.SpellFx hitFx) || hitFx.EffectId == 0)
        {
            if (last == null)
                Debug.LogWarning(
                    $"[Effects] nano={nanoId} missing HitEffectType — hit FX skipped.");
            return last;
        }

        return PlayNanoEffect(
            hitFx.EffectId,
            host: host,
            beamTarget: null,
            tint: hitFx.HasColor ? hitFx.Color : Color.white,
            nanoId: nanoId,
            phase: "hit",
            isLocal: isLocal,
            spellTarget: null) ?? last;
    }

    /// <summary>
    /// Buff landed on a character: stock <c>casteffecttype</c> on the recipient itself.
    /// Unlike the caster's cast FX there is no finish message to wind this down, so it runs
    /// on a bounded duration instead of the Spell1 safety duration.
    /// </summary>
    public EffectHandle PlayNanoBuff(Character target, int nanoId, bool isLocal, NanoSpell nano)
    {
        if (target == null)
            return null;
        if (!isLocal && !ShowOthersEffects)
            return null;

        if (nano == null
            || !NanoEffectResolver.TryResolveCast(nano, out NanoEffectResolver.SpellFx castFx)
            || castFx.EffectId == 0)
        {
            Debug.LogWarning($"[Effects] buff nano={nanoId} missing CastEffectType — buff FX skipped.");
            return null;
        }

        EffectHandle handle = CreateEffect2(
            castFx.EffectId,
            target,
            target,
            castFx.HasColor ? castFx.Color : Color.white,
            attachOverride: 0);
        if (handle == null)
        {
            Debug.LogWarning(
                $"[Effects] CreateEffect2 failed buff nano={nanoId} effectId={castFx.EffectId} host={target.Identity}");
            return null;
        }

        handle.SetDuration(BuffCastEffectSeconds);
        Debug.Log(
            $"[Effects] nano buff nano={nanoId} effectId={castFx.EffectId} host={target.Identity} local={isLocal}");
        return handle;
    }

    /// <summary>GfxTest: cast FX only during the cast. Tracer/hit start after cast finishes.</summary>
    public EffectHandle PlayNanoCastVisual(
        VisualDynel caster,
        VisualDynel target,
        int nanoId,
        NanoSpell nano)
    {
        if (caster == null)
            return null;

        if (nano == null || !NanoEffectResolver.TryResolveCast(nano, out NanoEffectResolver.SpellFx castFx) || castFx.EffectId == 0)
            return null;

        EffectHandle castHandle = CreateEffect2(castFx.EffectId, caster, target != null ? target : caster, Color.white, 0);
        castHandle?.SetDuration(60f);
        if (castHandle != null)
            Debug.Log($"[Effects] nano cast nano={nanoId} effectId={castFx.EffectId} visual spell1");
        return castHandle;
    }

    /// <summary>GfxTest: remember cast handle so finish can wind it down before tracer/hit.</summary>
    public void RememberPendingCastVisual(int casterKey, EffectHandle castHandle)
        => RememberPendingCast(casterKey, castHandle, null);

    /// <summary>GfxTest: start tracer after cast finishes. Returns travel seconds (0 if none).</summary>
    public float BeginNanoTracerVisual(
        VisualDynel caster,
        VisualDynel target,
        int nanoId,
        NanoSpell nano,
        int casterKey,
        float travelSeconds,
        out EffectHandle tracerHandle)
    {
        tracerHandle = null;
        if (caster == null || target == null || nano == null)
            return 0f;
        if (!NanoEffectResolver.TryResolveTrace(nano, out NanoEffectResolver.SpellFx traceFx) || traceFx.EffectId == 0)
            return 0f;

        float travel = Mathf.Max(0.15f, travelSeconds);
        tracerHandle = StartTracer(
            casterKey,
            null,
            null,
            caster,
            target,
            traceFx.EffectId,
            travel,
            nanoId,
            isLocal: true,
            holdUntilComplete: false);
        return tracerHandle != null ? travel : 0f;
    }

    /// <summary>GfxTest: land tracer (if any) then impact+hit on target attach 0.</summary>
    public EffectHandle PlayNanoHitVisual(
        VisualDynel target,
        int nanoId,
        NanoSpell nano,
        int casterKey,
        out EffectHandle impactHandle)
    {
        impactHandle = null;
        EndPendingCast(casterKey);
        CompletePendingTrace(casterKey);
        if (target == null)
            return null;

        if (nano != null && NanoEffectResolver.TryResolveImpact(nano, out NanoEffectResolver.SpellFx impactFx) && impactFx.EffectId != 0)
        {
            impactHandle = CreateEffect2(impactFx.EffectId, EffectLocator.OnVisual(target, 0), Color.white);
            if (impactHandle != null)
                Debug.Log($"[Effects] nano impact nano={nanoId} effectId={impactFx.EffectId} visual");
        }

        if (nano == null || !NanoEffectResolver.TryResolveHit(nano, out NanoEffectResolver.SpellFx hitFx) || hitFx.EffectId == 0)
            return null;

        EffectHandle hit = CreateEffect2(hitFx.EffectId, EffectLocator.OnVisual(target, 0), Color.white);
        if (hit != null)
            Debug.Log($"[Effects] nano hit nano={nanoId} effectId={hitFx.EffectId} visual");
        return hit;
    }

    EffectHandle StartTracer(
        int casterKey,
        Dynel source,
        Dynel target,
        VisualDynel sourceVisual,
        VisualDynel targetVisual,
        int traceEffectId,
        float travelSeconds,
        int nanoId,
        bool isLocal,
        bool holdUntilComplete)
    {
        CompletePendingTrace(casterKey);

        EffectHitLocation hitLoc = source != null
            ? NewHitLocation(source, target, travelSeconds, EffectAttachIds.LeftHand, TracerTargetAttachId, sourceVisual, targetVisual, holdUntilComplete)
            : NewHitLocation(sourceVisual, targetVisual, travelSeconds, holdUntilComplete: holdUntilComplete);

        EffectHandle tracer = CreateEffect2(traceEffectId, hitLoc, Color.white);
        if (tracer == null)
        {
            Debug.LogWarning(
                $"[Effects] CreateEffect2 tracer failed nano={nanoId} effectId={traceEffectId}");
            return null;
        }

        tracer.SetDuration(holdUntilComplete ? 60f : Mathf.Max(travelSeconds + 0.5f, 2f));
        _pendingTraces[casterKey] = new PendingTrace { HitLoc = hitLoc, Tracer = tracer };
        Debug.Log(
            $"[Effects] nano tracer nano={nanoId} effectId={traceEffectId} travel={travelSeconds:0.###}s hold={holdUntilComplete} local={isLocal}");
        return tracer;
    }

    void CompletePendingTrace(int casterKey)
    {
        if (casterKey == 0 || !_pendingTraces.TryGetValue(casterKey, out PendingTrace pending))
            return;

        _pendingTraces.Remove(casterKey);
        pending.HitLoc?.Complete();
        if (pending.Tracer != null && pending.Tracer.IsAlive)
            pending.Tracer.TerminateGracefully();
    }

    void RememberPendingCast(int casterKey, EffectHandle castHandle, Character target)
    {
        if (casterKey == 0)
            return;

        EndPendingCast(casterKey);
        if (castHandle == null && target == null)
            return;

        _pendingCasts[casterKey] = new PendingCast { Handle = castHandle, Target = target };
    }

    /// <summary>
    /// The spell target recorded when this caster started casting. FinishNanoCasting does not
    /// reliably carry one, so the hit site falls back to what CastNanoSpell told us.
    /// </summary>
    bool TryGetPendingCastTarget(int casterKey, out Character target)
    {
        target = null;
        if (casterKey == 0 || !_pendingCasts.TryGetValue(casterKey, out PendingCast pending))
            return false;

        if (pending.Target == null)
            return false;

        target = pending.Target;
        return true;
    }

    void EndPendingCast(int casterKey)
    {
        if (casterKey == 0 || !_pendingCasts.TryGetValue(casterKey, out PendingCast pending))
            return;

        _pendingCasts.Remove(casterKey);
        if (pending.Handle != null && pending.Handle.IsAlive)
            pending.Handle.TerminateGracefully();
    }

    /// <summary>Wind down cast Spell1 (and its hand children) without playing hit.</summary>
    public void EndPendingCastVisual(int casterKey) => EndPendingCast(casterKey);

    /// <summary>Interrupt / Clear: wind down cast + tracer without playing hit.</summary>
    public void CancelPendingTrace(int casterKey)
    {
        EndPendingCast(casterKey);
        CompletePendingTrace(casterKey);
    }

    float EstimateTravelSeconds(Dynel source, Dynel target, int traceEffectId)
    {
        if (_catalog.TryGet(traceEffectId, out GfxTweakRecord record))
        {
            int field30 = record.FieldInt(30, 0);
            if (field30 > 0)
                return Mathf.Clamp(field30 / 1000f, 0.05f, 3f);
        }

        if (source == null || target == null)
            return 0.35f;

        float dist = Vector3.Distance(source.transform.position, target.transform.position);
        return Mathf.Clamp(dist / 15f, 0.15f, 2.5f);
    }

    EffectHandle PlayNanoEffect(
        int effectId,
        Character host,
        Character beamTarget,
        Color tint,
        int nanoId,
        string phase,
        bool isLocal,
        Character spellTarget)
    {
        int rightHand = EffectAttachIds.RightHand;
        bool beam = beamTarget != null
            && _catalog.TryGet(effectId, out GfxTweakRecord record)
            && EffectTypeTags.IsBeam(record.TypeCode);

        if (beam)
        {
            EffectHandle beamHandle = CreateEffect2(effectId, EffectLocator.Beam(host, beamTarget, rightHand), tint);
            if (beamHandle == null)
            {
                Debug.LogWarning(
                    $"[Effects] CreateEffect2 failed nano={nanoId} phase={phase} effectId={effectId} host={host.Identity}");
                return null;
            }

            Debug.Log(
                $"[Effects] nano {phase} nano={nanoId} effectId={effectId} host={host.Identity} local={isLocal} beam");
            return beamHandle;
        }

        // Cast: stock CreateEffect2(caster, target) — Spell1 spans hands internally.
        if (phase == "cast")
        {
            Character target = spellTarget != null ? spellTarget : host;
            EffectHandle castHandle = CreateEffect2(effectId, host, target, tint, attachOverride: 0);
            if (castHandle == null)
            {
                Debug.LogWarning(
                    $"[Effects] CreateEffect2 failed nano={nanoId} phase={phase} effectId={effectId} host={host.Identity}");
                return null;
            }

            castHandle.SetDuration(60f);
            Debug.Log(
                $"[Effects] nano {phase} nano={nanoId} effectId={effectId} host={host.Identity} local={isLocal} spell1");
            return castHandle;
        }

        // Impact / hit: stock CreateEffect2(effectId, target, attach=0) → mesh RRefFrame (mid-body).
        EffectHandle handle = CreateEffect2(effectId, EffectLocator.OnDynel(host, 0), tint);
        if (handle == null)
        {
            Debug.LogWarning(
                $"[Effects] CreateEffect2 failed nano={nanoId} phase={phase} effectId={effectId} host={host.Identity}");
            return null;
        }

        Debug.Log(
            $"[Effects] nano {phase} nano={nanoId} effectId={effectId} host={host.Identity} local={isLocal} attach=meshFrame");
        return handle;
    }

    public void Tick(float dt, Camera camera)
    {
        for (int i = _hitLocations.Count - 1; i >= 0; i--)
        {
            EffectHitLocation hitLoc = _hitLocations[i];
            if (hitLoc == null)
            {
                _hitLocations.RemoveAt(i);
                continue;
            }

            hitLoc.Tick(dt);
        }

        _tick.Clear();
        for (int i = _handles.Count - 1; i >= 0; i--)
        {
            EffectHandle handle = _handles[i];
            if (handle == null)
            {
                _handles.RemoveAt(i);
                continue;
            }

            handle.CollectLive(_tick);
            if (!handle.IsAlive)
            {
                _handleSet.Remove(handle);
                _handles.RemoveAt(i);
            }
        }

        _batch.Clear();
        _quads.Clear();
        for (int i = 0; i < _tick.Count; i++)
        {
            GfxControl control = _tick[i];
            if (!control.Process(dt))
                continue;
            control.CollectBillboards(_quads, camera);
        }

        for (int i = 0; i < _quads.Count; i++)
            _batch.Add(_quads[i]);

        _batch.Submit(camera);
    }

    GfxControl CreateControl(
        int effectId,
        EffectLocator locator,
        Color tint,
        Dynel caster,
        Dynel target,
        int attachOverride,
        VisualDynel casterVisual,
        VisualDynel targetVisual)
    {
        if (!_catalog.TryGet(effectId, out GfxTweakRecord record))
            return CreateBillboard(CreateFallbackRecord(effectId), locator, tint);

        int typeCode = record.TypeCode;
        switch (typeCode)
        {
            case EffectTypeTags.Meta:
                return new GfxControlMeta(record, locator, this, tint);
            case EffectTypeTags.Sequencer:
                return new GfxControlSequencer(record, locator, this, tint);
            case EffectTypeTags.Delay:
                return new GfxControlDelay(record, locator, this, tint);
            case EffectTypeTags.Stars:
                return CreateStars(record, locator);
            case EffectTypeTags.BParticle2:
                return CreateBParticle2(record, locator, tint);
            case EffectTypeTags.TParticle:
                return CreateTParticle(record, locator, tint);
            case EffectTypeTags.Highlight:
                return new GfxControlHighlight(record, locator);
            case EffectTypeTags.Spell1:
                return CreateSpell1(record, locator, tint, caster, target, attachOverride, casterVisual, targetVisual);
            case EffectTypeTags.Shield:
            case EffectTypeTags.Shield2:
                // Mesh shields not wired yet — avoid white-square billboard misuse of float fields.
                return new GfxControlUnsupported(record, locator);
            default:
                if (EffectTypeTags.IsSpriteFamily(typeCode))
                    return CreateBillboard(record, locator, tint);
                if (EffectTypeTags.IsBeam(typeCode))
                    return CreateBillboard(record, locator, tint);
                Debug.LogWarning(
                    $"[Effects] unsupported typeCode={typeCode} (0x{typeCode:X}) id={record.Id}; skipping draw.");
                return new GfxControlUnsupported(record, locator);
        }
    }

    GfxControl CreateSpell1(
        GfxTweakRecord record,
        EffectLocator locator,
        Color tint,
        Dynel caster,
        Dynel target,
        int attachOverride,
        VisualDynel casterVisual,
        VisualDynel targetVisual)
    {
        if (caster == null && locator != null)
            locator.TryGetSourceDynel(out caster);
        if (target == null)
            target = caster;
        if (casterVisual == null && locator != null)
            locator.TryGetVisual(out casterVisual);
        if (targetVisual == null)
            targetVisual = casterVisual;

        int materialIndex = ResolveMaterialIndex(record, defaultIndex: 8, preferredField: 9);
        EffectMaterialTable.Slot slot = EffectMaterialTable.Get(materialIndex);
        Texture2D texture = slot.IsUntextured ? WhiteTexture : GetMaterialTexture(materialIndex, slot);
        return new GfxControlSpell1(
            record,
            locator,
            caster,
            target,
            this,
            texture,
            tint,
            attachOverride,
            casterVisual,
            targetVisual);
    }

    GfxControl CreateStars(GfxTweakRecord record, EffectLocator locator)
    {
        int materialIndex = ResolveMaterialIndex(record, defaultIndex: 9, preferredField: 9);
        EffectMaterialTable.Slot slot = EffectMaterialTable.Get(materialIndex);
        Texture2D texture = slot.IsUntextured ? WhiteTexture : GetMaterialTexture(materialIndex, slot);
        return new GfxControlStars(
            record, locator, texture, _atlasFrames,
            slot.Cols, slot.Rows, slot.FirstFrame, slot.LastFrame, _lights);
    }

    GfxControl CreateBParticle2(GfxTweakRecord record, EffectLocator locator, Color tint)
    {
        int materialIndex = ResolveMaterialIndex(record, defaultIndex: 10, preferredField: 10);
        EffectMaterialTable.Slot slot = EffectMaterialTable.Get(materialIndex);
        Texture2D texture = slot.IsUntextured ? WhiteTexture : GetMaterialTexture(materialIndex, slot);
        return new GfxControlBParticle2(
            record, locator, texture, _atlasFrames,
            slot.Cols, slot.Rows, slot.FirstFrame, slot.LastFrame, tint);
    }

    GfxControl CreateTParticle(GfxTweakRecord record, EffectLocator locator, Color tint)
    {
        int materialIndex = ResolveMaterialIndex(record, defaultIndex: 10, preferredField: 10);
        EffectMaterialTable.Slot slot = EffectMaterialTable.Get(materialIndex);
        Texture2D texture = slot.IsUntextured ? WhiteTexture : GetMaterialTexture(materialIndex, slot);
        return new GfxControlTParticle(
            record, locator, texture, _atlasFrames,
            slot.Cols, slot.Rows, slot.FirstFrame, slot.LastFrame, tint);
    }

    GfxControl CreateBillboard(GfxTweakRecord record, EffectLocator locator, Color tint)
    {
        int materialField = record != null && record.TypeCode == EffectTypeTags.Sprite ? 0 : 9;
        int materialIndex = ResolveMaterialIndex(record, defaultIndex: 10, preferredField: materialField);
        EffectMaterialTable.Slot slot = EffectMaterialTable.Get(materialIndex);
        Texture2D texture = slot.IsUntextured ? WhiteTexture : GetMaterialTexture(materialIndex, slot);
        bool additive = record == null || record.TypeCode != EffectTypeTags.Sprite;
        return new GfxControlBillboard(
            record,
            locator,
            texture,
            _atlasFrames,
            slot.Cols,
            slot.Rows,
            slot.FirstFrame,
            slot.LastFrame,
            tint,
            additive,
            _lights,
            durationIndex: 8);
    }

    int ResolveMaterialIndex(GfxTweakRecord record, int defaultIndex, int preferredField)
    {
        if (record?.Fields == null || record.Fields.Length <= preferredField)
            return defaultIndex;

        // Stock stores material ids as int32 bit-patterns in the float array (never as 0.2 etc.).
        int asInt = record.FieldInt(preferredField, int.MinValue);
        if (asInt == EffectMaterialTable.UntexturedIndex)
            return EffectMaterialTable.UntexturedIndex;
        if (asInt >= 0 && asInt < EffectMaterialTable.SlotCount)
            return asInt;

        // Rare: literal float integer (e.g. 10.0f). Reject fractional values — those are colors/scales.
        float raw = record.Field(preferredField, defaultIndex);
        if (!float.IsNaN(raw) && !float.IsInfinity(raw))
        {
            int rounded = Mathf.RoundToInt(raw);
            if (Mathf.Abs(raw - rounded) <= 0.001f)
            {
                if (rounded == EffectMaterialTable.UntexturedIndex)
                    return EffectMaterialTable.UntexturedIndex;
                if (rounded >= 0 && rounded < EffectMaterialTable.SlotCount)
                    return rounded;
            }
        }

        return defaultIndex;
    }

    Texture2D GetMaterialTexture(int index, EffectMaterialTable.Slot slot)
    {
        if (_materialTextures.TryGetValue(index, out Texture2D cached))
            return cached;

        Texture2D tex = null;
        if (_textures != null)
        {
            int textureId = 0;
            if (_textureNames != null)
                _textureNames.TryResolve(slot.FileName, out textureId);
            if (textureId <= 0)
                textureId = FallbackTextureId(slot.FileName);

            if (textureId > 0)
            {
                try
                {
                    tex = _textures.GetAoTexture(textureId);
                }
                catch (System.Exception)
                {
                    tex = null;
                }
            }
        }

        if (tex == null)
        {
            Debug.LogWarning(
                $"[Effects] missing texture for material[{index}] '{slot.FileName}' — effect will skip quads.");
            // Do not cache WhiteTexture: that permanently poisons this material slot for the session.
            return null;
        }

        _materialTextures[index] = tex;
        return tex;
    }

    static Texture2D _whiteTexture;
    static Texture2D WhiteTexture
    {
        get
        {
            if (_whiteTexture != null)
                return _whiteTexture;
            _whiteTexture = new Texture2D(1, 1, TextureFormat.RGBA32, mipChain: false);
            _whiteTexture.name = "EffectWhite";
            _whiteTexture.SetPixel(0, 0, Color.white);
            _whiteTexture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            return _whiteTexture;
        }
    }

    static int FallbackTextureId(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return 15620;

        switch (fileName.ToLowerInvariant())
        {
            case "muzzleflash.png": return 15620;
            case "s_muzzleflash.png": return 15979;
            case "s_bullet.png": return 18398;
            case "s_bulletfront.png": return 18399;
            case "spark.png": return 263828;
            case "dustcloud.png": return 6331;
            case "muzzle_flash_mg_side.png": return 263591;
            case "muzzle_flash_mg_front.png": return 263592;
            case "muzzle_flash_blast.png": return 263745;
            case "star_2_ball.png": return 0;
            case "mine_explosion02_01_v01.png": return 0;
            default: return 0;
        }
    }

    static GfxTweakRecord CreateFallbackRecord(int effectId)
    {
        return new GfxTweakRecord
        {
            Id = effectId,
            TypeCode = EffectTypeTags.Flare,
            Fields = new[] { 0f, 0f, 0.6f, 1.2f, 0f, 0f, 0f, 0f, DefaultLifetime, 10f },
        };
    }
}
