using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine;

static class LoginPreferences
{
    const string FileName = "login_preferences.json";

    public const int DefaultPlayfieldId = 4582;

    static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        Formatting = Formatting.Indented
    };

    static PrefsDto _cached;
    static bool _loaded;

    public static string ConfigPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Application.productName,
            FileName);

    public static void Save(string username, string dimensionId)
    {
        EnsureLoaded();
        _cached.Username = username ?? string.Empty;
        _cached.DimensionId = dimensionId ?? string.Empty;
        Write();
    }

    public static void SaveAoPath(string aoPath)
    {
        EnsureLoaded();
        _cached.AoPath = aoPath ?? string.Empty;
        Write();
    }

    public static string GetUsername()
    {
        EnsureLoaded();
        return _cached.Username ?? string.Empty;
    }

    public static string GetDimensionId()
    {
        EnsureLoaded();
        return _cached.DimensionId ?? string.Empty;
    }

    public static string GetAoPath()
    {
        EnsureLoaded();
        return _cached.AoPath ?? string.Empty;
    }

    public static int GetPlayfieldId() => DefaultPlayfieldId;

    static void Write()
    {
        string path = ConfigPath;
        string directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(path, JsonConvert.SerializeObject(_cached, JsonSettings));
    }

    static void EnsureLoaded()
    {
        if (_loaded)
            return;

        _loaded = true;
        _cached = new PrefsDto();

        string path = ConfigPath;
        if (!File.Exists(path))
            return;

        try
        {
            string json = File.ReadAllText(path);
            var dto = JsonConvert.DeserializeObject<PrefsDto>(json, JsonSettings);
            if (dto != null)
                _cached = dto;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[LoginPreferences] Failed to load '{path}': {ex.Message}");
        }
    }

    class PrefsDto
    {
        public string Username;
        public string DimensionId;
        public string AoPath;
    }
}
