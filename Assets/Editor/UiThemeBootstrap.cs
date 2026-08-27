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

    static void EnsureThemeAsset()
    {
        if (AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath) != null)
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(ThemePath)!);
        File.WriteAllText(ThemePath, "@import url(\"unity-theme://default\");\n");
        AssetDatabase.ImportAsset(ThemePath, ImportAssetOptions.ForceSynchronousImport);
    }

    static void EnsurePanelSettingsAsset()
    {
        if (AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath) != null)
            return;

        ThemeStyleSheet theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
        if (theme == null)
            return;

        var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
        panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        panelSettings.referenceResolution = new Vector2Int(1920, 1080);
        panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
        panelSettings.match = 0.5f;
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
