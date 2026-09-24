/// <summary>
/// Editor-tooling view of the AO install path.
///
/// This used to own its own EditorPrefs key and its own fallback chain into
/// <c>LoginPreferences</c>, which made it the only place that knew how the path was really
/// resolved. Storage and resolution now live in <see cref="AoInstall"/>; this is a thin
/// wrapper kept so the Uvga windows read the way they always did.
/// </summary>
public static class UvgaEditorPaths
{
    public static string GetStoredAoPath() => AoInstall.GetEditorOverride();

    public static void SetAoPath(string aoPath) => AoInstall.SetEditorOverride(aoPath);

    public static string ResolveAoPath() => AoInstall.Path;
}
