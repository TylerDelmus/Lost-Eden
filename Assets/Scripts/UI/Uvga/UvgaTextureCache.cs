using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Lazy name-keyed cache of AO Default GUI textures from Graphics.uvgi/uvga.
/// </summary>
public sealed class UvgaTextureCache
{
    const string DefaultRelativeUvgiPath = @"cd_image\gui\Default\Graphics.uvgi";

    readonly ResourceDatabase _database;
    readonly Dictionary<string, Texture2D> _textures =
        new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

    UvgaArchive _archive;
    bool _loadAttempted;
    bool _missingWarned;

    public UvgaTextureCache(ResourceDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public bool IsLoaded => _archive != null;

    public Texture2D Get(string name)
    {
        TryGet(name, out Texture2D texture);
        return texture;
    }

    public bool TryGet(string name, out Texture2D texture)
    {
        texture = null;
        if (string.IsNullOrEmpty(name))
            return false;

        if (_textures.TryGetValue(name, out texture))
            return texture != null;

        if (!EnsureArchive())
            return false;

        if (!_archive.TryGetPngBytes(name, out byte[] png))
            return false;

        texture = Decode(png, name);
        _textures[name] = texture;
        return texture != null;
    }

    bool EnsureArchive()
    {
        if (_archive != null)
            return true;

        if (_loadAttempted)
            return false;

        if (_database?.Rdb == null)
        {
            // Path may be confirmed later at login; do not latch failure or warn.
            return false;
        }

        _loadAttempted = true;

        string uvgiPath = Path.Combine(_database.Rdb.BaseAoPath, DefaultRelativeUvgiPath);
        if (!File.Exists(uvgiPath))
        {
            WarnOnce($"Missing GUI archive index at '{uvgiPath}'.");
            return false;
        }

        try
        {
            _archive = UvgaArchive.Load(uvgiPath);
        }
        catch (Exception ex)
        {
            WarnOnce($"Failed to load GUI archive '{uvgiPath}': {ex.Message}");
            return false;
        }

        return _archive != null;
    }

    void WarnOnce(string message)
    {
        if (_missingWarned)
            return;

        _missingWarned = true;
        Debug.LogWarning($"[UvgaTextureCache] {message}");
    }

    static Texture2D Decode(byte[] pngData, string name)
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
        if (pngData == null || pngData.Length == 0 || !tex.LoadImage(pngData, markNonReadable: false))
        {
            Debug.LogWarning($"[UvgaTextureCache] Failed to decode '{name}'.");
            UnityEngine.Object.Destroy(tex);
            return null;
        }

        tex.name = name;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        return tex;
    }
}
