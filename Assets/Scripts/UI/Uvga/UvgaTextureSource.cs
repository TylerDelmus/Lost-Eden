using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Facade used by Uvga* VisualElements to resolve textures at edit time and runtime.
/// Runtime prefers <see cref="UvgaTextureCache"/>; editor uses <see cref="UvgaPathTextureCache"/>.
/// </summary>
public static class UvgaTextureSource
{
    const string EditorAoPathPrefsKey = "LostEden.Uvga.AoPath";

    static UvgaTextureCache _runtime;
    static UvgaPathTextureCache _path;

    public static event Action Changed;

    public static UvgaTextureCache RuntimeCache => _runtime;

    public static UvgaPathTextureCache PathCache => _path;

    public static void BindRuntime(UvgaTextureCache cache)
    {
        _runtime = cache;
        RaiseChanged();
    }

    public static void BindPathCache(UvgaPathTextureCache cache)
    {
        _path = cache;
        RaiseChanged();
    }

    public static void RaiseChanged()
    {
        Changed?.Invoke();
    }

    public static bool TryGet(string name, out Texture2D texture)
    {
        texture = null;
        if (string.IsNullOrEmpty(name))
            return false;

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            EnsureEditorPathReady();
            if (_path != null && _path.TryGet(name, out texture) && texture != null)
                return true;

            texture = null;
            return false;
        }
#endif

        if (_runtime != null && _runtime.TryGet(name, out texture) && texture != null)
            return true;

        if (_path != null && _path.TryGet(name, out texture) && texture != null)
            return true;

        texture = null;
        return false;
    }

#if UNITY_EDITOR
    /// <summary>
    /// UI Builder may resolve textures before bootstrap finishes, or after a domain reload
    /// left an empty path — re-bind from EditorPrefs / login prefs lazily.
    /// </summary>
    static void EnsureEditorPathReady()
    {
        if (_path == null)
            _path = new UvgaPathTextureCache();

        if (_path.IsLoaded)
            return;

        string aoPath = AoInstallPath.Normalize(EditorPrefs.GetString(EditorAoPathPrefsKey, string.Empty));
        if (!AoInstallPath.IsValid(aoPath))
            aoPath = AoInstallPath.Normalize(LoginPreferences.GetAoPath());

        if (!AoInstallPath.IsValid(aoPath))
            return;

        if (!string.Equals(_path.AoBasePath, aoPath, StringComparison.OrdinalIgnoreCase))
            _path.SetAoBasePath(aoPath);

        _path.TryEnsureLoaded();
    }
#endif
}
