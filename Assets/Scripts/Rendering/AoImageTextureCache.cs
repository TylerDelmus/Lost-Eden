using System.Collections.Generic;
using AODB.Common.RDBObjects;
using UnityEngine;

/// <summary>
/// Loads AOTexture / SkinTexture JPEG payloads into readable Texture2Ds for baking.
/// </summary>
public sealed class AoImageTextureCache
{
    readonly ResourceDatabase _database;
    readonly Dictionary<int, Texture2D> _aoCache = new Dictionary<int, Texture2D>();
    readonly Dictionary<int, Texture2D> _skinCache = new Dictionary<int, Texture2D>();

    public AoImageTextureCache(ResourceDatabase database)
    {
        _database = database;
    }

    public Texture2D GetAoTexture(int textureId)
    {
        if (textureId <= 0)
            return null;

        if (_aoCache.TryGetValue(textureId, out Texture2D cached))
            return cached;

        AOTexture aoTex = _database.Get<AOTexture>(textureId);
        Texture2D tex = Decode(aoTex?.JpgData, $"AOTexture_{textureId}");
        _aoCache[textureId] = tex;
        return tex;
    }

    public Texture2D GetSkinTexture(int textureId)
    {
        if (textureId <= 0)
            return null;

        if (_skinCache.TryGetValue(textureId, out Texture2D cached))
            return cached;

        SkinTexture skinTex = _database.Get<SkinTexture>(textureId);
        Texture2D tex = Decode(skinTex?.JpgData, $"SkinTexture_{textureId}");
        _skinCache[textureId] = tex;
        return tex;
    }

    static Texture2D Decode(byte[] jpgData, string name)
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
        if (jpgData != null && jpgData.Length > 0)
        {
            if (!tex.LoadImage(jpgData, markNonReadable: false))
                Debug.LogWarning($"AoImageTextureCache: Failed to decode {name}.");
        }

        tex.name = name;
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;
        return tex;
    }
}
