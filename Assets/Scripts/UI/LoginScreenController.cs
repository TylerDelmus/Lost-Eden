using System.Collections.Generic;
using System.IO;
using AOSharp.Common.GameData;
using Reflex.Attributes;
using SmokeLounge.AOtomation.Messaging.Messages.SystemMessages;
using UnityEngine;

public enum LoginScreenState
{
    AwaitingAoPath,
    BootLoading,
    LoginBackdrop,
    Authenticating,
    CharacterSelect,
    EnteringGame,
    InGame,
    Reconnecting
}

[DisallowMultipleComponent]
public class LoginScreenController : MonoBehaviour
{
    [Inject] NetworkClient _networkClient;
    [Inject] PlayfieldFactory _playfieldFactory;
    [Inject] LoadingScreen _loadingScreen;
    [Inject] PlayerController _playerController;
    [Inject] ResourceDatabase _resourceDatabase;
    [Inject] WorldRouter _worldRouter;

    [SerializeField] LoginScreenView _loginView;

    const float AuthTimeoutSeconds = 30f;
    const float ReconnectDelaySeconds = 3f;
    const int MaxReconnectAttempts = 3;
    const string DefaultBrowseHint = @"C:\Program Files (x86)\Steam\steamapps\common\Anarchy Online";


    LoginScreenState _state = LoginScreenState.BootLoading;

    IReadOnlyList<DimensionInfo> _dimensions;
    bool _awaitingPlayfieldReady;
    bool _ignoreNextDisconnect;
    string _pendingLoginStatus;
    float _authTimeoutAt = -1f;

    // Dropped mid-game: log back in with the same credentials and re-enter this character.
    int _reconnectCharacterId;
    int _reconnectAttempt;
    float _reconnectAt = -1f;
    bool _abortingReconnectAttempt;

    void Awake()
    {
        _loginView ??= UserInterface.FindOrCreateMenuView<LoginScreenView>(transform, "LoginMenu");
    }

    void Start()
    {
        if (_loginView == null)
        {
            Debug.LogError("[LoginScreen] Missing LoginScreenView.");
            return;
        }

        // The view binds its elements from PanelRenderer's reload callback, which lands
        // after Start, so the whole boot sequence waits for it.
        _loginView.WhenReady(OnViewReady);
    }

    void OnViewReady()
    {
        if (!ValidateView())
            return;

        _dimensions = DimensionCatalog.All;
        _loginView.PopulateDimensions(_dimensions);
        RestoreFormDefaults();

        _playfieldFactory.NetworkDriven = false;
        _loginView.SetConnectHandler(OnConnectClicked);
        _loginView.SetBackHandler(OnBackFromCharacterSelect);
        _loginView.SetBrowseHandler(OnBrowseAoPathClicked);
        _loginView.SetAoPathConfirmHandler(OnAoPathConfirmClicked);

        if (!EnsureResourceDatabase())
        {
            BeginAoPathSetup();
            return;
        }

        BeginBootLoading();
    }

    bool ValidateView()
    {
        if (_loginView == null)
        {
            Debug.LogError("[LoginScreen] Missing LoginScreenView.");
            return false;
        }

        if (!_loginView.IsReady)
        {
            Debug.LogError("[LoginScreen] LoginScreenView failed to load UI/LoginScreen from Resources.");
            return false;
        }

        if (_loadingScreen == null || !_loadingScreen.IsReady)
        {
            Debug.LogError("[LoginScreen] LoadingScreen is not available. Ensure LoadingScreenView is under SceneScope.");
            return false;
        }

        return true;
    }

    void BeginAoPathSetup()
    {
        _state = LoginScreenState.AwaitingAoPath;
        _loadingScreen.Hide();
        string saved = AoInstall.Path;
        _loginView.ShowAoPathSetup(string.IsNullOrWhiteSpace(saved) ? string.Empty : saved);
        _loginView.SetAoPathStatus("Anarchy Online install path is required.");
    }

    /// <summary>
    /// Shows the form straight away and loads the backdrop behind it. The backdrop used to
    /// gate the form, which meant ~10s of playfield streaming (2.6M grass instances) before
    /// anyone could type a username — there is no reason to load a playfield to log in.
    /// </summary>
    void BeginBootLoading()
    {
        _state = LoginScreenState.LoginBackdrop;
        _loginView.ShowLoginForm();
        _loginView.SetFormInteractable(true);
        _loadingScreen.Hide();

        // The login backdrop is LoginWorld's job now, not a streamed playfield.
        _worldRouter?.LoginWorld?.SetStage(0);
    }

    bool EnsureResourceDatabase()
    {
        if (_resourceDatabase != null && _resourceDatabase.IsInitialized)
            return true;

        string path = AoInstall.Path;
        if (!AoInstallPath.IsValid(path))
            return false;

        try
        {
            _resourceDatabase.Initialize(path);
            UvgaTextureSource.RaiseChanged();
            return true;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[LoginScreen] Failed to open AO database at '{path}': {ex.Message}");
            return false;
        }
    }

    void OnBrowseAoPathClicked()
    {
        if (_state != LoginScreenState.AwaitingAoPath)
            return;

        string current = _loginView.AoPathField.Value;
        string initial = Directory.Exists(current) ? current : DefaultBrowseHint;

        if (!NativeFolderDialog.TryPickFolder("Select Anarchy Online Folder", initial, out string selected))
            return;

        _loginView.AoPathField.Value = selected;
        _loginView.SetAoPathStatus(string.Empty);
    }

    void OnAoPathConfirmClicked()
    {
        if (_state != LoginScreenState.AwaitingAoPath)
            return;

        string path = AoInstallPath.Normalize(_loginView.AoPathField.Value);
        if (!AoInstallPath.IsValid(path))
        {
            _loginView.SetAoPathStatus("Select a valid Anarchy Online install (must contain cd_image/data/db).");
            return;
        }

        try
        {
            _resourceDatabase.Initialize(path);
        }
        catch (System.Exception ex)
        {
            _loginView.SetAoPathStatus($"Failed to open database: {ex.Message}");
            return;
        }

        AoInstall.Save(path);
        _loginView.AoPathField.Value = path;
        UvgaTextureSource.RaiseChanged();
        BeginBootLoading();
    }

    void Update()
    {
        _networkClient.Update();
        TickAuthTimeout();
        TickReconnect();
    }

    void TickAuthTimeout()
    {
        if (_authTimeoutAt < 0f)
            return;

        if (_state != LoginScreenState.Authenticating && _state != LoginScreenState.Reconnecting)
            return;

        if (Time.realtimeSinceStartup < _authTimeoutAt)
            return;

        if (_state == LoginScreenState.Reconnecting)
        {
            AbortReconnectAttempt("login timed out");
            return;
        }

        string message = _networkClient.Phase == SessionPhase.Authenticating
            ? "Login timed out."
            : "Connection timed out.";
        FailAuthentication(message);
    }

    void BeginAuthTimeout()
    {
        _authTimeoutAt = Time.realtimeSinceStartup + AuthTimeoutSeconds;
    }

    void ClearAuthTimeout()
    {
        _authTimeoutAt = -1f;
    }

    void FailAuthentication(string message)
    {
        ClearAuthTimeout();
        _ignoreNextDisconnect = true;
        _networkClient.AbandonReconnect();
        _networkClient.Disconnect();
        _state = LoginScreenState.LoginBackdrop;
        _loginView.SetFormInteractable(true);
        _loginView.SetStatus(message);
    }

    void OnEnable()
    {
        _playfieldFactory.PlayfieldReady += OnPlayfieldReady;
        if (_playerController?.N3Camera != null)
            _playerController.N3Camera.TargetAttached += OnCameraTargetAttached;
        _networkClient.CharacterListReceived += OnCharacterListReceived;
        _networkClient.LoginFailed += OnLoginFailed;
        _networkClient.Disconnected += OnDisconnected;
        _networkClient.PhaseChanged += OnPhaseChanged;
    }

    void OnDisable()
    {
        _playfieldFactory.PlayfieldReady -= OnPlayfieldReady;
        if (_playerController?.N3Camera != null)
            _playerController.N3Camera.TargetAttached -= OnCameraTargetAttached;
        _networkClient.CharacterListReceived -= OnCharacterListReceived;
        _networkClient.LoginFailed -= OnLoginFailed;
        _networkClient.Disconnected -= OnDisconnected;
        _networkClient.PhaseChanged -= OnPhaseChanged;
    }

    void OnApplicationQuit()
    {
        ShutdownNetwork();
    }

    void OnDestroy()
    {
        // Safety net for editor play-mode stop / teardown if quit didn't run first.
        ShutdownNetwork();
    }

    void ShutdownNetwork()
    {
        if (_networkClient == null)
            return;

        _ignoreNextDisconnect = true;
        _networkClient.AbandonReconnect();
        _networkClient.Disconnect();
    }

    void OnPhaseChanged(SessionPhase phase)
    {
        if (_state != LoginScreenState.Authenticating)
            return;

        switch (phase)
        {
            case SessionPhase.Authenticating:
                _loginView.SetStatus("Authenticating...");
                break;
            case SessionPhase.EnteringZone:
                _loginView.SetStatus("Entering zone...");
                break;
        }
    }

    void RestoreFormDefaults()
    {
        _loginView.UsernameField.Value = LoginPreferences.GetUsername();

        string savedDimensionId = LoginPreferences.GetDimensionId();
        if (string.IsNullOrEmpty(savedDimensionId))
            return;

        for (int i = 0; i < _dimensions.Count; i++)
        {
            if (_dimensions[i].Id.Equals(savedDimensionId, System.StringComparison.OrdinalIgnoreCase))
            {
                _loginView.SelectDimension(i);
                break;
            }
        }
    }

    void OnConnectClicked()
    {
        if (_state != LoginScreenState.LoginBackdrop)
            return;

        string username = (_loginView.UsernameField.Value ?? string.Empty).Trim();
        string password = _loginView.PasswordField.Value;

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            _loginView.SetStatus("Username and password are required.");
            return;
        }

        int dimensionIndex = _loginView.GetSelectedDimensionIndex();
        if (dimensionIndex < 0 || dimensionIndex >= _dimensions.Count)
        {
            _loginView.SetStatus("Select a dimension.");
            return;
        }

        DimensionInfo dimension = _dimensions[dimensionIndex];
        LoginPreferences.Save(username, dimension.Id);

        _state = LoginScreenState.Authenticating;
        _loginView.SetStatus(string.Empty);
        _loginView.SetFormInteractable(false);
        _loginView.SetStatus("Connecting...");
        BeginAuthTimeout();

        _networkClient.Connect(new Credentials(username, password), dimension);
    }

    void OnCharacterListReceived(CharacterListMessage charList)
    {
        if (_state == LoginScreenState.Reconnecting)
        {
            ClearAuthTimeout();
            bool stillThere = false;
            if (charList.Characters != null)
                foreach (var character in charList.Characters)
                    stillThere |= character.Id == _reconnectCharacterId;

            if (!stillThere)
            {
                AbortReconnectAttempt($"character {_reconnectCharacterId} not in the character list");
                return;
            }

            Debug.Log($"[LoginScreen] Reconnected; re-entering the world as {_reconnectCharacterId}");
            EnterWorld(_reconnectCharacterId, "Reconnecting...");
            return;
        }

        if (_state != LoginScreenState.Authenticating)
            return;

        ClearAuthTimeout();

        if (charList.Characters == null || charList.Characters.Length == 0)
        {
            FailAuthentication("No characters found on this account.");
            return;
        }

        _state = LoginScreenState.CharacterSelect;
        _worldRouter?.LoginWorld?.SetStage(1);
        _loginView.SetStatus(string.Empty);
        _loginView.ShowCharacterSelect();
        _loginView.SetCharacterStatus("Choose a character to enter the world.");

        var entries = new List<(int id, string name)>(charList.Characters.Length);
        foreach (var character in charList.Characters)
            entries.Add((character.Id, character.Name));

        _loginView.RebuildCharacterButtons(entries, OnCharacterSelected);
    }

    void OnBackFromCharacterSelect()
    {
        if (_state != LoginScreenState.CharacterSelect)
            return;

        _ignoreNextDisconnect = true;
        _networkClient.AbandonReconnect();
        _networkClient.Disconnect();
        ReturnToCredentialsScreen();
    }

    void ReturnToCredentialsScreen()
    {
        _state = LoginScreenState.LoginBackdrop;
        _loginView.ClearCharacterButtons();
        _loginView.ShowLoginForm();
        _loginView.SetFormInteractable(true);
        _loginView.SetStatus(string.Empty);
        _loginView.SetCharacterStatus(string.Empty);
        _loginView.PasswordField.Value = string.Empty;
    }

    void OnCharacterSelected(int characterId)
    {
        if (_state != LoginScreenState.CharacterSelect)
            return;

        EnterWorld(characterId, "Entering world...");
    }

    void EnterWorld(int characterId, string loadingMessage)
    {
        _state = LoginScreenState.EnteringGame;
        _loginView.HideLoginUi();
        _loginView.ClearCharacterButtons();
        _loadingScreen.Show(loadingMessage, LoadingScreenKind.Login);
        _playfieldFactory.NetworkDriven = true;
        _playfieldFactory.Unload();
        _awaitingPlayfieldReady = true;
        _networkClient.SelectCharacter(characterId);
    }

    void OnPlayfieldReady(int zoneId)
    {
        // The form is already up; the backdrop just arrives behind it.
        if (_state == LoginScreenState.LoginBackdrop)
        {
            Debug.Log($"[LoginScreen] Boot backdrop ready (id={zoneId})");
            return;
        }

        if (_awaitingPlayfieldReady && _state == LoginScreenState.EnteringGame)
        {
            // Zone geometry is ready; keep loading until N3Camera resolves its target.
            Debug.Log($"[LoginScreen] Zone ready, awaiting camera attach (id={zoneId})");
        }
    }

    void OnCameraTargetAttached()
    {
        if (!_awaitingPlayfieldReady || _state != LoginScreenState.EnteringGame)
            return;

        _awaitingPlayfieldReady = false;
        _state = LoginScreenState.InGame;
        _reconnectAttempt = 0;
        _reconnectCharacterId = 0;

        // The login world has done its job; tear it down so its backdrop and camera stop
        // competing with the playfield. Previously it stayed alive behind the player.
        _worldRouter?.Leave();
        Debug.Log("[LoginScreen] Entered world (camera attached)");
    }


    void OnLoginFailed(LoginError error)
    {
        // The session closes the socket right after this; OnDisconnected schedules the retry.
        if (_state == LoginScreenState.Reconnecting)
        {
            Debug.LogWarning($"[LoginScreen] Reconnect attempt {_reconnectAttempt} refused: {error}");
            return;
        }

        // Authenticating, or soft-failed to the form after a raced socket close.
        if (_state != LoginScreenState.Authenticating && _state != LoginScreenState.LoginBackdrop)
            return;

        ClearAuthTimeout();
        _ignoreNextDisconnect = true;
        _networkClient.AbandonReconnect();
        _state = LoginScreenState.LoginBackdrop;
        _loginView.SetFormInteractable(true);
        _loginView.SetStatus(error.ToString());
    }

    void OnDisconnected()
    {
        if (_state == LoginScreenState.BootLoading || _state == LoginScreenState.AwaitingAoPath)
            return;

        if (_ignoreNextDisconnect)
        {
            _ignoreNextDisconnect = false;
            return;
        }

        if (_abortingReconnectAttempt)
            return;

        // Dropped in (or on the way into) the world: go back in rather than to the login form.
        if (_state == LoginScreenState.EnteringGame || _state == LoginScreenState.InGame
            || _state == LoginScreenState.Reconnecting)
        {
            if (!TryScheduleReconnect())
                ReturnToLoginAfterDrop();
            return;
        }

        // Bad password / early login drop: keep the form and backdrop, only show status.
        if (_state == LoginScreenState.Authenticating)
        {
            FailAuthentication("Disconnected");
            return;
        }

        ReturnToLoginAfterDrop();
    }

    bool TryScheduleReconnect()
    {
        int characterId = _reconnectCharacterId != 0 ? _reconnectCharacterId : _networkClient.LocalDynelId;
        if (characterId == 0 || _networkClient.Credentials == null || _networkClient.Dimension == null)
            return false;

        if (_reconnectAttempt >= MaxReconnectAttempts)
            return false;

        _reconnectAttempt++;
        _reconnectCharacterId = characterId;
        _state = LoginScreenState.Reconnecting;
        _awaitingPlayfieldReady = false;
        _networkClient.AbandonReconnect();
        ClearAuthTimeout();
        _reconnectAt = Time.realtimeSinceStartup + ReconnectDelaySeconds;

        // Drop the dead world now: a load still running would otherwise finish during the reconnect.
        _playfieldFactory.Unload();
        _loadingScreen.Show($"Connection lost. Reconnecting ({_reconnectAttempt}/{MaxReconnectAttempts})...", LoadingScreenKind.Login);
        Debug.LogWarning($"[LoginScreen] Connection lost; reconnect attempt {_reconnectAttempt}/{MaxReconnectAttempts} as {characterId} in {ReconnectDelaySeconds:F0}s");
        return true;
    }

    void TickReconnect()
    {
        if (_reconnectAt < 0f || Time.realtimeSinceStartup < _reconnectAt)
            return;

        _reconnectAt = -1f;
        if (_state != LoginScreenState.Reconnecting)
            return;

        BeginAuthTimeout();
        _networkClient.Connect(_networkClient.Credentials, _networkClient.Dimension);
    }

    /// <summary>Gives up on this attempt (closing whatever it opened) and tries again or goes to the form.</summary>
    void AbortReconnectAttempt(string reason)
    {
        Debug.LogWarning($"[LoginScreen] Reconnect attempt {_reconnectAttempt} failed: {reason}");
        ClearAuthTimeout();

        _abortingReconnectAttempt = true;
        _networkClient.AbandonReconnect();
        _networkClient.Disconnect();
        _abortingReconnectAttempt = false;

        if (!TryScheduleReconnect())
            ReturnToLoginAfterDrop();
    }

    void ReturnToLoginAfterDrop()
    {
        _networkClient.AbandonReconnect();
        ClearAuthTimeout();
        _reconnectAt = -1f;
        _reconnectAttempt = 0;
        _reconnectCharacterId = 0;

        _playfieldFactory.NetworkDriven = false;
        _playfieldFactory.Unload();
        _loadingScreen.Hide();
        _awaitingPlayfieldReady = false;
        _pendingLoginStatus = "Disconnected";

        // Back to the login screen. LoginWorld owns the backdrop, so this is a stage change
        // rather than streaming a playfield the way it used to be.
        _state = LoginScreenState.LoginBackdrop;
        _loginView.ClearCharacterButtons();
        _loginView.SetStatus(_pendingLoginStatus ?? string.Empty);
        _pendingLoginStatus = null;
        _loginView.SetCharacterStatus(string.Empty);
        _loginView.ShowLoginForm();
        _loginView.SetFormInteractable(true);

        // EnterLogin is idempotent, and the world will have been torn down if we got as far
        // as the playfield, so rebuild it before asking for a stage.
        _worldRouter?.EnterLogin();
        _worldRouter?.LoginWorld?.SetStage(0);
    }
}
