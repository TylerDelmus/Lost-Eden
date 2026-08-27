using System;
using UnityEngine;
using UnityEngine.UIElements;

[DisallowMultipleComponent]
[RequireComponent(typeof(UIDocument))]
public class LoadingScreenView : MonoBehaviour
{
    const string ResourcePath = "UI/LoadingScreen";
    const int SortOrder = 110;

    UiMenu _menu;
    VisualElement _background;
    Label _text;

    public bool IsReady => _menu != null;

    void Awake()
    {
        _menu = UserInterface.Load(this, ResourcePath, SortOrder, startVisible: false, logName: "LoadingScreen");
        if (_menu == null)
            return;

        _background = _menu.Q<VisualElement>("loading-background");
        _text = _menu.Q<Label>("loading-text");
        UserInterface.StyleLabel(_text);
    }

    void OnDestroy()
    {
        if (_menu != null)
            UserInterface.Unregister(_menu);
    }

    public void Show(string message, Texture2D texture)
    {
        if (_menu == null)
            return;

        if (_text != null)
            _text.text = message;

        if (_background != null)
        {
            if (texture != null)
                _background.style.backgroundImage = new StyleBackground(texture);
            else
                _background.style.backgroundImage = StyleKeyword.None;
        }

        _menu.Show();
    }

    public void HideFade(Action onComplete = null)
    {
        _menu?.HideFade(onComplete);
    }

    public void HideFade(float duration, Action onComplete = null)
    {
        _menu?.HideFade(duration, onComplete);
    }

    public void Hide()
    {
        _menu?.Hide();
    }
}
