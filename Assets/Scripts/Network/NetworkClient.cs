using System;
using System.Collections.Concurrent;
using AOSharp.Common.GameData;
using SmokeLounge.AOtomation.Messaging.Messages;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using SmokeLounge.AOtomation.Messaging.Messages.SystemMessages;
using UnityEngine;

public enum SessionPhase
{
    Disconnected,
    Connecting,
    Authenticating,
    EnteringZone,
    InPlay
}

public class Credentials
{
    public string Username { get; }
    public string Password { get; }

    public Credentials(string username, string password)
    {
        Username = username;
        Password = password;
    }
}

public class NetworkConfig
{
    public bool AutoReconnect = true;
    public int ReconnectDelayMs = 30000;
}

public class NetworkClient
{
    readonly NetworkConfig _config;
    readonly NetworkSession _session;
    readonly ConcurrentQueue<Action> _mainThreadActions = new ConcurrentQueue<Action>();

    Credentials _credentials;
    DimensionInfo _dimension;
    SessionPhase _phase = SessionPhase.Disconnected;
    bool _wantSession;
    bool _isFirstPlayshift = true;
    float _reconnectAt = -1f;

    public NetworkConfig Config => _config;
    public Credentials Credentials => _credentials;
    public DimensionInfo Dimension => _dimension;

    public SessionPhase Phase => _phase;
    public bool InPlay => _phase == SessionPhase.InPlay;
    public bool Connected => _session.Connected;

    public int LocalDynelId { get; internal set; }
    public int ServerId { get; internal set; }

    public event Action<SessionPhase> PhaseChanged;
    public event Action Disconnected;
    public event Action<bool> CharacterInPlay;
    public event Action<CharacterListMessage> CharacterListReceived;
    public event Action<LoginError> LoginFailed;
    public event Action<Message> MessageReceived;
    public event Action<int> PlayfieldAnarchyFReceived;
    public event Action<SimpleCharFullUpdateMessage> SimpleCharFullUpdateReceived;
    public event Action<FullCharacterMessage> FullCharacterReceived;
    public event Action<StatMessage> StatReceived;
    public event Action<CharDCMoveMessage> CharDCMoveReceived;
    public event Action<CharacterActionMessage> CharacterActionReceived;
    public event Action<FollowTargetMessage> FollowTargetReceived;
    public event Action<Identity> DynelDespawned;
    public event Action<AppearanceUpdateMessage> AppearanceUpdateReceived;
    public event Action<HealthDamageMessage> HealthDamageReceived;
    public event Action<AttackInfoMessage> AttackInfoReceived;

    public NetworkClient(NetworkConfig config = null)
    {
        _config = config ?? new NetworkConfig();
        _session = new NetworkSession(this);
    }

    public void Connect(Credentials credentials, string dimensionId)
    {
        Connect(credentials, DimensionCatalog.Get(dimensionId));
    }

    public void Connect(Credentials credentials, DimensionInfo dimension)
    {
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _dimension = dimension ?? throw new ArgumentNullException(nameof(dimension));
        _wantSession = true;
        _isFirstPlayshift = true;
        _reconnectAt = -1f;

        NetworkDebug.LogLoginConnect(_dimension, _credentials.Username);

        _session.ResetSession();
        _session.ConnectToLoginServer();
    }

    public void Disconnect()
    {
        _wantSession = false;
        _reconnectAt = -1f;

        bool alreadyDown = _phase == SessionPhase.Disconnected && !_session.Connected;
        _session.ResetSession();

        if (alreadyDown)
            return;

        SetPhase(SessionPhase.Disconnected);
        Disconnected?.Invoke();
    }

    public void AbandonReconnect()
    {
        _wantSession = false;
        _reconnectAt = -1f;
    }

    public void Update()
    {
        // Drain inbound packets before posted transport drops so LoginError
        // (and its soft-fail UI path) wins the race against a simultaneous close.
        _session.Update();

        while (_mainThreadActions.TryDequeue(out Action action))
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Debug.LogError($"[Network] Main-thread action failed: {e}");
            }
        }

        if (_reconnectAt > 0f && Time.realtimeSinceStartup >= _reconnectAt)
        {
            _reconnectAt = -1f;
            if (_wantSession && _credentials != null && _dimension != null)
            {
                Debug.Log("[Network] Auto-reconnecting...");
                _session.ResetSession();
                _session.ConnectToLoginServer();
            }
        }
    }

    public void Send(MessageBody body) => _session.Send(body);

    public void Send(Message message) => _session.Send(message);

    public void SelectCharacter(int characterId)
    {
        Send(new SelectCharacterMessage { CharacterId = characterId });
        LocalDynelId = characterId;
    }

    internal void Post(Action action) => _mainThreadActions.Enqueue(action);

    internal void SetPhase(SessionPhase phase)
    {
        if (_phase == phase)
            return;

        _phase = phase;
        Debug.Log($"[Network] Phase → {phase}");
        PhaseChanged?.Invoke(phase);
    }

    internal void OnLoginSocketReady()
    {
        NetworkDebug.LogLoginUserLogin(_credentials.Username, _dimension.ClientVersion);
        Send(new UserLoginMessage
        {
            UserName = _credentials.Username,
            ClientVersion = _dimension.ClientVersion
        });
    }

    internal void HandleSystemMessage(SystemMessage sysMsg)
    {
        switch (sysMsg.SystemMessageType)
        {
            case SystemMessageType.ServerSalt:
            {
                byte[] salt = ((ServerSaltMessage)sysMsg).ServerSalt;
                NetworkDebug.LogLoginServerSalt(salt);

                string challenge = LoginEncryption.MakeChallengeResponse(
                    _credentials,
                    salt,
                    _dimension.PublicKey);

                NetworkDebug.LogLoginCredentials(
                    _credentials.Username,
                    salt,
                    _dimension.PublicKey,
                    challenge,
                    _credentials.Password?.Length ?? 0);

                Send(new UserCredentialsMessage
                {
                    UserName = _credentials.Username,
                    Credentials = challenge
                });
                break;
            }

            case SystemMessageType.CharacterList:
                OnCharacterList((CharacterListMessage)sysMsg);
                break;
        }
    }

    void OnCharacterList(CharacterListMessage charList)
    {
        NetworkDebug.LogLoginCharacterList(charList.Characters?.Length ?? 0);
        CharacterListReceived?.Invoke(charList);
    }

    internal void OnFullCharacter(FullCharacterMessage msg)
    {
        Debug.Log($"[Network] FullCharacter → {msg.Identity.Type}:{msg.Identity.Instance}");
        FullCharacterReceived?.Invoke(msg);
    }

    internal void EnterPlay()
    {
        if (_phase == SessionPhase.InPlay)
            return;

        Send(new CharInPlayMessage());
        SetPhase(SessionPhase.InPlay);
        CharacterInPlay?.Invoke(_isFirstPlayshift);
        _isFirstPlayshift = false;
    }

    internal void OnPlayfieldAnarchyF(PlayfieldAnarchyFMessage msg)
    {
        int playfieldId = msg.PlayfieldId1.Instance;
        Debug.Log($"[Network] PlayfieldAnarchyF → {playfieldId}");
        PlayfieldAnarchyFReceived?.Invoke(playfieldId);
    }

    internal void OnSimpleCharFullUpdate(SimpleCharFullUpdateMessage msg)
    {
        Debug.Log($"[Network] SimpleCharFullUpdate → {msg.Identity.Type}:{msg.Identity.Instance} \"{msg.Name}\" @ ({msg.Position.X:F1}, {msg.Position.Y:F1}, {msg.Position.Z:F1})");
        SimpleCharFullUpdateReceived?.Invoke(msg);
    }

    internal void OnStat(StatMessage msg)
    {
        Debug.Log($"[Network] Stat → {msg.Identity.Type}:{msg.Identity.Instance} ({msg.Stats?.Length ?? 0} stats)");
        StatReceived?.Invoke(msg);
    }

    internal void OnCharDCMove(CharDCMoveMessage msg)
    {
        CharDCMoveReceived?.Invoke(msg);
    }

    internal void OnCharacterAction(CharacterActionMessage msg)
    {
        CharacterActionReceived?.Invoke(msg);
    }

    internal void OnFollowTarget(FollowTargetMessage msg)
    {
        FollowTargetReceived?.Invoke(msg);
    }

    internal void OnDynelDespawn(DespawnMessage msg)
    {
        Debug.Log($"[Network] Despawn → {msg.Identity.Type}:{msg.Identity.Instance}");
        DynelDespawned?.Invoke(msg.Identity);
    }

    internal void OnAppearanceUpdate(AppearanceUpdateMessage msg)
    {
        Debug.Log($"[Network] AppearanceUpdate → {msg.Identity.Type}:{msg.Identity.Instance} (textures={msg.Textures?.Length ?? 0}, meshes={msg.Meshes?.Length ?? 0})");
        AppearanceUpdateReceived?.Invoke(msg);
    }

    internal void OnHealthDamage(HealthDamageMessage msg)
    {
        HealthDamageReceived?.Invoke(msg);
    }

    internal void OnAttackInfo(AttackInfoMessage msg)
    {
        AttackInfoReceived?.Invoke(msg);
    }

    internal void RaiseMessageReceived(Message message) => MessageReceived?.Invoke(message);

    internal void RaiseLoginFailed(LoginError error) => LoginFailed?.Invoke(error);

    internal void HandleTransportDrop(bool unexpected)
    {
        bool alreadyDown = _phase == SessionPhase.Disconnected && !_session.Connected;
        _session.CloseSocket();

        if (alreadyDown)
            return;

        SetPhase(SessionPhase.Disconnected);
        Disconnected?.Invoke();

        if (!unexpected)
        {
            _wantSession = false;
            _reconnectAt = -1f;
            return;
        }

        if (_wantSession && _config.AutoReconnect)
        {
            _reconnectAt = Time.realtimeSinceStartup + _config.ReconnectDelayMs / 1000f;
            Debug.Log($"[Network] Will reconnect in {_config.ReconnectDelayMs}ms");
        }
    }
}
