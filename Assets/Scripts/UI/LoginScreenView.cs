using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The login screen, built on the <c>Ao*</c> widget classes ported from GUI.dll rather than on
/// raw UI Toolkit controls. Appearance still comes from <c>LoginScreen.uss</c>; the Ao classes
/// carry structure and behaviour only until the art pass.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PanelRenderer))]
public class LoginScreenView : MonoBehaviour
{
    const string LoginScreenResourcePath = "UI/LoginScreen";
    const string CharacterButtonResourcePath = "UI/CharacterButton";
    const int SortOrder = 100;

    UiMenu _menu;
    LoginScreenElements _ui;
    VisualTreeAsset _characterButtonTemplate;
    readonly List<AoButton> _characterButtons = new();
    readonly List<string> _dimensionChoices = new();

    Action _onReady;

    public bool IsReady => _menu != null && _ui != null;

    public AoComboBox UsernameField => _ui?.UsernameField;
    public AoTextInputView PasswordField => _ui?.PasswordField;
    public AoTextInputView AoPathField => _ui?.AoPathField;
    public AoComboBox DimensionDropdown => _ui?.DimensionDropdown;
    public AoButton ConnectButton => _ui?.ConnectButton;
    public AoButton BackButton => _ui?.BackButton;
    public AoButton BrowseButton => _ui?.BrowseButton;
    public AoButton AoPathConfirmButton => _ui?.AoPathConfirmButton;

    void Awake()
    {
        _menu = UserInterface.Load(this, LoginScreenResourcePath, SortOrder, startVisible: false, logName: "LoginScreen");
        if (_menu == null)
            return;

        _characterButtonTemplate = UserInterface.LoadTemplate(CharacterButtonResourcePath);
        if (_characterButtonTemplate == null)
            Debug.LogWarning($"[LoginScreen] Missing character button template at Resources/{CharacterButtonResourcePath}");

        // PanelRenderer hands the root back through its reload callback, after Awake.
        _menu.WhenReady(OnMenuReady);
    }

    void OnMenuReady(VisualElement root)
    {
        _ui = LoginScreenElements.Bind(root);

        if (_ui.PasswordField != null)
            _ui.PasswordField.IsPassword = true;

        // Enter submits from any of the three fields, and is swallowed rather than left to
        // fall through — stock's capture_enter.
        foreach (AoTextInputView field in new[] { _ui.PasswordField, _ui.AoPathField })
            if (field != null)
                field.CaptureEnter = true;

        HideAllPanels();

        Action callbacks = _onReady;
        _onReady = null;
        callbacks?.Invoke();
    }

    /// <summary>Runs <paramref name="onReady"/> now if the form is bound, else when it is.</summary>
    public void WhenReady(Action onReady)
    {
        if (onReady == null)
            return;

        if (IsReady)
            onReady();
        else
            _onReady += onReady;
    }

    void HideAllPanels()
    {
        UserInterface.SetVisible(_ui.AoPathPanel, false);
        UserInterface.SetVisible(_ui.LoginPanel, false);
        UserInterface.SetVisible(_ui.CharacterPanel, false);
    }

    // ---- handlers -----------------------------------------------------------------------

    public void SetConnectHandler(Action handler)
    {
        if (!IsReady || handler == null)
            return;

        _ui.ConnectButton.Clicked += handler;
        if (_ui.UsernameField?.Field != null)
            RegisterSubmitOnEnter(_ui.UsernameField.Field, handler);
        if (_ui.PasswordField != null)
            _ui.PasswordField.Submitted += handler;
    }

    public void SetBackHandler(Action handler)
    {
        if (!IsReady || handler == null)
            return;

        _ui.BackButton.Clicked += handler;
    }

    public void SetBrowseHandler(Action handler)
    {
        if (!IsReady || handler == null)
            return;

        _ui.BrowseButton.Clicked += handler;
    }

    public void SetAoPathConfirmHandler(Action handler)
    {
        if (!IsReady || handler == null)
            return;

        _ui.AoPathConfirmButton.Clicked += handler;
        if (_ui.AoPathField != null)
            _ui.AoPathField.Submitted += handler;
    }

    /// <summary>The combo box exposes a raw TextField, which has no Submitted of its own.</summary>
    static void RegisterSubmitOnEnter(TextField field, Action handler)
    {
        field.RegisterCallback<KeyDownEvent>(evt =>
        {
            bool isEnter = evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter
                           || evt.character == '\n' || evt.character == '\r';
            if (!isEnter || !field.enabledSelf)
                return;

            handler();
            evt.StopImmediatePropagation();
        }, TrickleDown.TrickleDown);
    }

    void OnDestroy()
    {
        _onReady = null;
        if (_menu != null)
            UserInterface.Unregister(_menu);
    }

    // ---- dimensions ---------------------------------------------------------------------

    public void PopulateDimensions(IReadOnlyList<DimensionInfo> dimensions)
    {
        if (!IsReady)
            return;

        _dimensionChoices.Clear();
        foreach (DimensionInfo dimension in dimensions)
            _dimensionChoices.Add(string.IsNullOrEmpty(dimension.Name) ? dimension.Id : dimension.Name);

        _ui.DimensionDropdown.SetChoices(_dimensionChoices);
        if (_dimensionChoices.Count > 0)
            _ui.DimensionDropdown.Value = _dimensionChoices[0];
    }

    public int GetSelectedDimensionIndex()
    {
        if (!IsReady)
            return -1;

        return _dimensionChoices.IndexOf(_ui.DimensionDropdown.Value ?? string.Empty);
    }

    public void SelectDimension(int index)
    {
        if (!IsReady || index < 0 || index >= _dimensionChoices.Count)
            return;

        _ui.DimensionDropdown.Value = _dimensionChoices[index];
    }

    // ---- panels -------------------------------------------------------------------------

    public void HideLoginUi()
    {
        if (!IsReady)
            return;

        HideAllPanels();
        _menu.Hide();
    }

    public void ShowAoPathSetup(string path = null)
    {
        if (!IsReady)
            return;

        _menu.Show();
        UserInterface.SetVisible(_ui.AoPathPanel, true);
        UserInterface.SetVisible(_ui.LoginPanel, false);
        UserInterface.SetVisible(_ui.CharacterPanel, false);
        _ui.AoPathField.Value = path ?? string.Empty;
        SetAoPathStatus(string.Empty);
    }

    public void ShowLoginForm()
    {
        if (!IsReady)
            return;

        _menu.Show();
        UserInterface.SetVisible(_ui.AoPathPanel, false);
        UserInterface.SetVisible(_ui.LoginPanel, true);
        UserInterface.SetVisible(_ui.CharacterPanel, false);
        SetFormInteractable(true);
    }

    public void ShowCharacterSelect()
    {
        if (!IsReady)
            return;

        _menu.Show();
        UserInterface.SetVisible(_ui.AoPathPanel, false);
        UserInterface.SetVisible(_ui.LoginPanel, false);
        UserInterface.SetVisible(_ui.CharacterPanel, true);
    }

    public void SetFormInteractable(bool interactable)
    {
        if (!IsReady)
            return;

        _ui.UsernameField.SetEnabled(interactable);
        _ui.PasswordField.SetEnabled(interactable);
        _ui.DimensionDropdown.SetEnabled(interactable);
        _ui.ConnectButton.SetEnabled(interactable);
    }

    // ---- status -------------------------------------------------------------------------

    public void SetStatus(string message)
    {
        if (IsReady)
            _ui.StatusText.Value = message ?? string.Empty;
    }

    public void SetAoPathStatus(string message)
    {
        if (IsReady)
            _ui.AoPathStatusText.Value = message ?? string.Empty;
    }

    public void SetCharacterStatus(string message)
    {
        if (IsReady)
            _ui.CharacterStatusText.Value = message ?? string.Empty;
    }

    // ---- character list -----------------------------------------------------------------

    public void RebuildCharacterButtons(IEnumerable<(int id, string name)> characters, Action<int> onSelected)
    {
        if (!IsReady)
            return;

        ClearCharacterButtons();

        if (_characterButtonTemplate == null)
            return;

        foreach ((int id, string name) character in characters)
        {
            TemplateContainer instance = _characterButtonTemplate.Instantiate();
            var button = instance.Q<AoButton>("character-button");
            if (button == null)
                continue;

            button.Label = character.name;

            int characterId = character.id;
            button.Clicked += () => onSelected(characterId);

            _ui.CharacterList.Add(button);
            _characterButtons.Add(button);
        }
    }

    public void ClearCharacterButtons()
    {
        if (!IsReady)
            return;

        _ui.CharacterList.Clear();
        _characterButtons.Clear();
    }
}

sealed class LoginScreenElements
{
    public VisualElement Root;
    public AoView AoPathPanel;
    public AoView LoginPanel;
    public AoView CharacterPanel;
    public AoComboBox UsernameField;
    public AoTextInputView PasswordField;
    public AoTextInputView AoPathField;
    public AoComboBox DimensionDropdown;
    public AoButton ConnectButton;
    public AoButton BackButton;
    public AoButton BrowseButton;
    public AoButton AoPathConfirmButton;
    public AoTextView StatusText;
    public AoTextView CharacterStatusText;
    public AoTextView AoPathStatusText;
    public VisualElement CharacterList;

    public static LoginScreenElements Bind(VisualElement root)
    {
        return new LoginScreenElements
        {
            Root = root,
            AoPathPanel = root.Q<AoView>("aopath-panel"),
            LoginPanel = root.Q<AoView>("login-panel"),
            CharacterPanel = root.Q<AoView>("character-panel"),
            UsernameField = root.Q<AoComboBox>("username-field"),
            PasswordField = root.Q<AoTextInputView>("password-field"),
            AoPathField = root.Q<AoTextInputView>("aopath-field"),
            DimensionDropdown = root.Q<AoComboBox>("dimension-dropdown"),
            ConnectButton = root.Q<AoButton>("connect-button"),
            BackButton = root.Q<AoButton>("back-button"),
            BrowseButton = root.Q<AoButton>("browse-button"),
            AoPathConfirmButton = root.Q<AoButton>("aopath-confirm-button"),
            StatusText = root.Q<AoTextView>("status-text"),
            CharacterStatusText = root.Q<AoTextView>("character-status-text"),
            AoPathStatusText = root.Q<AoTextView>("aopath-status-text"),
            CharacterList = root.Q<AoScrollViewChild>("character-list")
        };
    }
}
