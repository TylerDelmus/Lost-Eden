using System.Collections.Generic;
using AODB.Common.RDBObjects;
using UnityEngine;

/// <summary>
/// Loads RDB <see cref="IconTexture"/> payloads into cached Texture2Ds for UI icons.
/// </summary>
public sealed class IconTextureCache
{
    readonly ResourceDatabase _database;
    readonly Dictionary<int, Texture2D> _cache = new Dictionary<int, Texture2D>();

    public IconTextureCache(ResourceDatabase database)
    {
        _database = database;
    }

    public Texture2D GetIcon(int iconId)
    {
        if (iconId <= 0)
            return null;

        if (_cache.TryGetValue(iconId, out Texture2D cached))
            return cached;

        IconTexture icon = _database.Get<IconTexture>(iconId);
        Texture2D tex = Decode(icon?.JpgData, $"Icon_{iconId}");
        _cache[iconId] = tex;
        return tex;
    }

    static Texture2D Decode(byte[] imageData, string name)
    {
        if (imageData == null || imageData.Length == 0)
            return null;

        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
        if (!tex.LoadImage(imageData, markNonReadable: false))
        {
            Debug.LogWarning($"IconTextureCache: Failed to decode {name}.");
            Object.Destroy(tex);
            return null;
        }

        tex.name = name;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Point;
        return tex;
    }
}
