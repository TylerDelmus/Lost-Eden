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
    InGame
}

[DisallowMultipleComponent]
public class LoginScreenController : MonoBehaviour
{
    [Inject] NetworkClient _networkClient;
    [Inject] PlayfieldFactory _playfieldFactory;
    [Inject] LoadingScreen _loadingScreen;
    [Inject] PlayerController _playerController;
    [Inject] ResourceDatabase _resourceDatabase;

    [SerializeField] LoginScreenView _loginView;

    [Header("Login Camera")]
    [SerializeField] UnityEngine.Vector3 _loginCameraPosition;
    [SerializeField] UnityEngine.Vector3 _loginCameraEulerAngles;

    const float AuthTimeoutSeconds = 30f;
    const string DefaultBrowseHint = @"C:\Program Files (x86)\Steam\steamapps\common\Anarchy Online";

    LoginScreenState _state = LoginScreenState.BootLoading;

    IReadOnlyList<DimensionInfo> _dimensions;
    bool _awaitingPlayfieldReady;
    bool _awaitingBackdropReload;
    bool _ignoreNextDisconnect;
    string _pendingLoginStatus;
    float _authTimeoutAt = -1f;

    void Awake()
    {
        _loginView ??= UserInterface.FindOrCreateMenuView<LoginScreenView>(transform, "LoginMenu");
    }

    void Start()
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
        string saved = LoginPreferences.GetAoPath();
        _loginView.ShowAoPathSetup(string.IsNullOrWhiteSpace(saved) ? string.Empty : saved);
        _loginView.SetAoPathStatus("Anarchy Online install path is required.");
    }

    void BeginBootLoading()
    {
        _state = LoginScreenState.BootLoading;
        _loginView.HideLoginUi();
        _loadingScreen.Show("Loading...", LoadingScreenKind.Login);
        LoadBackdrop(LoginPreferences.GetPlayfieldId());
    }

    bool EnsureResourceDatabase()
    {
        if (_resourceDatabase != null && _resourceDatabase.IsInitialized)
            return true;

        string path = AoInstallPath.Normalize(LoginPreferences.GetAoPath());
        if (!AoInstallPath.IsValid(path))
            return false;

        try
        {
            _resourceDatabase.Initialize(path);
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

        string current = _loginView.AoPathField.value;
        string initial = Directory.Exists(current) ? current : DefaultBrowseHint;

        if (!NativeFolderDialog.TryPickFolder("Select Anarchy Online Folder", initial, out string selected))
            return;

        _loginView.AoPathField.value = selected;
        _loginView.SetAoPathStatus(string.Empty);
    }

    void OnAoPathConfirmClicked()
    {
        if (_state != LoginScreenState.AwaitingAoPath)
            return;

        string path = AoInstallPath.Normalize(_loginView.AoPathField.value);
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

        LoginPreferences.SaveAoPath(path);
        _loginView.AoPathField.value = path;
        BeginBootLoading();
    }

    void Update()
    {
        _networkClient.Update();
        TickAuthTimeout();
    }

    void TickAuthTimeout()
    {
        if (_authTimeoutAt < 0f || _state != LoginScreenState.Authenticating)
            return;

        if (Time.realtimeSinceStartup < _authTimeoutAt)
            return;

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
        _networkClient.CharacterListReceived += OnCharacterListReceived;
        _networkClient.LoginFailed += OnLoginFailed;
        _networkClient.Disconnected += OnDisconnected;
        _networkClient.PhaseChanged += OnPhaseChanged;
    }

    void OnDisable()
    {
        _playfieldFactory.PlayfieldReady -= OnPlayfieldReady;
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
        _loginView.UsernameField.value = LoginPreferences.GetUsername();

        string savedDimensionId = LoginPreferences.GetDimensionId();
        if (string.IsNullOrEmpty(savedDimensionId))
            return;

        for (int i = 0; i < _dimensions.Count; i++)
        {
            if (_dimensions[i].Id.Equals(savedDimensionId, System.StringComparison.OrdinalIgnoreCase))
            {
                _loginView.DimensionDropdown.index = i;
                break;
            }
        }
    }

    void OnConnectClicked()
    {
        if (_state != LoginScreenState.LoginBackdrop)
            return;

        string username = _loginView.UsernameField.value.Trim();
        string password = _loginView.PasswordField.value;

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
        if (_state != LoginScreenState.Authenticating)
            return;

        ClearAuthTimeout();

        if (charList.Characters == null || charList.Characters.Length == 0)
        {
            FailAuthentication("No characters found on this account.");
            return;
        }

        _state = LoginScreenState.CharacterSelect;
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
        _loginView.PasswordField.value = string.Empty;
    }

    void OnCharacterSelected(int characterId)
    {
        if (_state != LoginScreenState.CharacterSelect)
            return;

        _state = LoginScreenState.EnteringGame;
        _loginView.HideLoginUi();
        _loginView.ClearCharacterButtons();
        _loadingScreen.Show("Entering world...", LoadingScreenKind.Login);
        _playfieldFactory.NetworkDriven = true;
        _playfieldFactory.Unload();
        _awaitingPlayfieldReady = true;
        _networkClient.SelectCharacter(characterId);
    }

    void OnPlayfieldReady(int zoneId)
    {
        if (_awaitingBackdropReload)
        {
            _awaitingBackdropReload = false;
            _state = LoginScreenState.LoginBackdrop;
            ApplyLoginCameraPose();
            _loginView.ShowLoginForm();
            _loginView.SetFormInteractable(true);
            _loginView.SetStatus(_pendingLoginStatus ?? string.Empty);
            _pendingLoginStatus = null;
            _loadingScreen.HideFade();
            Debug.Log($"[LoginScreen] Backdrop ready (id={zoneId})");
            return;
        }

        if (_state == LoginScreenState.BootLoading)
        {
            _state = LoginScreenState.LoginBackdrop;
            ApplyLoginCameraPose();
            _loginView.ShowLoginForm();
            _loginView.SetFormInteractable(true);
            _loadingScreen.HideFade();
            Debug.Log($"[LoginScreen] Boot backdrop ready (id={zoneId})");
            return;
        }

        if (_awaitingPlayfieldReady && _state == LoginScreenState.EnteringGame)
        {
            _awaitingPlayfieldReady = false;
            _state = LoginScreenState.InGame;
            _loadingScreen.HideFade();
            Debug.Log($"[LoginScreen] Entered world (id={zoneId})");
        }
    }

    void ApplyLoginCameraPose()
    {
        if (_playerController?.CameraController == null)
        {
            Debug.LogWarning("[LoginScreen] CameraController unavailable; skipping login camera pose.");
            return;
        }

        _playerController.CameraController.SetFreePose(_loginCameraPosition, _loginCameraEulerAngles);
    }

    void OnLoginFailed(LoginError error)
    {
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

        // Bad password / early login drop: keep the form and backdrop, only show status.
        if (_state == LoginScreenState.Authenticating)
        {
            FailAuthentication("Disconnected");
            return;
        }

        _networkClient.AbandonReconnect();
        _playfieldFactory.NetworkDriven = false;
        _awaitingPlayfieldReady = false;
        _pendingLoginStatus = "Disconnected";

        _state = LoginScreenState.BootLoading;
        _loginView.ClearCharacterButtons();
        _loadingScreen.Show("Loading...", LoadingScreenKind.Login);
        _loginView.SetFormInteractable(false);
        _loginView.SetStatus(string.Empty);
        _loginView.SetCharacterStatus(string.Empty);

        _awaitingBackdropReload = true;
        LoadBackdrop(LoginPreferences.GetPlayfieldId());
    }

    void LoadBackdrop(int playfieldId)
    {
        _playfieldFactory.NetworkDriven = false;
        _playfieldFactory.Load(playfieldId);
    }
}
