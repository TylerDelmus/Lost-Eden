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

        KeyOutGreen(tex);
        tex.name = name;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Point;
        return tex;
    }

    // The icons have no alpha: what should show through is painted pure green (0,255,0), exactly,
    // with no compression noise around it. Only that exact colour is keyed, so green in the art
    // itself (the nano program icons' grid) stays.
    static void KeyOutGreen(Texture2D tex)
    {
        Color32[] pixels = tex.GetPixels32();
        bool keyed = false;
        for (int i = 0; i < pixels.Length; i++)
        {
            Color32 p = pixels[i];
            if (p.r == 0 && p.g == 255 && p.b == 0)
            {
                pixels[i] = new Color32(0, 0, 0, 0);
                keyed = true;
            }
        }

        if (keyed)
        {
            // LoadImage gives a JPEG an RGB24 texture, which has nowhere to keep the alpha.
            if (tex.format != TextureFormat.RGBA32)
                tex.Reinitialize(tex.width, tex.height, TextureFormat.RGBA32, false);
            tex.SetPixels32(pixels);
            tex.Apply(updateMipmaps: false);
        }
    }
}
