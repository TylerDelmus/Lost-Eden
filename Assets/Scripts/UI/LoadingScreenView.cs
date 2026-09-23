using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

[DisallowMultipleComponent]
[RequireComponent(typeof(PanelRenderer))]
public class LoadingScreenView : MonoBehaviour
{
    const string ResourcePath = "UI/LoadingScreen";
    const int SortOrder = 110;

    PanelRenderer _renderer;
    VisualElement _panelRoot;
    VisualElement _contentRoot;
    VisualElement _background;
    Label _text;

    Coroutine _fadeRoutine;

    // PanelRenderer only hands out its root through RegisterUIReloadCallback, which fires
    // once the panel is attached rather than during Awake. Show/Hide can land before that,
    // so the wanted state is kept here and replayed in OnUiReload.
    bool _visible;
    string _message = string.Empty;
    Texture2D _texture;

    public bool IsReady => _renderer != null;

    void Awake()
    {
        _renderer = GetComponent<PanelRenderer>();
        if (_renderer == null)
            _renderer = gameObject.AddComponent<PanelRenderer>();

        var asset = Resources.Load<VisualTreeAsset>(ResourcePath);
        if (asset == null)
        {
            Debug.LogError($"[LoadingScreen] Missing VisualTreeAsset at Resources/{ResourcePath}");
            _renderer = null;
            return;
        }

        // Asset before panel settings: assigning panelSettings rebuilds the root, and the
        // tree is only cloned into it if the asset is already there.
        _renderer.visualTreeAsset = asset;
        _renderer.sortingOrder = SortOrder;
        UserInterface.EnsurePanelSettings(_renderer, SortOrder);
        _renderer.RegisterUIReloadCallback(OnUiReload);
    }

    void OnDestroy()
    {
        StopFade();
        if (_renderer != null)
            _renderer.UnregisterUIReloadCallback(OnUiReload);
    }

    void OnUiReload(PanelRenderer panelRenderer, VisualElement root, int version)
    {
        _panelRoot = root;
        _contentRoot = root.Q<VisualElement>("root") ?? root;

        UserInterface.StretchToScreen(_panelRoot);
        UserInterface.StretchToScreen(_contentRoot);
        UserInterface.EnsureStylesheet(_contentRoot, ResourcePath);

        _background = _contentRoot.Q<VisualElement>("loading-background");
        _text = _contentRoot.Q<Label>("loading-text");
        UserInterface.StyleLabel(_text);

        ApplyState();
    }

    public void Show(string message, Texture2D texture)
    {
        if (_renderer == null)
            return;

        StopFade();
        _message = message;
        _texture = texture;
        _visible = true;
        ApplyState();
    }

    public void HideFade(Action onComplete = null)
    {
        HideFade(1f, onComplete);
    }

    public void HideFade(float duration, Action onComplete = null)
    {
        if (!_visible || _contentRoot == null)
        {
            Hide();
            onComplete?.Invoke();
            return;
        }

        StopFade();
        _fadeRoutine = StartCoroutine(FadeOut(duration, onComplete));
    }

    public void Hide()
    {
        StopFade();
        _visible = false;
        ApplyState();
    }

    void ApplyState()
    {
        if (_contentRoot == null)
            return;

        if (_text != null)
            _text.text = _message;

        if (_background != null)
        {
            if (_texture != null)
                _background.style.backgroundImage = new StyleBackground(_texture);
            else
                _background.style.backgroundImage = StyleKeyword.None;
        }

        UserInterface.SetOpacity(_contentRoot, 1f);
        UserInterface.SetVisible(_contentRoot, _visible);
    }

    void StopFade()
    {
        if (_fadeRoutine == null)
            return;

        StopCoroutine(_fadeRoutine);
        _fadeRoutine = null;
    }

    IEnumerator FadeOut(float duration, Action onComplete)
    {
        float elapsed = 0f;
        _contentRoot.pickingMode = PickingMode.Ignore;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            UserInterface.SetOpacity(_contentRoot, Mathf.Lerp(1f, 0f, elapsed / duration));
            yield return null;
        }

        _fadeRoutine = null;
        _visible = false;
        ApplyState();
        onComplete?.Invoke();
    }
}
