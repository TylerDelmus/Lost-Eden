using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Path-based UVGA texture cache (editor preview and any AO-base-path consumer).
/// Does not write textures into the Assets folder.
/// </summary>
public sealed class UvgaPathTextureCache
{
    public const string DefaultRelativeUvgiPath = @"cd_image\gui\Default\Graphics.uvgi";

    readonly Dictionary<string, Texture2D> _textures =
        new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

    string _aoBasePath = string.Empty;
    UvgaArchive _archive;
    bool _loadAttempted;
    bool _missingWarned;

    public string AoBasePath => _aoBasePath;

    public bool IsLoaded => _archive != null;

    public IReadOnlyCollection<string> Names =>
        _archive != null ? _archive.Names : Array.Empty<string>();

    public void SetAoBasePath(string aoBasePath)
    {
        string normalized = NormalizeBasePath(aoBasePath);

        if (string.Equals(_aoBasePath, normalized, StringComparison.OrdinalIgnoreCase))
            return;

        Clear();
        _aoBasePath = normalized;
    }

    public void Reload(string aoBasePath)
    {
        Clear();
        _aoBasePath = string.Empty;
        SetAoBasePath(aoBasePath);
        TryEnsureLoaded();
    }

    static string NormalizeBasePath(string aoBasePath)
    {
        if (string.IsNullOrWhiteSpace(aoBasePath))
            return string.Empty;

        return Path.GetFullPath(
            aoBasePath.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }

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
        {
            // Unity destroys textures on play-mode exit; drop dead entries and reload.
            if (texture != null)
                return true;

            _textures.Remove(name);
        }

        if (!TryEnsureLoaded())
            return false;

        if (!_archive.TryGetPngBytes(name, out byte[] png))
            return false;

        texture = UvgaTextureDecoder.DecodePng(png, name);
        _textures[name] = texture;
        return texture != null;
    }

    public bool TryEnsureLoaded()
    {
        if (_archive != null)
            return true;

        if (_loadAttempted)
            return false;

        if (string.IsNullOrEmpty(_aoBasePath))
            return false;

        _loadAttempted = true;

        string uvgiPath = Path.Combine(_aoBasePath, DefaultRelativeUvgiPath);
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

    public void Clear()
    {
        foreach (KeyValuePair<string, Texture2D> pair in _textures)
            UvgaTextureDecoder.DestroyTexture(pair.Value);

        _textures.Clear();
        _archive = null;
        _loadAttempted = false;
        _missingWarned = false;
    }

    void WarnOnce(string message)
    {
        if (_missingWarned)
            return;

        _missingWarned = true;
        Debug.LogWarning($"[UvgaPathTextureCache] {message}");
    }
}
