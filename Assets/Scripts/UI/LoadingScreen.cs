using System;
using System.IO;
using UnityEngine;

public enum LoadingScreenKind
{
    Login
}

public sealed class LoadingScreen
{
    const string LoginRelativePath = "cd_image/gui/Default/gfx/welcome_to_rubika.jpg";

    readonly LoadingScreenView _view;
    readonly ResourceDatabase _database;

    Texture2D _loginTexture;

    public LoadingScreen(LoadingScreenView view, ResourceDatabase database)
    {
        _view = view;
        _database = database;
    }

    public bool IsReady => _view != null && _view.IsReady;

    public void Show(string message, LoadingScreenKind kind = LoadingScreenKind.Login)
    {
        if (_view == null)
            return;

        _view.Show(message, ResolveTexture(kind));
    }

    public void HideFade(Action onComplete = null)
    {
        _view?.HideFade(onComplete);
    }

    public void HideFade(float duration, Action onComplete = null)
    {
        _view?.HideFade(duration, onComplete);
    }

    public void Hide()
    {
        _view?.Hide();
    }

    Texture2D ResolveTexture(LoadingScreenKind kind)
    {
        if (_database?.Rdb == null)
            return null;

        _ = kind;
        return GetOrLoad(ref _loginTexture, _database.Rdb.BaseAoPath, LoginRelativePath);
    }

    static Texture2D GetOrLoad(ref Texture2D cache, string aoBasePath, string relativePath)
    {
        if (cache != null)
            return cache;

        string path = Path.Combine(aoBasePath, relativePath);
        if (!File.Exists(path))
        {
            Debug.LogWarning($"[LoadingScreen] Missing art at '{path}'");
            return null;
        }

        byte[] bytes = File.ReadAllBytes(path);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
        if (!texture.LoadImage(bytes, markNonReadable: false))
        {
            Debug.LogWarning($"[LoadingScreen] Failed to decode '{path}'");
            UnityEngine.Object.Destroy(texture);
            return null;
        }

        cache = texture;
        return cache;
    }
}
