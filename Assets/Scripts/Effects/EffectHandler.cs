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
    /// <summary>
    /// Stock <c>CharCastNano_t</c> ctor (<c>Gamecode 1007b6d7</c>) gives the cast effect
    /// <c>SetDuration(6000)</c>; a Meta hands that on to its children (<c>100e5e09</c>). The cast ends
    /// the effect itself, with <c>NextState</c> when the result arrives (<c>1007b1e3</c>), which is a
    /// graceful terminate (<c>_GfxControl_t::NextState</c> 100a76f9 calls slot 6).
    /// </summary>
    const float CastEffectSeconds = 6000f;
    /// <summary>Stock NewHitLocation target attach (Bip01 Head_ac).</summary>
    const int TracerTargetAttachId = EffectAttachIds.BoneHead;

    struct PendingTrace
    {
        public EffectHitLocation HitLoc;
        public EffectHandle Tracer;
    }

    /// <summary>
    /// A nano cast in progress: stock <c>CharCastNano_t</c> (charstate 5), from the cast start until
    /// its release clip ends. Its caster's clip notes are watched for effect1start.
    /// </summary>
    sealed class PendingCast
    {
        public EffectHandle Handle;
        public bool Released;
        public Character Caster;
        public Character Target;
        public VisualDynel CasterVisual;
        public VisualDynel TargetVisual;
        public NanoSpell Nano;
        public int NanoId;
        public bool IsLocal;
        public bool TracerLaunched;
        public CatAnimPlayer Player;
        public System.Action<int, int> OnNote;
    }

    readonly GfxTweakCatalog _catalog;
    readonly AoImageTextureCache _textures;
    readonly EffectTextureNames _textureNames;
    readonly EffectBillboardBatch _batch = new EffectBillboardBatch();
    readonly EffectAtlasFrames _atlasFrames = new EffectAtlasFrames();
    readonly EffectLightPool _lights = new EffectLightPool();
    readonly List<EffectHandle> _handles = new List<EffectHandle>(64);
    readonly List<GfxControl> _tick = new List<GfxControl>(64);
    readonly List<EffectHandle> _sweep = new List<EffectHandle>(64);
    readonly List<EffectBillboardBatch.Quad> _quads = new List<EffectBillboardBatch.Quad>(128);
    readonly List<EffectBillboardBatch.Strip> _strips = new List<EffectBillboardBatch.Strip>(8);
    readonly Dictionary<int, Texture2D> _materialTextures = new Dictionary<int, Texture2D>();
    readonly HashSet<EffectHandle> _handleSet = new HashSet<EffectHandle>();
    readonly List<EffectHitLocation> _hitLocations = new List<EffectHitLocation>(16);
    readonly Dictionary<int, PendingTrace> _pendingTraces = new Dictionary<int, PendingTrace>();
    readonly Dictionary<int, PendingCast> _pendingCasts = new Dictionary<int, PendingCast>();

    /// <summary>
    /// Live buff effects by recipient (a Character or, in GfxTest, a VisualDynel) and nano. Stock keeps
    /// the handle in the recipient's buff entry (FUN_100517f3, entry +0x1c).
    /// </summary>
    readonly Dictionary<(object Host, int Nano), EffectHandle> _buffEffects = new Dictionary<(object Host, int Nano), EffectHandle>();

    int _spawnDepth;
    int _nextHitLocId = 1;

    public bool ShowOthersEffects { get; set; } = true;

    /// <summary>Controls processed on the last <see cref="Tick"/>. Read-only, for debug tooling.</summary>
    public IReadOnlyList<GfxControl> LiveControls => _tick;

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
        if (!IsSpell1(effectId))
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
        if (!IsSpell1(effectId))
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
    /// Stock <c>CreateGfxControl(id, caster, target, attach)</c> (Gamecode 100d1705) builds a control
    /// only for typeCode 0x3f2 and returns null for anything else, so a caster/target effect is
    /// always a Spell1.
    /// </summary>
    bool IsSpell1(int effectId)
        => _catalog.TryGet(effectId, out GfxTweakRecord record) && record.TypeCode == EffectTypeTags.Spell1;

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

    GfxControl IEffectSpawnFactory.CreateOwnedControl(int effectId, EffectLocator locator)
    {
        if (effectId == EffectTypeTags.RejectedEffectId || effectId <= 0 || locator == null)
            return null;
        if (_spawnDepth > MaxSpawnDepth)
            return null;

        _spawnDepth++;
        try
        {
            return CreateControl(effectId, locator, Color.white, null, null, 0, null, null);
        }
        finally
        {
            _spawnDepth--;
        }
    }

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

    /// <summary>
    /// CastNanoSpell: stock <c>CharCastNano_t</c> begins. The cast effect goes on the caster, and the
    /// caster's clips are watched for the note that launches the tracer (<see cref="OnCastNote"/>).
    /// </summary>
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

        // Recorded even without a cast handle: the tracer and the hit need the target either way.
        RememberPendingCast(caster.Identity.Instance, new PendingCast
        {
            Handle = castHandle,
            Caster = caster,
            Target = target,
            Nano = nano,
            NanoId = nanoId,
            IsLocal = isLocal,
        });

        return castHandle;
    }

    /// <summary>
    /// FinishNanoCasting: the cast's result is in. Stock <c>CharCastNano_t</c> (Gamecode
    /// <c>1007b1e3</c>) gives the cast effect <c>NextState</c> and plays the release clip. The caster
    /// stays in charstate 5 through that clip, so its effect1start note still launches the tracer, and
    /// the impact and hit wait for the clip to end (<see cref="PlayNanoHit"/>).
    /// </summary>
    public void FinishNanoCast(Character caster)
    {
        if (caster != null)
            ReleasePendingCast(caster.Identity.Instance);
    }

    /// <summary>
    /// The release clip ended: stock plays <c>impacteffecttype</c> (414) then <c>hiteffecttype</c>
    /// (361) on the target with attach 0 (CAT mesh frame / mid-body, not dynel feet), and the cast is
    /// over (<c>1007b1e3</c>, second phase).
    /// </summary>
    public EffectHandle PlayNanoHit(Character caster, Character target, int nanoId, bool isLocal, NanoSpell nano)
    {
        if (!isLocal && !ShowOthersEffects)
            return null;

        int casterKey = caster != null ? caster.Identity.Instance : 0;
        // Read before the pending cast is dropped.
        if (target == null)
            TryGetPendingCastTarget(casterKey, out target);

        ReleasePendingCast(casterKey);
        ForgetPendingCast(casterKey);
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
    /// A buff's time arrived (CharacterAction 98 SetNanoDuration, Gamecode 1005dd25 into FUN_100517f3).
    /// Stock first ends any buff the new one replaces, then, for a nano whose flags have bit 0x10000
    /// (100518a9), creates its stat 413 effecttype on the recipient (CreateEffect2(id, dynel, 0)) and
    /// sets its duration to the time in centiseconds / 100, integer-divided. A time of 0 does nothing.
    /// </summary>
    public EffectHandle AddNanoBuff(Character target, int nanoId, NanoSpell nano, int centiseconds, bool isLocal)
    {
        if (target == null)
            return null;
        if (!isLocal && !ShowOthersEffects)
            return null;
        return AddNanoBuff(target, EffectLocator.OnDynel(target, 0), nanoId, nano, centiseconds);
    }

    /// <summary>GfxTest: <see cref="AddNanoBuff(Character, int, NanoSpell, int, bool)"/> on a bare visual.</summary>
    public EffectHandle AddNanoBuffVisual(VisualDynel target, int nanoId, NanoSpell nano, int centiseconds)
        => target == null ? null : AddNanoBuff(target, EffectLocator.OnVisual(target, 0), nanoId, nano, centiseconds);

    EffectHandle AddNanoBuff(object host, EffectLocator locator, int nanoId, NanoSpell nano, int centiseconds)
    {
        // 10051807 compares the time unsigned: only 0 is turned away.
        if (centiseconds == 0 || nano == null)
            return null;

        // Stock ends the buffs the new one conflicts with (FUN_1004e9cc); the port only knows the same
        // nano replacing itself.
        RemoveNanoBuff(host, nanoId);

        if (!NanoEffectResolver.HasBuffEffect(nano)
            || !NanoEffectResolver.TryResolveBuff(nano, out NanoEffectResolver.SpellFx buffFx))
        {
            return null;
        }

        EffectHandle handle = CreateEffect2(buffFx.EffectId, locator, Color.white);
        if (handle == null)
        {
            Debug.LogWarning($"[Effects] CreateEffect2 failed buff nano={nanoId} effectId={buffFx.EffectId}");
            return null;
        }

        handle.SetDuration((uint)centiseconds / 100u);
        _buffEffects[(host, nanoId)] = handle;
        Debug.Log($"[Effects] nano buff nano={nanoId} effectId={buffFx.EffectId} seconds={(uint)centiseconds / 100u}");
        return handle;
    }

    /// <summary>
    /// The buff wore off (the Buff message with its first field 0: BuffIIR_c 100726e5 into FUN_10051041
    /// and FUN_10050b90). Stock ends its effect gracefully.
    /// </summary>
    public void RemoveNanoBuff(Character target, int nanoId) => RemoveNanoBuff((object)target, nanoId);

    /// <summary>GfxTest: <see cref="RemoveNanoBuff(Character, int)"/> on a bare visual.</summary>
    public void RemoveNanoBuffVisual(VisualDynel target, int nanoId) => RemoveNanoBuff((object)target, nanoId);

    void RemoveNanoBuff(object host, int nanoId)
    {
        if (host == null || !_buffEffects.TryGetValue((host, nanoId), out EffectHandle handle))
            return;

        _buffEffects.Remove((host, nanoId));
        if (handle != null && handle.IsAlive)
            handle.TerminateGracefully();
    }

    /// <summary>GfxTest: <see cref="PlayNanoCast"/> between two bare visuals.</summary>
    public EffectHandle PlayNanoCastVisual(
        VisualDynel caster,
        VisualDynel target,
        int nanoId,
        NanoSpell nano,
        int casterKey)
    {
        if (caster == null)
            return null;

        EffectHandle castHandle = null;
        if (nano != null && NanoEffectResolver.TryResolveCast(nano, out NanoEffectResolver.SpellFx castFx) && castFx.EffectId != 0)
        {
            castHandle = CreateEffect2(castFx.EffectId, caster, target != null ? target : caster, Color.white, 0);
            castHandle?.SetDuration(CastEffectSeconds);
            if (castHandle != null)
                Debug.Log($"[Effects] nano cast nano={nanoId} effectId={castFx.EffectId} visual spell1");
        }

        RememberPendingCast(casterKey, new PendingCast
        {
            Handle = castHandle,
            CasterVisual = caster,
            TargetVisual = target != null ? target : caster,
            Nano = nano,
            NanoId = nanoId,
            IsLocal = true,
        });
        return castHandle;
    }

    /// <summary>GfxTest: <see cref="FinishNanoCast"/>.</summary>
    public void FinishNanoCastVisual(int casterKey) => ReleasePendingCast(casterKey);

    /// <summary>GfxTest: <see cref="PlayNanoHit"/> on a bare visual.</summary>
    public EffectHandle PlayNanoHitVisual(
        VisualDynel target,
        int nanoId,
        NanoSpell nano,
        int casterKey,
        out EffectHandle impactHandle)
    {
        impactHandle = null;
        ReleasePendingCast(casterKey);
        ForgetPendingCast(casterKey);
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

        // Stock's launch (FUN_1004f989) sets no duration; only the older stand-ins need one.
        if (!IsStockTracer(tracer))
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
        // Stock-built tracers run their own course. Stock keeps the handle with the cast's result entry
        // (FUN_1004f989); whether anything there ends it early is not traced, so the port leaves it.
        if (pending.Tracer != null && pending.Tracer.IsAlive && !IsStockTracer(pending.Tracer))
            pending.Tracer.TerminateGracefully();
    }

    void RememberPendingCast(int casterKey, PendingCast cast)
    {
        if (casterKey == 0)
            return;

        ReleasePendingCast(casterKey);
        ForgetPendingCast(casterKey);
        if (cast.Handle == null && cast.Target == null && cast.TargetVisual == null)
            return;

        _pendingCasts[casterKey] = cast;

        VisualDynel visual = cast.Caster != null ? cast.Caster.Visual : cast.CasterVisual;
        if (visual != null && visual.TryGetAnimPlayer(out CatAnimPlayer player))
        {
            cast.Player = player;
            cast.OnNote = (eventId, animId) => OnCastNote(casterKey, cast, eventId);
            player.NoteReached += cast.OnNote;
        }
    }

    /// <summary>
    /// A clip note on a casting caster. Stock (<c>FUN_100452d3</c>) launches the tracer on
    /// effect1start while the caster is in charstate 5 (<c>FUN_1004f989</c>):
    /// <c>NewHitLocation(caster, LHand 2001, target, Head 1006, true)</c>, then
    /// <c>CreateEffect2(tracereffecttype, hitLoc)</c>, and only with a target. One per cast here.
    /// </summary>
    void OnCastNote(int casterKey, PendingCast cast, int eventId)
    {
        if (eventId != AnimNoteIds.Effect1Start || cast.TracerLaunched)
            return;
        if (!_pendingCasts.TryGetValue(casterKey, out PendingCast live) || live != cast)
            return;

        cast.TracerLaunched = true;
        if (cast.Nano == null
            || !NanoEffectResolver.TryResolveTrace(cast.Nano, out NanoEffectResolver.SpellFx traceFx)
            || traceFx.EffectId == 0)
        {
            return;
        }

        if (cast.Caster != null)
        {
            if (cast.Target == null)
                return;
            StartTracer(
                casterKey,
                cast.Caster,
                cast.Target,
                null,
                null,
                traceFx.EffectId,
                EstimateTravelSeconds(cast.Caster, cast.Target, traceFx.EffectId),
                cast.NanoId,
                cast.IsLocal,
                holdUntilComplete: false);
        }
        else if (cast.CasterVisual != null && cast.TargetVisual != null)
        {
            StartTracer(
                casterKey,
                null,
                null,
                cast.CasterVisual,
                cast.TargetVisual,
                traceFx.EffectId,
                0.35f,
                cast.NanoId,
                cast.IsLocal,
                holdUntilComplete: false);
        }
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

    /// <summary>
    /// The cast's result arrived. Stock <c>CharCastNano_t</c> (Gamecode 1007b1e3) calls
    /// <c>NextState</c> on the cast effect and otherwise leaves it running: for a Spell1 that re-times
    /// its windows around now (<c>100f2617</c>) or, when field 33 is set, does nothing at all, so the
    /// hand effects play out their own template timeline. Once per cast.
    /// </summary>
    void ReleasePendingCast(int casterKey)
    {
        if (casterKey == 0 || !_pendingCasts.TryGetValue(casterKey, out PendingCast pending) || pending.Released)
            return;

        pending.Released = true;
        if (pending.Handle != null && pending.Handle.IsAlive)
            pending.Handle.NextState();
    }

    /// <summary>The cast is over: stop watching its caster's notes.</summary>
    void ForgetPendingCast(int casterKey)
    {
        if (casterKey == 0 || !_pendingCasts.TryGetValue(casterKey, out PendingCast pending))
            return;

        _pendingCasts.Remove(casterKey);
        if (pending.Player != null && pending.OnNote != null)
            pending.Player.NoteReached -= pending.OnNote;
    }

    /// <summary>
    /// Interrupt / Clear. Stock's abort path (Gamecode 1007b1e3, top) deletes the cast effect
    /// outright with <c>DeleteEffect</c>.
    /// </summary>
    public void CancelPendingTrace(int casterKey)
    {
        if (casterKey != 0 && _pendingCasts.TryGetValue(casterKey, out PendingCast pending))
        {
            ForgetPendingCast(casterKey);
            if (pending.Handle != null)
            {
                pending.Handle.Destroy();
                Unregister(pending.Handle);
            }
        }
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

            castHandle.SetDuration(CastEffectSeconds);
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

        // Sweep a snapshot. Releasing a finished parent (Meta, Sequencer, ...) deletes its children,
        // which unregisters them from _handles in the middle of this loop; indexing _handles directly
        // then removed the wrong entry or ran off the end once a parent and its live children went
        // together.
        _tick.Clear();
        _sweep.Clear();
        _sweep.AddRange(_handles);
        for (int i = _sweep.Count - 1; i >= 0; i--)
        {
            EffectHandle handle = _sweep[i];
            if (handle == null || !_handleSet.Contains(handle))
                continue;

            handle.CollectLive(_tick);
            if (!handle.IsAlive)
                Unregister(handle);
        }

        _batch.Clear();
        _quads.Clear();
        _strips.Clear();
        for (int i = 0; i < _tick.Count; i++)
        {
            GfxControl control = _tick[i];
            if (!control.Process(dt))
                continue;
            control.CollectBillboards(_quads, camera);
            control.CollectStrips(_strips, camera);
        }

        for (int i = 0; i < _quads.Count; i++)
            _batch.Add(_quads[i]);
        for (int i = 0; i < _strips.Count; i++)
            _batch.Add(_strips[i]);

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

        locator = ApplyTemplateAttach(record, locator, attachOverride);

        int typeCode = record.TypeCode;
        switch (typeCode)
        {
            case EffectTypeTags.Meta:
                return new GfxControlMeta(record, locator, this, tint);
            case EffectTypeTags.Sequencer:
                return new GfxControlSequencer(record, locator, this, tint);
            case EffectTypeTags.Delay:
                return new GfxControlDelay(record, locator, this, tint);
            case EffectTypeTags.Scatter:
                return new GfxControlScatter(record, locator, this, tint);
            case EffectTypeTags.Stars:
                return CreateStars(record, locator);
            case EffectTypeTags.Flare:
                return CreateFlareType0(record, locator);
            case EffectTypeTags.FlareAlt:
                // _GfxControlFlare1_t (ctor 100dea3d) is a separate class; still the earlier emitter.
                return CreateFlare(record, locator, tint);
            case EffectTypeTags.Cord:
                return CreateCord(record, locator, tint);
            case EffectTypeTags.Sparks:
                return CreateSparks(record, locator, tint);
            case EffectTypeTags.Nano0:
            case EffectTypeTags.Nano1:
                return CreateNano(record, locator, tint);
            case EffectTypeTags.BParticle2:
                return CreateBParticle2(record, locator, tint);
            case EffectTypeTags.TParticle:
                return CreateTParticle(record, locator, tint);
            case EffectTypeTags.Highlight:
                return new GfxControlHighlight(record, locator);
            case EffectTypeTags.Spell1:
                return CreateSpell1(record, locator, tint, caster, target, attachOverride, casterVisual, targetVisual);
            case EffectTypeTags.Tracer1:
                return CreateTracer1(record, locator);
            case EffectTypeTags.Plasma:
                return CreatePlasma(record, locator);
            case EffectTypeTags.Tracer4:
                return CreateTracer4(record, locator);
            case EffectTypeTags.Deformer:
                if (record.FieldInt(10, 0) == DeformerSim.WobbleMode)
                    return new GfxControlDeformer(record, locator);
                return new GfxControlUnsupported(record, locator);
            case EffectTypeTags.Electra:
                if (record.FieldInt(10, 0) == ElectraSim.ShellMode)
                    return CreateElectra(record, locator);
                return new GfxControlUnsupported(record, locator);
            default:
                // Tracers are strips between two points, so they get the stretched control.
                // Remaining sprite-family types fall back to the generic billboard stand-in.
                // Everything else has no control yet; only warn when the stock class actually
                // draws, so audio, buff bookkeeping and typeCode 0 stay quiet.
                if (EffectTypeTags.IsBeam(typeCode))
                    return CreateTracer(record, locator, tint);
                if (EffectTypeTags.IsSpriteFamily(typeCode))
                    return CreateBillboard(record, locator, tint);
                if (!EffectTypeCatalog.IsIntentionallyNotRendered(typeCode))
                {
                    Debug.LogWarning(
                        $"[Effects] no control for {EffectTypeCatalog.Describe(typeCode)} id={record.Id}; skipping draw.");
                }
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
        EffectHitLocation hitLoc = null;
        locator?.TryGetHitLocation(out hitLoc);
        return new GfxControlStars(
            record, locator, texture, _atlasFrames,
            slot.Cols, slot.Rows, slot.FirstFrame, slot.LastFrame, hitLoc);
    }

    GfxControl CreateFlareType0(GfxTweakRecord record, EffectLocator locator)
    {
        // Visual init FUN_100dd071: material, columns, rows and frame range all from field 9.
        int materialIndex = ResolveMaterialIndex(record, defaultIndex: 9, preferredField: 9);
        EffectMaterialTable.Slot slot = EffectMaterialTable.Get(materialIndex);
        Texture2D texture = slot.IsUntextured ? WhiteTexture : GetMaterialTexture(materialIndex, slot);
        return new GfxControlFlareType0(
            record, locator, texture, _atlasFrames,
            slot.Cols, slot.Rows, slot.FirstFrame, slot.LastFrame);
    }

    GfxControl CreateFlare(GfxTweakRecord record, EffectLocator locator, Color tint)
    {
        int materialIndex = ResolveMaterialIndex(record, defaultIndex: 9, preferredField: 9);
        EffectMaterialTable.Slot slot = EffectMaterialTable.Get(materialIndex);
        Texture2D texture = slot.IsUntextured ? WhiteTexture : GetMaterialTexture(materialIndex, slot);
        return new GfxControlFlare(
            record, locator, texture, _atlasFrames,
            slot.Cols, slot.Rows, slot.FirstFrame, slot.LastFrame, tint, _lights);
    }

    GfxControl CreateCord(GfxTweakRecord record, EffectLocator locator, Color tint)
    {
        // Every Cord template names material 8 (light_halo2) in field 9, same as Flare.
        int materialIndex = ResolveMaterialIndex(record, defaultIndex: 8, preferredField: 9);
        EffectMaterialTable.Slot slot = EffectMaterialTable.Get(materialIndex);
        Texture2D texture = slot.IsUntextured ? WhiteTexture : GetMaterialTexture(materialIndex, slot);
        return new GfxControlCord(
            record, locator, texture, _atlasFrames,
            slot.Cols, slot.Rows, slot.FirstFrame, slot.LastFrame, tint, _lights);
    }

    GfxControl CreateNano(GfxTweakRecord record, EffectLocator locator, Color tint)
    {
        // 8010 and 8011 both name material 8 (light_halo2) in field 9, and stock reads that same field
        // in the create at FUN_100e7a0e to pick the material, columns, rows and frame range.
        int materialIndex = ResolveMaterialIndex(record, defaultIndex: 8, preferredField: 9);
        EffectMaterialTable.Slot slot = EffectMaterialTable.Get(materialIndex);
        Texture2D texture = slot.IsUntextured ? WhiteTexture : GetMaterialTexture(materialIndex, slot);
        return new GfxControlNano(
            record, locator, texture, _atlasFrames,
            slot.Cols, slot.Rows, slot.FirstFrame, slot.LastFrame, tint, _lights);
    }

    GfxControl CreateSparks(GfxTweakRecord record, EffectLocator locator, Color tint)
    {
        int materialIndex = ResolveMaterialIndex(record, defaultIndex: 33, preferredField: 9);
        EffectMaterialTable.Slot slot = EffectMaterialTable.Get(materialIndex);
        Texture2D texture = slot.IsUntextured ? WhiteTexture : GetMaterialTexture(materialIndex, slot);
        return new GfxControlSparks(
            record, locator, texture, _atlasFrames,
            slot.Cols, slot.Rows, slot.FirstFrame, slot.LastFrame, tint, _lights);
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

    /// <summary>
    /// Stock <c>_GfxControl_t::InitDynelTemplate</c> (0x100d2d31) reads the attach id from the
    /// caller, and when that is 0 falls back to the template's own field 7:
    /// <c>if (attachId == 0) attachId = record.GetInt(7)</c>. That is how a hit effect places
    /// itself on a bone — 2710, the blood splat, asks for 1004 (Bip01 Spine3_ac, upper chest)
    /// rather than the mid-body mesh frame. Applied per record so each child of a Meta or
    /// Sequencer tree gets its own attach.
    /// </summary>
    static EffectLocator ApplyTemplateAttach(
        GfxTweakRecord record, EffectLocator locator, int attachOverride)
    {
        if (attachOverride != 0 || record == null || locator == null || record.FieldCount <= 7)
            return locator;

        int templateAttach = record.FieldInt(7, 0);
        if (templateAttach == 0)
            return locator;
        if (!EffectAttachIds.IsBone(templateAttach)
            && !EffectAttachIds.IsAttractor(templateAttach)
            && !EffectAttachIds.IsMuzzle(templateAttach))
        {
            return locator;
        }

        return locator.WithAttach(templateAttach);
    }

    /// <summary>
    /// Stock builds a Tracer1 only from a hit location (<c>CreateGfxControl(id, hitLoc)</c>,
    /// <c>100fecea</c>), reading its start and end once. Without one stock readies it at once.
    /// </summary>
    GfxControl CreateTracer1(GfxTweakRecord record, EffectLocator locator)
    {
        if (locator == null
            || !locator.TryGetHitLocation(out EffectHitLocation hitLoc)
            || !hitLoc.TryGetEndpoints(out Vector3 start, out Vector3 end))
        {
            return new GfxControlUnsupported(record, locator);
        }

        // Visual build FUN_100fe845: material and frame range from field 9, as Flare.
        int materialIndex = ResolveMaterialIndex(record, defaultIndex: 16, preferredField: 9);
        EffectMaterialTable.Slot slot = EffectMaterialTable.Get(materialIndex);
        Texture2D texture = slot.IsUntextured ? WhiteTexture : GetMaterialTexture(materialIndex, slot);
        return new GfxControlTracer1(
            record,
            EffectLocator.WorldPoint(start, Quaternion.identity),
            start,
            end,
            texture,
            _atlasFrames,
            slot.Cols,
            slot.Rows,
            slot.FirstFrame);
    }

    /// <summary>
    /// Stock builds a Plasma only from a hit location (<c>CreateGfxControl(id, hitLoc)</c>,
    /// <c>100ec5ae</c>) and follows it every frame.
    /// </summary>
    GfxControl CreatePlasma(GfxTweakRecord record, EffectLocator locator)
    {
        if (locator == null || !locator.TryGetHitLocation(out EffectHitLocation hitLoc))
            return new GfxControlUnsupported(record, locator);

        // FUN_100ec522: GfxVisualPlasma(material from field 9, 0, 6.28, 12).
        int materialIndex = ResolveMaterialIndex(record, defaultIndex: 8, preferredField: 9);
        EffectMaterialTable.Slot slot = EffectMaterialTable.Get(materialIndex);
        Texture2D texture = slot.IsUntextured ? WhiteTexture : GetMaterialTexture(materialIndex, slot);
        return new GfxControlPlasma(record, locator, hitLoc, texture);
    }

    /// <summary>
    /// Stock builds a Tracer4 only from a hit location (<c>CreateGfxControl(id, hitLoc)</c>,
    /// <c>101002e0</c>), reading its start and end once.
    /// </summary>
    GfxControl CreateTracer4(GfxTweakRecord record, EffectLocator locator)
    {
        if (locator == null
            || !locator.TryGetHitLocation(out EffectHitLocation hitLoc)
            || !hitLoc.TryGetEndpoints(out Vector3 start, out Vector3 end))
        {
            return new GfxControlUnsupported(record, locator);
        }

        // Visual build 100ffd67: GfxVisualCord4(material from field 9, null, additive, modulate).
        int materialIndex = ResolveMaterialIndex(record, defaultIndex: 15, preferredField: 9);
        EffectMaterialTable.Slot slot = EffectMaterialTable.Get(materialIndex);
        Texture2D texture = slot.IsUntextured ? WhiteTexture : GetMaterialTexture(materialIndex, slot);
        return new GfxControlTracer4(record, EffectLocator.WorldPoint(start, Quaternion.identity), start, end, texture);
    }

    /// <summary>
    /// Electra's visual (100d98e0): GfxVisualElectra(material from field 9, 0, additive), sized by the
    /// material's own columns and rows.
    /// </summary>
    GfxControl CreateElectra(GfxTweakRecord record, EffectLocator locator)
    {
        int materialIndex = ResolveMaterialIndex(record, defaultIndex: 2, preferredField: 9);
        EffectMaterialTable.Slot slot = EffectMaterialTable.Get(materialIndex);
        Texture2D texture = slot.IsUntextured ? WhiteTexture : GetMaterialTexture(materialIndex, slot);
        return new GfxControlElectra(record, locator, texture, slot.Cols, slot.Rows);
    }

    /// <summary>Tracers stock builds from its own data and lets run their course (no SetDuration, no cut-off).</summary>
    static bool IsStockTracer(EffectHandle handle)
        => handle?.Control is GfxControlTracer1
           || handle?.Control is GfxControlPlasma
           || handle?.Control is GfxControlTracer4
           || handle?.Control is GfxControlStars { IsHitLocationTracer: true };

    GfxControl CreateTracer(GfxTweakRecord record, EffectLocator locator, Color tint)
    {
        // Tracer templates keep the material in field 9 like the rest of the sprite family;
        // s_bullet.png (15) and s_bulletfront.png (16) are the common ones.
        int materialIndex = ResolveMaterialIndex(record, defaultIndex: 15, preferredField: 9);
        EffectMaterialTable.Slot slot = EffectMaterialTable.Get(materialIndex);
        Texture2D texture = slot.IsUntextured ? WhiteTexture : GetMaterialTexture(materialIndex, slot);
        bool additive = record == null || SpriteEmitterMath.IsAdditive(record.FieldInt(0, 0));
        return new GfxControlTracer(
            record,
            locator,
            texture,
            _atlasFrames,
            slot.Cols,
            slot.Rows,
            slot.FirstFrame,
            slot.LastFrame,
            tint,
            additive);
    }

    GfxControl CreateBillboard(GfxTweakRecord record, EffectLocator locator, Color tint)
    {
        // typeCode 1012 keeps the material index in field 0, so that layout has no flags word and
        // cannot be asked about blending; every 1012 template is alpha-blended in stock anyway.
        bool spriteLayout = record != null && record.TypeCode == EffectTypeTags.Sprite;
        int materialField = spriteLayout ? 0 : 9;
        int materialIndex = ResolveMaterialIndex(record, defaultIndex: 10, preferredField: materialField);
        EffectMaterialTable.Slot slot = EffectMaterialTable.Get(materialIndex);
        Texture2D texture = slot.IsUntextured ? WhiteTexture : GetMaterialTexture(materialIndex, slot);
        // Flags bit 0x800 picks DESTBLEND=INVSRCALPHA over ONE, i.e. matte instead of glowing.
        bool additive = !spriteLayout
            && (record == null || SpriteEmitterMath.IsAdditive(record.FieldInt(0, 0)));
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
