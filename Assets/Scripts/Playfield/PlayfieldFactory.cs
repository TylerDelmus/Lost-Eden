using System;
using System.Collections;
using System.Collections.Generic;
using AOSharp.Common.GameData;
using Reflex.Attributes;
using Reflex.Core;
using SmokeLounge.AOtomation.Messaging.GameData;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using UnityEngine;
using UnityEngine.Serialization;

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

    readonly Dictionary<Identity, SimpleCharFullUpdateMessage> _pendingCharacters = new();
    FullCharacterMessage _pendingFullCharacter;

    Coroutine _loadRoutine;
    Transform _playfieldRoot;
    Playfield _current;
    bool _playfieldReady;

    public bool NetworkDriven { get; set; }

    public event Action<int> PlayfieldReady;

    void OnEnable()
    {
        _networkClient.PlayfieldAnarchyFReceived += OnNetworkPlayfieldReceived;
        _networkClient.SimpleCharFullUpdateReceived += OnSimpleCharFullUpdate;
        _networkClient.FullCharacterReceived += OnFullCharacter;
        _networkClient.StatReceived += OnStat;
        _networkClient.CharDCMoveReceived += OnCharDCMove;
        _networkClient.CharacterActionReceived += OnCharacterAction;
        _networkClient.FollowTargetReceived += OnFollowTarget;
        _networkClient.DynelDespawned += OnDynelDespawn;
        _networkClient.AppearanceUpdateReceived += OnAppearanceUpdate;
    }

    void OnDisable()
    {
        _networkClient.PlayfieldAnarchyFReceived -= OnNetworkPlayfieldReceived;
        _networkClient.SimpleCharFullUpdateReceived -= OnSimpleCharFullUpdate;
        _networkClient.FullCharacterReceived -= OnFullCharacter;
        _networkClient.StatReceived -= OnStat;
        _networkClient.CharDCMoveReceived -= OnCharDCMove;
        _networkClient.CharacterActionReceived -= OnCharacterAction;
        _networkClient.FollowTargetReceived -= OnFollowTarget;
        _networkClient.DynelDespawned -= OnDynelDespawn;
        _networkClient.AppearanceUpdateReceived -= OnAppearanceUpdate;
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
        TryApplyPendingFullCharacter(msg.Identity);
    }

    void OnFullCharacter(FullCharacterMessage msg)
    {
        if (!NetworkDriven)
            return;

        _pendingFullCharacter = msg;
        Debug.Log($"[PlayfieldFactory] FullCharacter received (awaiting local SCFU if needed): {msg.Identity.Type}:{msg.Identity.Instance}");
        TryApplyPendingFullCharacter(LocalPlayerIdentity());
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
        if (!NetworkDriven || _current == null)
            return;

        if (msg.Identity.Instance == _networkClient.LocalDynelId)
            return;

        _current.ApplyCharacterAction(msg);
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

        if (_playfieldRoot == null)
            yield break;

        _pendingCharacters.Clear();
        _pendingFullCharacter = null;
        Destroy(_playfieldRoot.gameObject);
        _playfieldRoot = null;
        _current = null;
        PlayfieldTweakCatalog.ClearCache();
        yield return null;
    }

    IEnumerator LoadRoutine(int zoneId)
    {
        var holder = new GameObject($"PlayfieldLoad_{zoneId}");
        holder.transform.SetParent(transform, false);
        _playfieldRoot = holder.transform;

        _current = holder.AddComponent<Playfield>();
        _current.Init(zoneId, _characterPrefab, _container);

        var terrainParser = new TerrainParser(_resourceDatabase, _renderConfig);
        yield return terrainParser.BuildCoroutine(zoneId, _playfieldRoot);

        var waterBuilder = new PlayfieldWaterBuilder(_resourceDatabase, _renderConfig);
        yield return waterBuilder.BuildCoroutine(zoneId, _playfieldRoot);

        var abiffMaterials = new AbiffMaterialFactory(_resourceDatabase);
        var statelParser = new StatelParser(_resourceDatabase, _renderConfig, abiffMaterials);
        yield return statelParser.BuildCoroutine(zoneId, _playfieldRoot);

        AttachLocality(zoneId);

        _playfieldReady = true;
        FlushPendingCharacters();
        Debug.Log($"[PlayfieldFactory] Playfield ready for dynels (id={zoneId}, prefab={(_characterPrefab != null ? _characterPrefab.name : "MISSING")})");
        PlayfieldReady?.Invoke(zoneId);

        // Always dismiss loading UI once zone geometry is ready, even if dynel
        // spawning hit recoverable errors (otherwise builds can soft-lock).
        if (NetworkDriven)
            _loadingScreen.HideFade();
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
                TryApplyPendingFullCharacter(msg.Identity);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PlayfieldFactory] Failed to spawn dynel {msg.Identity.Type}:{msg.Identity.Instance} \"{msg.Name}\": {ex}");
            }
        }

        _pendingCharacters.Clear();
    }

    void TryApplyPendingFullCharacter(Identity identity)
    {
        if (_pendingFullCharacter == null || _current == null || identity != LocalPlayerIdentity())
            return;

        if (!_current.TryGetCharacter(identity, out Character localPlayer))
            return;

        try
        {
            ApplyFullCharacter(localPlayer, _pendingFullCharacter);
            _pendingFullCharacter = null;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[PlayfieldFactory] Failed to apply FullCharacter for local player: {ex}");
        }
    }

    void ApplyFullCharacter(Character localPlayer, FullCharacterMessage msg)
    {
        localPlayer.Apply(msg);
        _playerController.SetLocalPlayer(localPlayer);
        _networkClient.EnterPlay();
        Debug.Log($"[PlayfieldFactory] Local player set from FullCharacter: {localPlayer.Identity.Type}:{localPlayer.Identity.Instance} \"{localPlayer.Name}\"");
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
