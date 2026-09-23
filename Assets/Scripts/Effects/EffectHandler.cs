using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stock-shaped effect factory: effectId → gfxtweak typeCode → concrete <see cref="GfxControl"/>.
/// Implements <see cref="IEffectSpawnFactory"/> for Meta / Sequencer / Delay children.
/// </summary>
public sealed class EffectHandler : IEffectSpawnFactory
{
    const int MaxSpawnDepth = 6;
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

    /// <summary>The draw batch, for debug tooling.</summary>
    public EffectBillboardBatch Batch => _batch;
    readonly EffectAtlasFrames _atlasFrames = new EffectAtlasFrames();
    readonly EffectLightPool _lights = new EffectLightPool();
    readonly List<EffectHandle> _handles = new List<EffectHandle>(64);
    readonly List<GfxControl> _tick = new List<GfxControl>(64);
    readonly List<EffectHandle> _sweep = new List<EffectHandle>(64);
    readonly List<EffectBillboardBatch.Quad> _quads = new List<EffectBillboardBatch.Quad>(128);
    readonly List<EffectBillboardBatch.Strip> _strips = new List<EffectBillboardBatch.Strip>(8);
    readonly List<EffectBillboardBatch.MeshDraw> _meshes = new List<EffectBillboardBatch.MeshDraw>(4);
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
            // An id missing from gfxtweak draws nothing in stock either; only a real failure is reported.
            if (_catalog.TryGet(buffFx.EffectId, out _))
                Debug.LogWarning($"[Effects] CreateEffect2 failed buff nano={nanoId} effectId={buffFx.EffectId}");
            return null;
        }

        handle.SetDuration((uint)centiseconds / 100u);
        MarkPersistent(handle);
        _buffEffects[(host, nanoId)] = handle;
        Debug.Log($"[Effects] nano buff nano={nanoId} effectId={buffFx.EffectId} seconds={(uint)centiseconds / 100u}");
        return handle;
    }

    /// <summary>
    /// Exempts a buff's control tree from the port's watchdog (<see cref="GfxControl.IgnoreWatchdog"/>).
    /// A Sequencer passes it on to the children it spawns later.
    /// </summary>
    static void MarkPersistent(EffectHandle handle)
    {
        GfxControl control = handle?.Control;
        if (control == null)
            return;
        control.IgnoreWatchdog = true;
        if (control is GfxControlMeta meta)
        {
            foreach (EffectHandle child in meta.Children)
                MarkPersistent(child);
        }
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
            if (_catalog.TryGet(traceEffectId, out _))
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
        // _EffectHandler_t::ComputeWind (100cdee7), once a frame before the controls run.
        EffectWind.Advance(dt);

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
        _meshes.Clear();
        for (int i = 0; i < _tick.Count; i++)
        {
            GfxControl control = _tick[i];
            if (!control.Process(dt))
                continue;
            control.CollectBillboards(_quads, camera);
            control.CollectStrips(_strips, camera);
            control.CollectMeshes(_meshes, camera);
        }

        // GroundShake posts its offset while the controls run; stock moves the eye the moment it does
        // (n3Camera_t +0x1d4 then UpdateTargetEye). Consume it here so it is applied once and never
        // carries into the next frame, whichever host drove the tick.
        Vector3 shake = EffectCameraShake.Consume();
        if (shake != Vector3.zero && camera != null)
            camera.transform.position += shake;

        for (int i = 0; i < _quads.Count; i++)
            _batch.Add(_quads[i]);
        for (int i = 0; i < _strips.Count; i++)
            _batch.Add(_strips[i]);
        for (int i = 0; i < _meshes.Count; i++)
            _batch.Add(_meshes[i]);

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
        // 100d0656: an id gfxtweak.bin doesn't hold gives no control in stock (the table is loaded only from
        // Setupf, 100ce664), so nothing is drawn: 39745 (the buff of 39 nanos) and 71016's child 71025.
        if (!_catalog.TryGet(effectId, out GfxTweakRecord record))
            return null;

        locator = ApplyTemplateAttach(record, locator, attachOverride);
        EffectBodyTable.Entry body = EffectBodyTable.Read(_catalog, record, locator);
        if (TakesLocatorTemplate(record.TypeCode))
            locator = locator?.WithTemplate(record, body.HasOffset ? body.OffsetZ : (float?)null);

        int typeCode = record.TypeCode;
        switch (typeCode)
        {
            case EffectTypeTags.Meta:
                return new GfxControlMeta(record, locator, this, tint);
            case EffectTypeTags.Sequencer:
                return new GfxControlSequencer(record, locator, this, tint);
            case EffectTypeTags.BuffFsm:
                return new GfxControlBuffFsm(record, locator, this);
            case EffectTypeTags.BuffPlaceHolder:
                return new GfxControlBuffPlaceHolder(record, locator, this, body);
            case EffectTypeTags.Delay:
                return new GfxControlDelay(record, locator, this, tint);
            case EffectTypeTags.Toggle:
                // 1011265a: the child is made unregistered and run by the Toggle itself.
                return new GfxControlToggle(record, locator, this);
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
                return CreateCord(record, locator);
            case EffectTypeTags.Sparks:
                return CreateSparks(record, locator, tint);
            case EffectTypeTags.Fire:
                return CreateFire(record, locator);
            case EffectTypeTags.Smoke:
                return CreateSmoke(record, locator);
            case EffectTypeTags.Spiral:
                return CreateSpiral(record, locator);
            case EffectTypeTags.Spiral2:
                return CreateSpiral2(record, locator);
            case EffectTypeTags.Beam:
                return CreateBeam(record, locator);
            case EffectTypeTags.CrazyCone:
                // 100d7401: GfxVisualCone(field 11 segments, fields 14/15 uv, material field 9).
                return new GfxControlCrazyCone(record, locator, WrappedTexture(record, field: 9));
            case EffectTypeTags.GroundRing:
                // 100e1f80: GfxVisualGroundRing(field 10 segments, fields 11/12 uv, material field 9).
                return new GfxControlGroundRing(record, locator, WrappedTexture(record, field: 9));
            case EffectTypeTags.Mesh:
                // 100e53fb: the model is one of four literals picked by field 9.
                return new GfxControlMesh(record, locator, Models);
            case EffectTypeTags.Nano0:
                return CreateNano0(record, locator);
            case EffectTypeTags.Nano1:
                return CreateNano(record, locator, tint);
            case EffectTypeTags.BParticle2:
                return CreateBParticle2(record, locator, tint);
            case EffectTypeTags.BParticle:
                return CreateBParticle(record, locator);
            case EffectTypeTags.GroundGrid:
                return CreateGroundGrid(record, locator);
            case EffectTypeTags.EffectMesh:
                return new GfxControlEffectMesh(record, locator, Models);
            case EffectTypeTags.MParticle:
                return new GfxControlMParticle(record, locator, Models);
            case EffectTypeTags.TParticle:
                return CreateTParticle(record, locator);
            case EffectTypeTags.TParticle2:
                return CreateTParticle2(record, locator);
            case EffectTypeTags.Highlight:
                return new GfxControlHighlight(record, locator);
            case EffectTypeTags.GroundShake:
                return new GfxControlGroundShake(record, locator);
            case EffectTypeTags.Spell1:
                return CreateSpell1(record, locator, tint, caster, target, attachOverride, casterVisual, targetVisual);
            case EffectTypeTags.Tracer1:
                return CreateTracer1(record, locator);
            case EffectTypeTags.Plasma:
                return CreatePlasma(record, locator);
            case EffectTypeTags.Tracer4:
                return CreateTracer4(record, locator);
            case EffectTypeTags.Tracer5:
                return CreateTracer5(record, locator);
            case EffectTypeTags.Tracer6:
                return CreateTracer6(record, locator);
            case EffectTypeTags.Tracer3:
                return CreateTracer3(record, locator);
            case EffectTypeTags.ShockWave:
                return CreateShockWave(record, locator);
            case EffectTypeTags.VulcanRocks:
                return new GfxControlVulcanRocks(record, locator, Models);
            case EffectTypeTags.Sprite:
                return CreateSprite(record, locator);
            case EffectTypeTags.VolGrid:
                // 10115eb5: GfxVisualVolGrid(flags, fields 14-16, material from field 9).
                return new GfxControlVolGrid(record, locator, WrappedTexture(record, field: 9));
            case EffectTypeTags.SkyFlash:
                // 100ef58b: GfxVisualCone per cone, material from field 9.
                return new GfxControlSkyFlash(record, locator, WrappedTexture(record, field: 9));
            case EffectTypeTags.Trail2:
                // 10115260: GfxVisualTrail2(material from field 10, flags, field 11 samples), texture clamped.
                return new GfxControlTrail2(record, locator, ClampedTexture(record, field: 10));
            case EffectTypeTags.Deformer:
                if (record.FieldInt(10, 0) == DeformerSim.WobbleMode)
                    return new GfxControlDeformer(record, locator);
                return new GfxControlUnsupported(record, locator);
            case EffectTypeTags.Electra:
                if (record.FieldInt(10, 0) == ElectraSim.ShellMode)
                    return CreateElectra(record, locator);
                return new GfxControlUnsupported(record, locator);
            case EffectTypeTags.Suns:
                if (record.FieldInt(10, 0) is SunsSim.SparkLineType or SunsSim.RingType or SunsSim.HaloType)
                    return CreateSuns(record, locator);
                return new GfxControlUnsupported(record, locator);
            case EffectTypeTags.Shield:
                return CreateShield(record, locator);
            case EffectTypeTags.Shield2:
                return CreateShield2(record, locator);
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

    /// <summary>Visual build 100d627a: GfxVisualCord4(material from field 9, null, additive, no life v).</summary>
    GfxControl CreateCord(GfxTweakRecord record, EffectLocator locator)
    {
        // Every Cord template names material 8 (light_halo2) in field 9, same as Flare.
        int materialIndex = ResolveMaterialIndex(record, defaultIndex: 8, preferredField: 9);
        EffectMaterialTable.Slot slot = EffectMaterialTable.Get(materialIndex);
        Texture2D texture = slot.IsUntextured ? WhiteTexture : GetMaterialTexture(materialIndex, slot);
        return new GfxControlCord(record, locator, texture);
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

    /// <summary>
    /// Stock Nano0 (init <c>100e7146</c>): GfxVisualSprite2Type0(material from field 9, null, additive
    /// unless flag 0x800), with the material's frame range. No tint and no light.
    /// </summary>
    GfxControl CreateNano0(GfxTweakRecord record, EffectLocator locator)
    {
        int materialIndex = ResolveMaterialIndex(record, defaultIndex: 10, preferredField: 9);
        EffectMaterialTable.Slot slot = EffectMaterialTable.Get(materialIndex);
        Texture2D texture = slot.IsUntextured ? WhiteTexture : GetMaterialTexture(materialIndex, slot);
        return new GfxControlNano0(
            record, locator, texture, _atlasFrames,
            slot.Cols, slot.Rows, slot.FirstFrame, slot.LastFrame);
    }

    /// <summary>
    /// Stock Sparks (init <c>100f1939</c>): GfxVisualSprite2Type0(material from field 9, null, additive
    /// unless flag 0x800), with the material's frame range. No tint and no light.
    /// </summary>
    GfxControl CreateSparks(GfxTweakRecord record, EffectLocator locator, Color tint)
    {
        int materialIndex = ResolveMaterialIndex(record, defaultIndex: 33, preferredField: 9);
        EffectMaterialTable.Slot slot = EffectMaterialTable.Get(materialIndex);
        Texture2D texture = slot.IsUntextured ? WhiteTexture : GetMaterialTexture(materialIndex, slot);
        return new GfxControlSparks(
            record, locator, texture, _atlasFrames,
            slot.Cols, slot.Rows, slot.FirstFrame, slot.LastFrame);
    }

    /// <summary>
    /// Stock Fire (init <c>100dc59c</c>): GfxVisualSprite2Type0(material from field 9, null, additive),
    /// with the material's frame range. No tint and no light.
    /// </summary>
    GfxControl CreateFire(GfxTweakRecord record, EffectLocator locator)
    {
        int materialIndex = ResolveMaterialIndex(record, defaultIndex: 9, preferredField: 9);
        EffectMaterialTable.Slot slot = EffectMaterialTable.Get(materialIndex);
        Texture2D texture = slot.IsUntextured ? WhiteTexture : GetMaterialTexture(materialIndex, slot);
        return new GfxControlFire(
            record, locator, texture, _atlasFrames,
            slot.Cols, slot.Rows, slot.FirstFrame, slot.LastFrame);
    }

    /// <summary>
    /// Stock Smoke (init <c>100f0cd4</c>): GfxVisualSprite2Type0(material from field 9, null, additive only
    /// for effect 80005), with the material's frame range. No tint and no light.
    /// </summary>
    GfxControl CreateSmoke(GfxTweakRecord record, EffectLocator locator)
    {
        int materialIndex = ResolveMaterialIndex(record, defaultIndex: 31, preferredField: 9);
        EffectMaterialTable.Slot slot = EffectMaterialTable.Get(materialIndex);
        Texture2D texture = slot.IsUntextured ? WhiteTexture : GetMaterialTexture(materialIndex, slot);
        return new GfxControlSmoke(
            record, locator, texture, _atlasFrames,
            slot.Cols, slot.Rows, slot.FirstFrame, slot.LastFrame);
    }

    /// <summary>
    /// Stock Spiral (init <c>100f4f45</c>): two GfxVisualSpiral(material from field 9, i · 3.14, 6.28, 12),
    /// whose texture wraps along the ribbon (D3D wraps by default).
    /// </summary>
    GfxControl CreateSpiral(GfxTweakRecord record, EffectLocator locator)
    {
        int materialIndex = ResolveMaterialIndex(record, defaultIndex: 32, preferredField: 9);
        EffectMaterialTable.Slot slot = EffectMaterialTable.Get(materialIndex);
        Texture2D texture = slot.IsUntextured ? WhiteTexture : GetMaterialTexture(materialIndex, slot);
        if (texture != null)
            texture.wrapMode = TextureWrapMode.Repeat;
        return new GfxControlSpiral(record, locator, texture);
    }

    /// <summary>
    /// Stock Spiral2 (init <c>10111fdb</c>): field 10 GfxVisualSpiral2 ribbons built with the field 9
    /// material. u runs to field 17 along the ribbon, up to 10 in the records, so it wraps like Spiral's.
    /// </summary>
    GfxControl CreateSpiral2(GfxTweakRecord record, EffectLocator locator)
    {
        int materialIndex = ResolveMaterialIndex(record, defaultIndex: 32, preferredField: 9);
        EffectMaterialTable.Slot slot = EffectMaterialTable.Get(materialIndex);
        Texture2D texture = slot.IsUntextured ? WhiteTexture : GetMaterialTexture(materialIndex, slot);
        if (texture != null)
            texture.wrapMode = TextureWrapMode.Repeat;
        return new GfxControlSpiral2(record, locator, texture);
    }

    /// <summary>
    /// Stock Beam (build <c>101096ee</c>): <c>GfxVisualBeam(flags, field 14 blades, fields 15/16 uv,
    /// field 40, bit 8, material from field 9, bit 9, 0, 0, 0, material from field 39)</c>. A field 39
    /// below zero — which is most records — means there is no second material and no glare quad.
    /// </summary>
    GfxControl CreateBeam(GfxTweakRecord record, EffectLocator locator)
    {
        Texture2D texture = WrappedTexture(record, field: 9);
        int second = record != null ? record.FieldInt(39, -1) : -1;
        Texture2D cap = second >= 0 && second < EffectMaterialTable.SlotCount
            ? WrappedTexture(record, field: 39)
            : null;
        return new GfxControlBeam(record, locator, texture, cap);
    }

    GfxControl CreateBParticle2(GfxTweakRecord record, EffectLocator locator, Color tint)
    {
        int materialIndex = ResolveMaterialIndex(record, defaultIndex: 10, preferredField: 10);
        EffectMaterialTable.Slot slot = EffectMaterialTable.Get(materialIndex);
        Texture2D texture = slot.IsUntextured ? WhiteTexture : GetMaterialTexture(materialIndex, slot);
        return new GfxControlBParticle2(
            record, locator, texture, _atlasFrames,
            slot.Cols, slot.Rows, slot.FirstFrame, slot.LastFrame);
    }

    /// <summary>Loader 1010ebbd: the material is field 9 (visual build 1010ea04).</summary>
    EffectModels _models;

    /// <summary>ABIFF models for EffectMesh, MParticle and VulcanRocks, loaded on first use.</summary>
    EffectModels Models => _models ??= new EffectModels(_catalog.Database);

    GfxControl CreateGroundGrid(GfxTweakRecord record, EffectLocator locator)
    {
        int materialIndex = ResolveMaterialIndex(record, defaultIndex: 95, preferredField: 9);
        EffectMaterialTable.Slot slot = EffectMaterialTable.Get(materialIndex);
        Texture2D texture = slot.IsUntextured ? WhiteTexture : GetMaterialTexture(materialIndex, slot);
        return new GfxControlGroundGrid(record, locator, texture);
    }

    /// <summary>Loader 1010a644: the material is field 9 (visual build 1010a424).</summary>
    GfxControl CreateBParticle(GfxTweakRecord record, EffectLocator locator)
    {
        int materialIndex = ResolveMaterialIndex(record, defaultIndex: 8, preferredField: 9);
        EffectMaterialTable.Slot slot = EffectMaterialTable.Get(materialIndex);
        Texture2D texture = slot.IsUntextured ? WhiteTexture : GetMaterialTexture(materialIndex, slot);
        return new GfxControlBParticle(
            record, locator, texture, _atlasFrames,
            slot.Cols, slot.Rows, slot.FirstFrame, slot.LastFrame);
    }

    /// <summary>Loader 10112ccd: the material is field 9 (the visual is built with it, 10112b98).</summary>
    GfxControl CreateTParticle(GfxTweakRecord record, EffectLocator locator)
    {
        int materialIndex = ResolveMaterialIndex(record, defaultIndex: 10, preferredField: 9);
        EffectMaterialTable.Slot slot = EffectMaterialTable.Get(materialIndex);
        Texture2D texture = slot.IsUntextured ? WhiteTexture : GetMaterialTexture(materialIndex, slot);
        return new GfxControlTParticle(record, locator, texture);
    }

    /// <summary>
    /// Loader <c>10113780</c>: the material is field 10, and the init (<c>1011465c</c>) takes the
    /// cell grid and frame range from it for every particle's visual.
    /// </summary>
    GfxControl CreateTParticle2(GfxTweakRecord record, EffectLocator locator)
    {
        int materialIndex = ResolveMaterialIndex(record, defaultIndex: 10, preferredField: 10);
        EffectMaterialTable.Slot slot = EffectMaterialTable.Get(materialIndex);
        Texture2D texture = slot.IsUntextured ? WhiteTexture : GetMaterialTexture(materialIndex, slot);
        return new GfxControlTParticle2(
            record, locator, texture, slot.Cols, slot.Rows, slot.FirstFrame, slot.LastFrame);
    }

    /// <summary>
    /// Stock <c>_GfxControl_t::InitDynelTemplate</c> (0x100d2d31) reads the attach id from the
    /// caller, and when that is 0 falls back to the template's own field 7:
    /// <c>if (attachId == 0) attachId = record.GetInt(7)</c>. That is how a hit effect places
    /// itself on a bone — 2710, the blood splat, asks for 1004 (Bip01 Spine3_ac, upper chest)
    /// rather than the mid-body mesh frame. Applied per record so each child of a Meta or
    /// Sequencer tree gets its own attach.
    /// </summary>
    /// <summary>
    /// Types whose dynel ctor builds its locator with fields 1-3 as the template offset and 4-6 as its
    /// turn. Stock has two sibling setters: <c>101067af</c> takes those six fields, and <c>10106be2</c>
    /// takes six zeros; every control has one ctor per locator kind and only the **dynel** one passes the
    /// fields, which is why <see cref="EffectLocator.WithTemplate"/> ignores the other kinds.
    ///
    /// The list below is the RTTI-complete set: every call site of <c>101067af</c> and of its wrapper
    /// <c>_GfxControl_t::InitDynelTemplate</c> (<c>100d2d84</c>) was walked back to the vftable its ctor
    /// installs. Ported types that reach it directly are Flare (<c>100dd740</c>), FlareAlt
    /// (<c>100deb41</c>), Nano0 (<c>100e7502</c>), Nano1 (<c>100e8527</c>), Sparks (<c>100f1f9c</c>),
    /// BuffPlaceHolder (<c>100d5a5b</c>), Cord (<c>100d6524</c>), Fire (<c>100dc8f2</c>), Smoke
    /// (<c>100f103f</c>), Spiral (<c>100f5118</c>), Sprite (<c>100f68fd</c>) and VulcanRocks
    /// (<c>1010379e</c>); through InitDynelTemplate: Stars (<c>100f760a</c>), Suns (<c>100fd4c0</c>),
    /// BParticle (<c>1010ac6d</c>), BParticle2 (<c>1010c4ee</c>), EffectMesh (<c>1010d910</c>),
    /// GroundGrid (<c>1010ee15</c>), MParticle (<c>1011049f</c>), TParticle (<c>10112f18</c>),
    /// TParticle2 (<c>1011483a</c>), VolGrid (<c>101164c2</c>), SkyFlash (<c>100ef756</c>), Trail2
    /// (<c>101153f0</c>) and Spiral2 (<c>1011227e</c>).
    ///
    /// Not here, deliberately: **Electra**, whose three ctors call neither setter, so stock never gives it
    /// a template; and **Spell1**, whose ctor (<c>100f333b</c>) builds three locators and passes the
    /// fields to only one of them (<c>100f3533</c>, on a different dynel argument than the other two) —
    /// the port has one locator, so applying it to all three would be wrong (Docs §9). Other types keep
    /// the bare attach; for some, fields 1-6 mean something else (Meta's child ids, a Billboard's scales).
    /// </summary>
    static bool TakesLocatorTemplate(int typeCode)
    {
        switch (typeCode)
        {
            case EffectTypeTags.BuffPlaceHolder:
            case EffectTypeTags.Cord:
            case EffectTypeTags.Fire:
            case EffectTypeTags.Flare:
            case EffectTypeTags.FlareAlt:
            case EffectTypeTags.Nano0:
            case EffectTypeTags.Nano1:
            case EffectTypeTags.Nano3:
            case EffectTypeTags.Sparks:
            case EffectTypeTags.Smoke:
            case EffectTypeTags.Spiral:
            case EffectTypeTags.Spiral2:
            case EffectTypeTags.Beam:
            case EffectTypeTags.CrazyCone:
            case EffectTypeTags.GroundRing:
            case EffectTypeTags.Mesh:
            case EffectTypeTags.Stars:
            case EffectTypeTags.Suns:
            case EffectTypeTags.BParticle:
            case EffectTypeTags.BParticle2:
            case EffectTypeTags.EffectMesh:
            case EffectTypeTags.GroundGrid:
            case EffectTypeTags.GroundShake:
            case EffectTypeTags.MParticle:
            case EffectTypeTags.TParticle:
            case EffectTypeTags.TParticle2:
            case EffectTypeTags.VulcanRocks:
            case EffectTypeTags.VolGrid:
            case EffectTypeTags.Sprite:
            case EffectTypeTags.SkyFlash:
            case EffectTypeTags.Trail2:
                return true;
            default:
                return false;
        }
    }

    static EffectLocator ApplyTemplateAttach(
        GfxTweakRecord record, EffectLocator locator, int attachOverride)
    {
        if (attachOverride != 0 || record == null || locator == null || record.FieldCount <= 7)
            return locator;

        // Stock takes any non-zero field 7 as the attach; one it can't resolve leaves the mesh frame.
        int templateAttach = record.FieldInt(7, 0);
        if (templateAttach == 0)
            return locator;

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
    /// Stock builds a Tracer5 only from a hit location (<c>CreateGfxControl(id, hitLoc)</c>,
    /// <c>10100b82</c>), reading its start and end once.
    /// </summary>
    GfxControl CreateTracer5(GfxTweakRecord record, EffectLocator locator)
    {
        if (locator == null
            || !locator.TryGetHitLocation(out EffectHitLocation hitLoc)
            || !hitLoc.TryGetEndpoints(out Vector3 start, out Vector3 end))
        {
            return new GfxControlUnsupported(record, locator);
        }

        // Build 101004b4: GfxVisualCord4(material from field 9, null, additive, life-v).
        int materialIndex = ResolveMaterialIndex(record, defaultIndex: 15, preferredField: 9);
        EffectMaterialTable.Slot slot = EffectMaterialTable.Get(materialIndex);
        Texture2D texture = slot.IsUntextured ? WhiteTexture : GetMaterialTexture(materialIndex, slot);
        return new GfxControlTracer5(record, EffectLocator.WorldPoint(start, Quaternion.identity), start, end, texture);
    }

    /// <summary>
    /// Stock builds a Tracer6 the same way, from the tracer factory's 0x402 case (<c>100d18e6</c>).
    /// Its two visuals take different materials: the build hardcodes 15 for the three
    /// <c>GfxVisualCord4</c> ribbons (<c>10100e01</c>) and uses field 9 only for the sprite trail
    /// (<c>10100f87</c>), whose atlas it then forces to 8 × 8.
    /// </summary>
    GfxControl CreateTracer6(GfxTweakRecord record, EffectLocator locator)
    {
        if (locator == null
            || !locator.TryGetHitLocation(out EffectHitLocation hitLoc)
            || !hitLoc.TryGetEndpoints(out Vector3 start, out Vector3 end))
        {
            return new GfxControlUnsupported(record, locator);
        }

        EffectMaterialTable.Slot ribbonSlot = EffectMaterialTable.Get(Tracer6Sim.RibbonMaterialIndex);
        Texture2D ribbonTexture = ribbonSlot.IsUntextured
            ? WhiteTexture
            : GetMaterialTexture(Tracer6Sim.RibbonMaterialIndex, ribbonSlot);

        int spriteIndex = ResolveMaterialIndex(record, defaultIndex: 15, preferredField: 9);
        EffectMaterialTable.Slot spriteSlot = EffectMaterialTable.Get(spriteIndex);
        Texture2D spriteTexture = spriteSlot.IsUntextured
            ? WhiteTexture
            : GetMaterialTexture(spriteIndex, spriteSlot);

        return new GfxControlTracer6(
            record, EffectLocator.WorldPoint(start, Quaternion.identity), start, end,
            ribbonTexture, spriteTexture);
    }

    /// <summary>
    /// Stock Tracer3 from a hit location (<c>CreateGfxControl(id, hitLoc)</c>, case <c>100d1b07</c>, ctor
    /// <c>100ff996</c>): the endpoints are read once; the child is owned by the tracer.
    /// </summary>
    GfxControl CreateTracer3(GfxTweakRecord record, EffectLocator locator)
    {
        if (locator == null
            || !locator.TryGetHitLocation(out EffectHitLocation hitLoc)
            || !hitLoc.TryGetEndpoints(out Vector3 start, out Vector3 end))
        {
            return new GfxControlUnsupported(record, locator);
        }

        return new GfxControlTracer3(record, EffectLocator.WorldPoint(start, Quaternion.identity), start, end, this);
    }

    /// <summary>
    /// Stock ShockWave (init <c>100eeacb</c>): GfxVisualGroundRing(material from field 9) per ring and
    /// GfxVisualCone(material from field 25) per cone. Both textures wrap (D3D's default).
    /// </summary>
    GfxControl CreateShockWave(GfxTweakRecord record, EffectLocator locator)
    {
        return new GfxControlShockWave(
            record, locator, WrappedTexture(record, field: 9), WrappedTexture(record, field: 25));
    }

    /// <summary>A material texture drawn clamped (D3DTSS_ADDRESS 3), as GfxVisualTrail2 sets it (<c>1002c63e</c>).</summary>
    Texture2D ClampedTexture(GfxTweakRecord record, int field)
    {
        int materialIndex = ResolveMaterialIndex(record, defaultIndex: 0, preferredField: field);
        EffectMaterialTable.Slot slot = EffectMaterialTable.Get(materialIndex);
        Texture2D texture = slot.IsUntextured ? WhiteTexture : GetMaterialTexture(materialIndex, slot);
        if (texture != null && texture != WhiteTexture)
            texture.wrapMode = TextureWrapMode.Clamp;
        return texture;
    }

    Texture2D WrappedTexture(GfxTweakRecord record, int field)
    {
        int materialIndex = ResolveMaterialIndex(record, defaultIndex: 0, preferredField: field);
        EffectMaterialTable.Slot slot = EffectMaterialTable.Get(materialIndex);
        Texture2D texture = slot.IsUntextured ? WhiteTexture : GetMaterialTexture(materialIndex, slot);
        if (texture != null && texture != WhiteTexture)
            texture.wrapMode = TextureWrapMode.Repeat;
        return texture;
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

    /// <summary>
    /// Shield's visual (100edf78): GfxVisualShield over the host's CAT mesh with the field 9 material,
    /// whose texture tiles and scrolls (D3D wraps by default).
    /// </summary>
    GfxControl CreateShield(GfxTweakRecord record, EffectLocator locator)
    {
        int materialIndex = ResolveMaterialIndex(record, defaultIndex: 13, preferredField: 9);
        EffectMaterialTable.Slot slot = EffectMaterialTable.Get(materialIndex);
        Texture2D texture = slot.IsUntextured ? WhiteTexture : GetMaterialTexture(materialIndex, slot);
        if (texture != null)
            texture.wrapMode = TextureWrapMode.Repeat;
        return new GfxControlShield(record, locator, texture);
    }

    /// <summary>Loader <c>1011148b</c>: Shield2's material is field 12 (the init hands it to the visual at <c>10111743</c>).</summary>
    GfxControl CreateShield2(GfxTweakRecord record, EffectLocator locator)
    {
        int materialIndex = ResolveMaterialIndex(record, defaultIndex: 13, preferredField: 12);
        EffectMaterialTable.Slot slot = EffectMaterialTable.Get(materialIndex);
        Texture2D texture = slot.IsUntextured ? WhiteTexture : GetMaterialTexture(materialIndex, slot);
        if (texture != null)
            texture.wrapMode = TextureWrapMode.Repeat;
        return new GfxControlShield2(record, locator, texture);
    }

    /// <summary>
    /// Suns' visual (around 100fd276): GfxVisualSol(material from field 9, 0, additive), sized by the
    /// material's columns and rows. Type 4 reads its hit location on every spawn.
    /// </summary>
    GfxControl CreateSuns(GfxTweakRecord record, EffectLocator locator)
    {
        int materialIndex = ResolveMaterialIndex(record, defaultIndex: 2, preferredField: 9);
        EffectMaterialTable.Slot slot = EffectMaterialTable.Get(materialIndex);
        Texture2D texture = slot.IsUntextured ? WhiteTexture : GetMaterialTexture(materialIndex, slot);
        EffectHitLocation hitLoc = null;
        locator?.TryGetHitLocation(out hitLoc);
        return new GfxControlSuns(record, locator, hitLoc, texture, _atlasFrames, slot.Cols, slot.Rows);
    }

    /// <summary>
    /// Tracers stock builds from its own data and lets run their course. Stock's launch
    /// (<c>FUN_1004f989</c>) <b>never</b> sets a tracer's duration and never cuts it off when the hit
    /// lands, so only the port's own stand-ins need that treatment.
    ///
    /// This is deliberately a test for the three stand-in classes rather than a list of the ported
    /// ones. The list version went stale twice: it missed <see cref="GfxControlSequencer"/> (28 nanos
    /// use one as a tracer) and it missed Stars starType 28, whose head crosses by age / duration — an
    /// overridden duration made the snowball cross at a third of stock's speed and the cut-off stopped
    /// it 0.57 m short of the target. Ten more ported types reach a tracer stat and react to
    /// SetDuration (VolGrid, Shield, Deformer, Smoke, GroundShake, Flare, Electra, Highlight), so the
    /// same deviation was stretching or squashing all of them.
    ///
    /// Nothing runs away as a result: <see cref="GfxControl.WatchdogSeconds"/> still ends any control
    /// that outlives 60 s, and tracers are not buff trees, so none of them is exempt.
    /// </summary>
    static bool IsStockTracer(EffectHandle handle)
    {
        GfxControl control = handle?.Control;
        if (control == null)
            return false;

        // A composite is as stock-built as the children it has actually spawned.
        if (control is GfxControlMeta meta)
            return AllChildrenStock(meta.Children);
        if (control is GfxControlSequencer sequencer)
            return AllChildrenStock(sequencer.Children);

        return control is not GfxControlTracer
               && control is not GfxControlBillboard
               && control is not GfxControlUnsupported;
    }

    static bool AllChildrenStock(IEnumerable<EffectHandle> children)
    {
        bool any = false;
        foreach (EffectHandle child in children)
        {
            if (child == null)
                continue;
            if (!IsStockTracer(child))
                return false;
            any = true;
        }
        return any;
    }

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

    /// <summary>
    /// Stock Sprite (init <c>100f64a7</c>): GfxVisualSprite2 or 3 with the material from field 9, starting
    /// on the material's first frame (<c>100cdfa1</c>) and running to its last (<c>100cdfb7</c>).
    /// </summary>
    GfxControl CreateSprite(GfxTweakRecord record, EffectLocator locator)
    {
        int materialIndex = ResolveMaterialIndex(record, defaultIndex: 0, preferredField: 9);
        EffectMaterialTable.Slot slot = EffectMaterialTable.Get(materialIndex);
        Texture2D texture = slot.IsUntextured ? WhiteTexture : GetMaterialTexture(materialIndex, slot);
        return new GfxControlSprite(
            record, locator, texture, _atlasFrames, slot.Cols, slot.Rows, slot.FirstFrame, slot.LastFrame);
    }

    GfxControl CreateBillboard(GfxTweakRecord record, EffectLocator locator, Color tint)
    {
        int materialIndex = ResolveMaterialIndex(record, defaultIndex: 10, preferredField: 9);
        EffectMaterialTable.Slot slot = EffectMaterialTable.Get(materialIndex);
        Texture2D texture = slot.IsUntextured ? WhiteTexture : GetMaterialTexture(materialIndex, slot);
        // Flags bit 0x800 picks DESTBLEND=INVSRCALPHA over ONE, i.e. matte instead of glowing.
        bool additive = record == null || SpriteEmitterMath.IsAdditive(record.FieldInt(0, 0));
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

        // Stock's effect setup (100089f4) never sets D3DTSS_ADDRESSU/V, so every effect draw runs on
        // D3D's default, D3DTADDRESS_WRAP. BParticle mode 1 relies on it: its u is 1 - (u + offset),
        // which is negative for all but one particle, and clamping smears the edge column across the
        // quad instead of rolling the texture. AoImageTextureCache hands out Clamp, so this sets the
        // wrap on the ids the effect material table names. The cache is shared with VisualDynel and
        // AbiffLoader, so this does change those ids for them too; no armour or model texture is in
        // the material table today, and stock drew those with wrap as well.
        tex.wrapMode = TextureWrapMode.Repeat;
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

}
