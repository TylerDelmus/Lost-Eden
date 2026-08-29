using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine;

public static class DimensionCatalog
{
    const string FileName = "dimensions.json";

    static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver()
    };

    static List<DimensionInfo> _dimensions;

    public static string ConfigPath =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", FileName));

    public static IReadOnlyList<DimensionInfo> All
    {
        get
        {
            EnsureLoaded();
            return _dimensions;
        }
    }

    public static DimensionInfo Get(string id)
    {
        EnsureLoaded();

        DimensionInfo dimension = _dimensions.FirstOrDefault(d =>
            d.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

        if (dimension == null)
        {
            throw new Exception(
                $"Unknown dimension id '{id}'. Known: {string.Join(", ", _dimensions.Select(d => d.Id))}");
        }

        return dimension;
    }

    static void EnsureLoaded()
    {
        if (_dimensions != null)
            return;

        string path = ConfigPath;
        if (!File.Exists(path))
            throw new Exception($"Missing dimension config at '{path}' (expected next to the app).");

        string json = File.ReadAllText(path);
        var dto = JsonConvert.DeserializeObject<DimensionCatalogDto>(json, JsonSettings);
        if (dto?.Dimensions == null || dto.Dimensions.Count == 0)
            throw new Exception($"'{path}' contains no dimensions.");

        _dimensions = dto.Dimensions;
        Debug.Log($"[Network] Loaded {_dimensions.Count} dimension(s) from {path}");
    }
}

public class DimensionInfo
{
    public string Id;
    public string Name;
    public string Host;
    public int Port;
    public string ClientVersion;
    public string PrivateKey;
    public string PublicKey;
}

class DimensionCatalogDto
{
    public List<DimensionInfo> Dimensions;
}
