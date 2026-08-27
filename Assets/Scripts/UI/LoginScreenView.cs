using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

[DisallowMultipleComponent]
[RequireComponent(typeof(UIDocument))]
public class LoginScreenView : MonoBehaviour
{
    const string LoginScreenResourcePath = "UI/LoginScreen";
    const string CharacterButtonResourcePath = "UI/CharacterButton";
    const int SortOrder = 100;

    UiMenu _menu;
    LoginScreenElements _ui;
    VisualTreeAsset _characterButtonTemplate;
    readonly List<Button> _characterButtons = new();

    public bool IsReady => _menu != null && _ui != null;

    public TextField UsernameField => _ui?.UsernameField;
    public TextField PasswordField => _ui?.PasswordField;
    public DropdownField DimensionDropdown => _ui?.DimensionDropdown;
    public Button ConnectButton => _ui?.ConnectButton;
    public Button BackButton => _ui?.BackButton;

    void Awake()
    {
        _menu = UserInterface.Load(this, LoginScreenResourcePath, SortOrder, startVisible: false, logName: "LoginScreen");
        if (_menu == null)
            return;

        _characterButtonTemplate = UserInterface.LoadTemplate(CharacterButtonResourcePath);
        if (_characterButtonTemplate == null)
            Debug.LogWarning($"[LoginScreen] Missing character button template at Resources/{CharacterButtonResourcePath}");

        _ui = LoginScreenElements.Bind(_menu.Root);
        ApplyFormAppearance();

        UserInterface.SetVisible(_ui.LoginPanel, false);
        UserInterface.SetVisible(_ui.CharacterPanel, false);
    }

    public void SetConnectHandler(Action handler)
    {
        if (!IsReady || handler == null)
            return;

        _ui.ConnectButton.clicked += handler;
        RegisterSubmitOnEnter(_ui.UsernameField, handler);
        RegisterSubmitOnEnter(_ui.PasswordField, handler);
    }

    public void SetBackHandler(Action handler)
    {
        if (!IsReady || handler == null)
            return;

        _ui.BackButton.clicked += handler;
    }

    void RegisterSubmitOnEnter(TextField field, Action handler)
    {
        if (field == null)
            return;

        // TrickleDown so we catch Enter before TextField consumes it to commit focus.
        field.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (!IsSubmitKey(evt))
                return;

            if (!field.enabledSelf || !_ui.ConnectButton.enabledSelf)
                return;

            handler();
            evt.StopImmediatePropagation();
        }, TrickleDown.TrickleDown);
    }

    static bool IsSubmitKey(KeyDownEvent evt)
    {
        return evt.keyCode == KeyCode.Return
            || evt.keyCode == KeyCode.KeypadEnter
            || evt.character == '\n'
            || evt.character == '\r';
    }

    void OnDestroy()
    {
        if (_menu != null)
            UserInterface.Unregister(_menu);
    }

    void ApplyFormAppearance()
    {
        UserInterface.StyleTextField(_ui.UsernameField);
        UserInterface.StyleTextField(_ui.PasswordField);
        UserInterface.StyleDropdown(_ui.DimensionDropdown);
        UserInterface.StyleButton(_ui.ConnectButton);

        UserInterface.StyleLabel(_ui.StatusText);
        UserInterface.StyleLabel(_ui.CharacterStatusText);

        foreach (Label label in _ui.LoginPanel.Query<Label>().ToList())
            UserInterface.StyleLabel(label);

        foreach (Label label in _ui.CharacterPanel.Query<Label>().ToList())
            UserInterface.StyleLabel(label);
    }

    public void PopulateDimensions(IReadOnlyList<DimensionInfo> dimensions)
    {
        if (!IsReady)
            return;
        var choices = new List<string>(dimensions.Count);
        foreach (DimensionInfo dimension in dimensions)
            choices.Add(string.IsNullOrEmpty(dimension.Name) ? dimension.Id : dimension.Name);

        _ui.DimensionDropdown.choices = choices;
        if (choices.Count > 0)
            _ui.DimensionDropdown.index = 0;

        UserInterface.StyleDropdown(_ui.DimensionDropdown);
    }

    public int GetSelectedDimensionIndex() => IsReady ? _ui.DimensionDropdown.index : -1;

    public void HideLoginUi()
    {
        if (!IsReady)
            return;
        UserInterface.SetVisible(_ui.LoginPanel, false);
        UserInterface.SetVisible(_ui.CharacterPanel, false);
        _menu.Hide();
    }

    public void ShowLoginForm()
    {
        if (!IsReady)
            return;
        _menu.Show();
        UserInterface.SetVisible(_ui.LoginPanel, true);
        UserInterface.SetVisible(_ui.CharacterPanel, false);
        SetFormInteractable(true);
    }

    public void ShowCharacterSelect()
    {
        if (!IsReady)
            return;
        _menu.Show();
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

        if (interactable)
            UserInterface.StyleButton(_ui.ConnectButton);
        else
            UserInterface.StyleDisabledButton(_ui.ConnectButton);
    }

    public void SetStatus(string message)
    {
        if (!IsReady)
            return;
        _ui.StatusText.text = message ?? string.Empty;
    }

    public void SetCharacterStatus(string message)
    {
        if (!IsReady)
            return;
        _ui.CharacterStatusText.text = message ?? string.Empty;
    }

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
            var button = instance.Q<Button>("character-button");
            button.text = character.name;
            UserInterface.StyleButton(button);

            int characterId = character.id;
            button.clicked += () => onSelected(characterId);

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
    public VisualElement LoginPanel;
    public VisualElement CharacterPanel;
    public TextField UsernameField;
    public TextField PasswordField;
    public DropdownField DimensionDropdown;
    public Button ConnectButton;
    public Button BackButton;
    public Label StatusText;
    public Label CharacterStatusText;
    public VisualElement CharacterList;

    public static LoginScreenElements Bind(VisualElement root)
    {
        return new LoginScreenElements
        {
            Root = root,
            LoginPanel = root.Q<VisualElement>("login-panel"),
            CharacterPanel = root.Q<VisualElement>("character-panel"),
            UsernameField = root.Q<TextField>("username-field"),
            PasswordField = root.Q<TextField>("password-field"),
            DimensionDropdown = root.Q<DropdownField>("dimension-dropdown"),
            ConnectButton = root.Q<Button>("connect-button"),
            BackButton = root.Q<Button>("back-button"),
            StatusText = root.Q<Label>("status-text"),
            CharacterStatusText = root.Q<Label>("character-status-text"),
            CharacterList = root.Q<VisualElement>("character-list")
        };
    }
}
