using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// The single authority for "where is Anarchy Online installed".
///
/// Everything in the project reads its art, meshes, RDB records and GUI XML out of the AO
/// install, so this path is content configuration — not a login preference, which is where it
/// used to live. There are two stores and one resolve order:
///
///   1. an editor-only override (EditorPrefs), used by the DEV scenes and the Uvga tooling so
///      a developer can point at a different install without touching user settings;
///   2. the persisted setting in <c>ao_install.json</c>, which only the login screen writes.
///
/// <see cref="AoInstallPath"/> still owns validation and normalisation; this owns storage.
/// </summary>
public static class AoInstall
{
    const string FileName = "ao_install.json";
    const string EditorOverrideKey = "LostEden.AoInstallPath";

    static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        Formatting = Formatting.Indented
    };

    static SettingsDto _cached;
    static bool _loaded;

    // Fully qualified: this class has its own 'Path' property, which shadows System.IO.Path.
    public static string ConfigPath =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Application.productName,
            FileName);

    /// <summary>The resolved install path, normalised. Empty when nothing is configured.</summary>
    public static string Path
    {
        get
        {
#if UNITY_EDITOR
            string over = AoInstallPath.Normalize(EditorPrefs.GetString(EditorOverrideKey, string.Empty));
            if (AoInstallPath.IsValid(over))
                return over;
#endif
            string saved = AoInstallPath.Normalize(Stored);
            if (AoInstallPath.IsValid(saved))
                return saved;

#if UNITY_EDITOR
            // Nothing valid: hand back whatever is set so a UI can show it for correction.
            return !string.IsNullOrEmpty(over) ? over : saved;
#else
            return saved;
#endif
        }
    }

    public static bool IsConfigured => AoInstallPath.IsValid(Path);

    /// <summary>Persists the install path for the player. Only the login screen should call this.</summary>
    public static void Save(string aoPath)
    {
        EnsureLoaded();
        _cached.AoPath = AoInstallPath.Normalize(aoPath) ?? string.Empty;

        string path = ConfigPath;
        string directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(path, JsonConvert.SerializeObject(_cached, JsonSettings));
    }

#if UNITY_EDITOR
    /// <summary>
    /// Sets the editor-only override. DEV scenes and editor tooling use this so they never
    /// write the player's settings file.
    /// </summary>
    public static void SetEditorOverride(string aoPath)
    {
        EditorPrefs.SetString(EditorOverrideKey, AoInstallPath.Normalize(aoPath) ?? string.Empty);
    }

    public static string GetEditorOverride() => EditorPrefs.GetString(EditorOverrideKey, string.Empty);
#endif

    static string Stored
    {
        get
        {
            EnsureLoaded();
            return _cached.AoPath ?? string.Empty;
        }
    }

    static void EnsureLoaded()
    {
        if (_loaded)
            return;

        _loaded = true;
        _cached = new SettingsDto();

        string path = ConfigPath;
        if (!File.Exists(path))
            return;

        try
        {
            var dto = JsonConvert.DeserializeObject<SettingsDto>(File.ReadAllText(path), JsonSettings);
            if (dto != null)
                _cached = dto;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[AoInstall] Failed to load '{path}': {ex.Message}");
        }
    }

    class SettingsDto
    {
        public string AoPath;
    }
}
