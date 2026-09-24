#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

[InitializeOnLoad]
static class UiThemeBootstrap
{
    internal const string ThemePath = "Assets/Resources/UI/UnityDefaultRuntimeTheme.tss";
    internal const string PanelSettingsPath = "Assets/Resources/UI/DefaultPanelSettings.asset";

    static UiThemeBootstrap()
    {
        EnsureAssets();
    }

    internal static void EnsureAssets()
    {
        EnsureThemeAsset();
        EnsurePanelSettingsAsset();
    }

    // Both checks are on the file, not on the loaded asset. This runs on every domain reload,
    // and mid-reimport the asset can fail to load for a moment. A load check then overwrote
    // the real theme (which imports the skin) with this stub.
    static void EnsureThemeAsset()
    {
        if (File.Exists(ThemePath))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(ThemePath)!);
        File.WriteAllText(ThemePath, "@import url(\"unity-theme://default\");\n");
        AssetDatabase.ImportAsset(ThemePath, ImportAssetOptions.ForceSynchronousImport);
    }

    static void EnsurePanelSettingsAsset()
    {
        if (File.Exists(PanelSettingsPath))
            return;

        ThemeStyleSheet theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
        if (theme == null)
            return;

        var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
        // One UI pixel is one screen pixel, whatever the resolution: the fonts are bitmaps and
        // only draw exactly at a whole-number scale (Docs/UI.md §8a).
        panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
        panelSettings.scale = 1f;
        panelSettings.sortingOrder = 0;
        panelSettings.themeStyleSheet = theme;

        PanelTextSettings textSettings = FindPanelTextSettings();
        if (textSettings != null)
            panelSettings.textSettings = textSettings;

        Directory.CreateDirectory(Path.GetDirectoryName(PanelSettingsPath)!);
        AssetDatabase.CreateAsset(panelSettings, PanelSettingsPath);
        AssetDatabase.SaveAssets();
    }

    static PanelTextSettings FindPanelTextSettings()
    {
        string[] guids = AssetDatabase.FindAssets("t:PanelTextSettings");
        if (guids.Length == 0)
            return null;

        return AssetDatabase.LoadAssetAtPath<PanelTextSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));
    }
}
#endif
