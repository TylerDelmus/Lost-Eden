using System;
using System.Collections;
using System.Collections.Generic;
using AOSharp.Common.GameData;
using Reflex.Attributes;
using Reflex.Core;
using SmokeLounge.AOtomation.Messaging.GameData;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Serialization;
using BuffMessage = AOSharp.Common.SmokeLounge.AOtomation.Messaging.Messages.N3Messages.BuffMessage;
using Vector3 = UnityEngine.Vector3;

public class PlayfieldFactory : MonoBehaviour
{
    [SerializeField] RenderConfig _renderConfig;
    [FormerlySerializedAs("_dynelPrefab")]
    [SerializeField] Character _characterPrefab;

    [Inject] ResourceDatabase _resourceDatabase;
    [Inject] Container _container;
    [Inject] NetworkClient _networkClient;
    [Inject] PlayerController _playerController;
    [Inject] LoadingScreen _loadingScreen;
    [Inject] EffectHandler _effectHandler;
    [Inject] ItemTemplateCache _itemTemplates;

    readonly Dictionary<Identity, SimpleCharFullUpdateMessage> _pendingCharacters = new();

    Coroutine _loadRoutine;
    Transform _playfieldRoot;
    Playfield _current;
    bool _playfieldReady;
    HdrpEnvironmentApplicator _aoEnvironment;

    public bool NetworkDriven { get; set; }

    public event Action<int> PlayfieldReady;
    public event Action<Playfield> CurrentPlayfieldChanged;

    public Playfield Current => _current;

    public bool TryGetCharacter(Identity identity, out Character character)
    {
        character = null;
        return _current != null && _current.TryGetCharacter(identity, out character);
    }

    public bool TryGetDynel(Identity identity, out Dynel dynel)
    {
        dynel = null;
        return _current != null && _current.TryGetDynel(identity, out dynel);
    }

    void OnEnable()
    {
        _networkClient.PlayfieldAnarchyFReceived += OnNetworkPlayfieldReceived;
        _networkClient.SimpleCharFullUpdateReceived += OnSimpleCharFullUpdate;
        _networkClient.StatReceived += OnStat;
        _networkClient.CharDCMoveReceived += OnCharDCMove;
        _networkClient.CharacterActionReceived += OnCharacterAction;
        _networkClient.FollowTargetReceived += OnFollowTarget;
        _networkClient.DynelDespawned += OnDynelDespawn;
        _networkClient.AppearanceUpdateReceived += OnAppearanceUpdate;
        _networkClient.HealthDamageReceived += OnHealthDamage;
        _networkClient.AttackInfoReceived += OnAttackInfo;
        _networkClient.AttackReceived += OnAttack;
        _networkClient.StopFightReceived += OnStopFight;
        _networkClient.CastNanoSpellReceived += OnCastNanoSpell;
        _networkClient.BuffReceived += OnBuff;

        if (_playerController?.CameraController != null)
            _playerController.CameraController.TargetAttached += OnCameraTargetAttached;
    }

    void OnDisable()
    {
        _networkClient.PlayfieldAnarchyFReceived -= OnNetworkPlayfieldReceived;
        _networkClient.SimpleCharFullUpdateReceived -= OnSimpleCharFullUpdate;
        _networkClient.StatReceived -= OnStat;
        _networkClient.CharDCMoveReceived -= OnCharDCMove;
        _networkClient.CharacterActionReceived -= OnCharacterAction;
        _networkClient.FollowTargetReceived -= OnFollowTarget;
        _networkClient.DynelDespawned -= OnDynelDespawn;
        _networkClient.AppearanceUpdateReceived -= OnAppearanceUpdate;
        _networkClient.HealthDamageReceived -= OnHealthDamage;
        _networkClient.AttackInfoReceived -= OnAttackInfo;
        _networkClient.AttackReceived -= OnAttack;
        _networkClient.StopFightReceived -= OnStopFight;
        _networkClient.CastNanoSpellReceived -= OnCastNanoSpell;
        _networkClient.BuffReceived -= OnBuff;

        if (_playerController?.CameraController != null)
            _playerController.CameraController.TargetAttached -= OnCameraTargetAttached;
    }

    void OnCameraTargetAttached()
    {
        if (!NetworkDriven)
            return;

        _loadingScreen.HideFade();
    }

    void OnNetworkPlayfieldReceived(int zoneId)
    {
        if (!NetworkDriven)
            return;

        Load(zoneId);
    }

    void OnSimpleCharFullUpdate(SimpleCharFullUpdateMessage msg)
    {
        if (!NetworkDriven)
            return;

        // DEBUG: log movement status for local player / unknown movement bytes when present
        bool isLocalPlayer = msg.Identity.Instance == _networkClient.LocalDynelId;
        byte[] unkMovementStatus = msg.UnkMovementStatus;
        bool hasUnkMovementStatus = unkMovementStatus != null && unkMovementStatus.Length > 0;

        if (isLocalPlayer || hasUnkMovementStatus)
        {
            if (isLocalPlayer)
            {
                if (msg.MovementStatus.HasValue)
                {
                    CharMovementStatus ms = msg.MovementStatus.Value;
                    Debug.LogWarning(
                        $"[PlayfieldFactory] Local player SCFU MovementStatus: " +
                        $"ModeId={ms.ModeId}, FwdState={ms.FwdState}, FwdDir={ms.FwdDir}, " +
                        $"StrafeState={ms.StrafeState}, StrafeDir={ms.StrafeDir}, " +
                        $"ElevateState={ms.ElevateState}, ElevateDir={ms.ElevateDir}, " +
                        $"TurnState={ms.TurnState}, TurnDir={ms.TurnDir}, " +
                        $"JumpState={ms.JumpState}, LastSpeedMode={ms.LastSpeedMode}");
                }
                else
                {
                    Debug.LogError("[PlayfieldFactory] Local player SCFU MovementStatus: null");
                }
            }

            if (hasUnkMovementStatus)
            {
                Debug.LogError(
                    $"[PlayfieldFactory] SCFU UnkMovementStatus " +
                    $"({msg.Identity.Type}:{msg.Identity.Instance}, {unkMovementStatus.Length}): " +
                    $"[{string.Join(", ", unkMovementStatus)}]");
            }
        }
        // DEBUG END

        if (!_playfieldReady)
        {
            _pendingCharacters[msg.Identity] = msg;
            Debug.Log($"[PlayfieldFactory] SimpleCharFullUpdate queued (playfield not ready): {msg.Identity.Type}:{msg.Identity.Instance} \"{msg.Name}\" (pending={_pendingCharacters.Count})");
            return;
        }

        _current.SpawnDynel(msg);
        NotifyLocalCharacterSpawned(msg.Identity);
    }

    void OnStat(StatMessage msg)
    {
        if (!NetworkDriven || _current == null)
            return;

        _current.ApplyStat(msg);
    }

    void OnCharDCMove(CharDCMoveMessage msg)
    {
        if (!NetworkDriven || _current == null)
            return;

        // Local movement is predicted client-side; ignore our own echo.
        if (msg.Identity.Instance == _networkClient.LocalDynelId)
            return;

        _current.ApplyCharDCMove(msg);
    }

    void OnCharacterAction(CharacterActionMessage msg)
    {
        if (!NetworkDriven || _current == null || msg == null)
            return;

        int action = (int)msg.Action;
        bool combat = action == AnimKindIds.FightEnterAction
            || action == AnimKindIds.FightLeaveAction
            || action == AnimKindIds.AttackSwingAction;
        bool nanoFx = msg.Action == CharacterActionType.FinishNanoCasting
            || msg.Action == CharacterActionType.InterruptNanoCasting;

        // Local non-combat CharacterActions are usually client-predicted; still need FinishNanoCasting for hit FX.
        if (msg.Identity.Instance == _networkClient.LocalDynelId && !combat && !nanoFx)
            return;

        if (msg.Action == CharacterActionType.FinishNanoCasting)
        {
            OnFinishNanoCasting(msg);
            return;
        }

        if (msg.Action == CharacterActionType.InterruptNanoCasting)
        {
            OnInterruptNanoCasting(msg);
            return;
        }

        _current.ApplyCharacterAction(msg);
    }

    void OnInterruptNanoCasting(CharacterActionMessage msg)
    {
        if (!_current.TryGetCharacter(msg.Identity, out Character caster))
            return;

        caster.StopSpellCastAnim();
        _effectHandler?.CancelPendingTrace(caster.Identity.Instance);
        _current.ApplyCharacterAction(msg);
    }

    void OnFinishNanoCasting(CharacterActionMessage msg)
    {
        if (!_current.TryGetCharacter(msg.Identity, out Character caster))
        {
            Debug.LogWarning($"[Effects] FinishNanoCasting: caster not found {msg.Identity}");
            return;
        }

        // Often absent here; EffectHandler falls back to the target CastNanoSpell gave us.
        Character target = null;
        if (msg.Target.Instance != 0 && !_current.TryGetCharacter(msg.Target, out target))
            Debug.LogWarning($"[Effects] FinishNanoCasting: target {msg.Target} not in playfield.");

        int nanoId = msg.Parameter2;
        bool isLocal = caster.Identity.Instance == _networkClient.LocalDynelId;
        NanoSpell nano = nanoId != 0 && _itemTemplates != null ? _itemTemplates.GetNano(nanoId) : null;
        if (nano == null && nanoId != 0)
            Debug.LogWarning($"[Effects] FinishNanoCasting: nano template {nanoId} failed to load.");

        bool selfCast = msg.Target.Instance != 0
            ? msg.Target.Instance == caster.Identity.Instance
            : caster.SpellCastOnSelf;

        caster.PlaySpellCastReleaseAnim(selfCast);
        _effectHandler?.PlayNanoHit(caster, target, nanoId, isLocal, nano);
    }

    void OnCastNanoSpell(CastNanoSpellMessage msg)
    {
        if (!NetworkDriven || _current == null || msg == null)
            return;

        // Identity is the caster on stock CastNanoSpell; Caster is a secondary field when present.
        Identity casterId = msg.Identity.Instance != 0
            ? msg.Identity
            : msg.Caster;
        if (!_current.TryGetCharacter(casterId, out Character caster)
            && msg.Caster.Instance != 0
            && msg.Caster.Instance != casterId.Instance)
        {
            _current.TryGetCharacter(msg.Caster, out caster);
            casterId = msg.Caster;
        }

        if (caster == null)
        {
            Debug.LogWarning(
                $"[Effects] CastNanoSpell: caster not found identity={msg.Identity} caster={msg.Caster} nano={msg.NanoId}");
            return;
        }

        Character target = null;
        if (msg.Target.Instance != 0)
            _current.TryGetCharacter(msg.Target, out target);

        // No target means a self cast, as does a target that is the caster.
        caster.PlaySpellCastAnim(
            msg.Target.Instance == 0 || msg.Target.Instance == caster.Identity.Instance);

        int nanoId = msg.NanoId;
        bool isLocal = caster.Identity.Instance == _networkClient.LocalDynelId;
        NanoSpell nano = nanoId != 0 && _itemTemplates != null ? _itemTemplates.GetNano(nanoId) : null;
        if (nano == null && nanoId != 0)
            Debug.LogWarning($"[Effects] CastNanoSpell: nano template {nanoId} failed to load.");

        _effectHandler?.PlayNanoCast(caster, target, nanoId, isLocal, nano);
    }

    /// <summary>Buff landed: identity is the recipient, Buff is the nano that was applied.</summary>
    void OnBuff(BuffMessage msg)
    {
        if (!NetworkDriven || _current == null || msg == null)
            return;

        if (!_current.TryGetCharacter(msg.Identity, out Character target))
            return;

        int nanoId = msg.Buff.Instance;
        if (nanoId == 0)
            return;

        NanoSpell nano = _itemTemplates != null ? _itemTemplates.GetNano(nanoId) : null;
        if (nano == null)
        {
            Debug.LogWarning($"[Effects] Buff: nano template {msg.Buff.Type}:{nanoId} failed to load.");
            return;
        }

        bool isLocal = target.Identity.Instance == _networkClient.LocalDynelId;
        _effectHandler?.PlayNanoBuff(target, nanoId, isLocal, nano);
    }

    void OnAttackInfo(AttackInfoMessage msg)
    {
        if (!NetworkDriven || _current == null)
            return;

        _current.ApplyAttackInfo(msg);
    }

    void OnAttack(AttackMessage msg)
    {
        if (!NetworkDriven || _current == null)
            return;

        _current.ApplyAttack(msg);
    }

    void OnStopFight(StopFightMessage msg)
    {
        if (!NetworkDriven || _current == null)
            return;

        _current.ApplyStopFight(msg);
    }

    void OnFollowTarget(FollowTargetMessage msg)
    {
        if (!NetworkDriven || _current == null)
            return;

        _current.ApplyFollowTarget(msg);
    }

    void OnDynelDespawn(Identity identity)
    {
        if (!NetworkDriven)
            return;

        _pendingCharacters.Remove(identity);

        if (_current == null)
            return;

        _current.DespawnDynel(identity);
    }

    void OnAppearanceUpdate(AppearanceUpdateMessage msg)
    {
        if (!NetworkDriven || _current == null)
            return;

        _current.ApplyAppearanceUpdate(msg);
    }

    void OnHealthDamage(HealthDamageMessage msg)
    {
        if (!NetworkDriven || _current == null)
            return;

        _current.ApplyHealthDamage(msg);
    }

    public void Load(int zoneId)
    {
        if (_loadRoutine != null)
            StopCoroutine(_loadRoutine);

        _loadRoutine = StartCoroutine(LoadWithUnloadRoutine(zoneId));
    }

    public void Unload()
    {
        if (_loadRoutine != null)
            StopCoroutine(_loadRoutine);

        _loadRoutine = StartCoroutine(UnloadOnlyRoutine());
    }

    IEnumerator UnloadOnlyRoutine()
    {
        yield return UnloadRoutine();
        _loadRoutine = null;
    }

    IEnumerator LoadWithUnloadRoutine(int zoneId)
    {
        if (NetworkDriven)
            _loadingScreen.Show("Loading zone...", LoadingScreenKind.Login);

        yield return UnloadRoutine();
        yield return LoadRoutine(zoneId);
        _loadRoutine = null;
    }

    IEnumerator UnloadRoutine()
    {
        _playfieldReady = false;
        _aoEnvironment?.Clear();

        if (_playfieldRoot == null)
            yield break;

        _pendingCharacters.Clear();
        _playerController?.ClearPendingFullCharacter();
        Destroy(_playfieldRoot.gameObject);
        _playfieldRoot = null;
        _current = null;
        CurrentPlayfieldChanged?.Invoke(null);
        PlayfieldTweakCatalog.ClearCache();
        SkippedStatelsCatalog.ClearCache();
        StatelLightsCatalog.ClearCache();
        yield return null;
    }

    IEnumerator LoadRoutine(int zoneId)
    {
        var holder = new GameObject($"PlayfieldLoad_{zoneId}");
        holder.transform.SetParent(transform, false);
        _playfieldRoot = holder.transform;

        _current = holder.AddComponent<Playfield>();
        _current.Init(zoneId, _characterPrefab, _container);
        CurrentPlayfieldChanged?.Invoke(_current);

        var terrainParser = new TerrainParser(_resourceDatabase, _renderConfig);
        yield return terrainParser.BuildCoroutine(zoneId, _playfieldRoot);

        var waterBuilder = new PlayfieldWaterBuilder(_resourceDatabase, _renderConfig);
        yield return waterBuilder.BuildCoroutine(zoneId, _playfieldRoot);

        var abiffMaterials = new AbiffMaterialFactory(_resourceDatabase);
        var statelParser = new StatelParser(_resourceDatabase, _renderConfig, abiffMaterials);
        yield return statelParser.BuildCoroutine(zoneId, _playfieldRoot);

        var grassBuilder = new PlayfieldGrassBuilder(_resourceDatabase, _renderConfig);
        yield return grassBuilder.BuildCoroutine(zoneId, _playfieldRoot);

        TryApplyAoEnvironment(zoneId, abiffMaterials);

        AttachLocality(zoneId);

        if (_renderConfig == null || _renderConfig.UseReflectionProbe)
            yield return BakeReflectionProbesRoutine();

        _playfieldReady = true;
        FlushPendingCharacters();
        Debug.Log($"[PlayfieldFactory] Playfield ready for dynels (id={zoneId}, prefab={(_characterPrefab != null ? _characterPrefab.name : "MISSING")})");
        PlayfieldReady?.Invoke(zoneId);

        // Network-driven loading stays up until the local player is possessed and the
        // camera snaps (see PlayerController FullCharacter bind). Zone geometry alone is too early.
    }

    public void PrioritizeLocalityAround(Vector3 position)
    {
        PlayfieldLocality locality = _playfieldRoot != null
            ? _playfieldRoot.GetComponent<PlayfieldLocality>()
            : null;
        locality?.PrioritizeAround(position);
    }

    IEnumerator BakeReflectionProbesRoutine()
    {
        // Let new renderers register with HDRP before capturing.
        yield return null;

        HDAdditionalReflectionData[] probes =
            FindObjectsByType<HDAdditionalReflectionData>(FindObjectsSortMode.None);
        if (probes == null || probes.Length == 0)
        {
            Debug.LogWarning("[PlayfieldFactory] No HD reflection probes found to bake.");
            yield break;
        }

        Vector3 capturePos = ResolveReflectionProbeCapturePosition();
        int requested = 0;
        for (int i = 0; i < probes.Length; i++)
        {
            HDAdditionalReflectionData probe = probes[i];
            if (probe == null || !probe.isActiveAndEnabled)
                continue;

            probe.mode = ProbeSettings.Mode.Realtime;
            probe.realtimeMode = ProbeSettings.RealtimeMode.OnDemand;
            probe.transform.position = capturePos;
            probe.RequestRenderNextUpdate();
            requested++;
        }

        Debug.Log($"[PlayfieldFactory] Requested reflection probe bake ({requested} probe(s) at {capturePos}).");
    }

    Vector3 ResolveReflectionProbeCapturePosition()
    {
        if (_playerController != null && _playerController.TryGetLocalPlayer(out Character localPlayer)
            && localPlayer != null)
            return localPlayer.transform.position;

        if (TryGetPlayfieldRendererCenter(out Vector3 center))
            return center;

        Camera cam = Camera.main;
        if (cam != null)
            return cam.transform.position;

        if (_playfieldRoot != null)
            return _playfieldRoot.position;

        return Vector3.zero;
    }

    bool TryGetPlayfieldRendererCenter(out Vector3 center)
    {
        center = Vector3.zero;
        if (_playfieldRoot == null)
            return false;

        var renderers = _playfieldRoot.GetComponentsInChildren<Renderer>();
        if (renderers == null || renderers.Length == 0)
            return false;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                bounds.Encapsulate(renderers[i].bounds);
        }

        center = bounds.center;
        return true;
    }

    void FlushPendingCharacters()
    {
        if (_current == null || _pendingCharacters.Count == 0)
            return;

        Debug.Log($"[PlayfieldFactory] Flushing {_pendingCharacters.Count} queued SimpleCharFullUpdate(s)");
        foreach (SimpleCharFullUpdateMessage msg in _pendingCharacters.Values)
        {
            try
            {
                _current.SpawnDynel(msg);
                NotifyLocalCharacterSpawned(msg.Identity);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PlayfieldFactory] Failed to spawn dynel {msg.Identity.Type}:{msg.Identity.Instance} \"{msg.Name}\": {ex}");
            }
        }

        _pendingCharacters.Clear();
    }

    void NotifyLocalCharacterSpawned(Identity identity)
    {
        if (identity != LocalPlayerIdentity())
            return;

        _playerController?.TryBindPendingFullCharacter();
    }

    void TryApplyAoEnvironment(int playfieldId, AbiffMaterialFactory abiffMaterials)
    {
        if (_renderConfig == null || !_renderConfig.ApplyAoPlayfieldTweaks)
        {
            _aoEnvironment?.Clear();
            return;
        }

        try
        {
            _aoEnvironment?.Clear();
            var abiffLoader = new AbiffLoader(_resourceDatabase, abiffMaterials);
            _aoEnvironment = new HdrpEnvironmentApplicator(_resourceDatabase, abiffLoader);
            _aoEnvironment.Apply(playfieldId, _renderConfig.ApplyAoSkyMeshes);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PlayfieldFactory] AO environment tweaks failed for {playfieldId}: {ex.Message}");
            _aoEnvironment?.Clear();
        }
    }

    void AttachLocality(int playfieldId)
    {
        if (_playfieldRoot == null)
            return;

        if (!PlayfieldLayoutFactory.TryCreate(_resourceDatabase, playfieldId, out IPlayfieldCellLayout layout))
        {
            Debug.LogWarning($"[PlayfieldFactory] Cell locality not attached for playfield {playfieldId}.");
            return;
        }

        var locality = _playfieldRoot.gameObject.AddComponent<PlayfieldLocality>();
        locality.Initialize(layout, _resourceDatabase, _playerController);
    }

    Identity LocalPlayerIdentity()
        => new Identity(IdentityType.SimpleChar, _networkClient.LocalDynelId);
}
