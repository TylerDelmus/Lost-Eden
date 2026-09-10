using UnityEditor;

/// <summary>
/// EditorPrefs-backed AO install path for UVGA preview tooling.
/// Falls back to <see cref="LoginPreferences"/> when EditorPrefs is empty.
/// </summary>
public static class UvgaEditorPaths
{
    const string AoPathPrefsKey = "LostEden.Uvga.AoPath";

    public static string GetStoredAoPath()
    {
        return EditorPrefs.GetString(AoPathPrefsKey, string.Empty);
    }

    public static void SetAoPath(string aoPath)
    {
        string normalized = AoInstallPath.Normalize(aoPath);
        EditorPrefs.SetString(AoPathPrefsKey, normalized);
    }

    public static string ResolveAoPath()
    {
        string fromPrefs = AoInstallPath.Normalize(GetStoredAoPath());
        if (AoInstallPath.IsValid(fromPrefs))
            return fromPrefs;

        string fromLogin = AoInstallPath.Normalize(LoginPreferences.GetAoPath());
        if (AoInstallPath.IsValid(fromLogin))
            return fromLogin;

        return !string.IsNullOrEmpty(fromPrefs) ? fromPrefs : fromLogin;
    }
}
