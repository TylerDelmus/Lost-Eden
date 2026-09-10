using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Handle for a chrome window loaded via <see cref="UserInterface.LoadWindow"/>.
/// </summary>
public sealed class UiWindow : IDisposable
{
    readonly UiMenu _menu;
    readonly Label _titleLabel;
    readonly VisualElement _closeButton;
    readonly VisualElement _body;
    VisualElement _contentRoot;
    bool _disposed;

    public UiMenu Menu => _menu;
    public VisualElement Root => _menu.Root;
    public VisualElement ContentRoot => _contentRoot;
    public VisualElement Body => _body;
    public string Title { get; private set; }
    public bool IsVisible => _menu != null && _menu.IsVisible;

    public event Action Closed;

    internal UiWindow(
        UiMenu menu,
        Label titleLabel,
        VisualElement closeButton,
        VisualElement body,
        VisualElement contentRoot,
        string title)
    {
        _menu = menu ?? throw new ArgumentNullException(nameof(menu));
        _titleLabel = titleLabel;
        _closeButton = closeButton;
        _body = body ?? throw new ArgumentNullException(nameof(body));
        _contentRoot = contentRoot ?? body;
        Title = title ?? string.Empty;

        if (_closeButton != null)
        {
            _closeButton.pickingMode = PickingMode.Position;
            _closeButton.RegisterCallback<ClickEvent>(OnCloseClicked);
        }

        SetTitle(Title);
    }

    public void SetTitle(string title)
    {
        Title = title ?? string.Empty;
        if (_titleLabel != null)
            _titleLabel.text = Title;
    }

    public void Show() => _menu.Show();

    public void Hide() => _menu.Hide();

    public void SetVisible(bool visible) => _menu.SetVisible(visible);

    public void Close()
    {
        Hide();
        Closed?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_closeButton != null)
            _closeButton.UnregisterCallback<ClickEvent>(OnCloseClicked);

        // Unregister removes from ActiveMenus and disposes the underlying UiMenu.
        if (_menu != null)
        {
            UserInterface.Unregister(_menu);
        }
    }

    void OnCloseClicked(ClickEvent evt)
    {
        evt.StopPropagation();
        Close();
    }
}
