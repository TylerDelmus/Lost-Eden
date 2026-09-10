using UnityEditor;

[InitializeOnLoad]
static class UvgaEditorBootstrap
{
    static readonly UvgaPathTextureCache PathCache = new UvgaPathTextureCache();

    static UvgaEditorBootstrap()
    {
        PathCache.SetAoBasePath(UvgaEditorPaths.ResolveAoPath());
        PathCache.TryEnsureLoaded();
        UvgaTextureSource.BindPathCache(PathCache);
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    public static UvgaPathTextureCache Cache => PathCache;

    public static void ReloadFromPrefs()
    {
        PathCache.Reload(UvgaEditorPaths.ResolveAoPath());
        UvgaTextureSource.RaiseChanged();
    }

    static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode)
            return;

        UvgaTextureSource.BindRuntime(null);
        ReloadFromPrefs();
    }
}
