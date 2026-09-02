using System;
using System.Collections.Generic;
using AODB.Common.RDBObjects;
using UnityEngine;

/// <summary>
/// A low-resolution boolean "is this part of the tile actually grass" map, baked from a
/// GroundTexture's own pixels.
///
/// Needed because AO's ground art includes transition tiles - half grass, half dirt or
/// rock, in one texture. Scattering at a flat reduced density over such a tile still
/// spreads blades uniformly across the whole tile, including the bare half, which reads
/// as wrong. Sampling the texture instead makes density follow the art.
/// </summary>
public sealed class GrassMask
{
    public readonly int Resolution;
    readonly bool[] _cells;

    public GrassMask(int resolution, bool[] cells)
    {
        Resolution = resolution;
        _cells = cells;
    }

    /// <summary>Coverage as a fraction of cells, for sanity-checking the threshold.</summary>
    public float Coverage
    {
        get
        {
            if (_cells == null || _cells.Length == 0)
                return 0f;

            int hits = 0;
            for (int i = 0; i < _cells.Length; i++)
            {
                if (_cells[i])
                    hits++;
            }

            return hits / (float)_cells.Length;
        }
    }

    /// <summary>
    /// s/t are normalised coordinates in the texture's own, un-rotated space, with
    /// t = 0 at the bottom of the image (matching Texture2D.GetPixels ordering).
    /// Thread-safe once built: nothing mutates after construction.
    /// </summary>
    public bool Sample(float s, float t)
    {
        if (_cells == null || _cells.Length == 0)
            return true;

        int x = Mathf.Clamp((int)(s * Resolution), 0, Resolution - 1);
        int y = Mathf.Clamp((int)(t * Resolution), 0, Resolution - 1);
        return _cells[x + y * Resolution];
    }
}

public static class GrassMaskLibrary
{
    /// <summary>
    /// Decodes each partial-grass GroundTexture once and reduces it to a
    /// resolution x resolution grid of "mostly grass-coloured?" flags.
    ///
    /// Must run on the main thread (Texture2D decode). It is cheap - a handful of small
    /// textures per playfield - and the returned dictionary is immutable afterwards, so
    /// the placement pass can read it concurrently from worker threads.
    /// </summary>
    public static Dictionary<int, GrassMask> Build(
        ResourceDatabase database,
        IEnumerable<int> partialTextureIds,
        int resolution,
        float greenFractionThreshold,
        bool logCoverage)
    {
        var masks = new Dictionary<int, GrassMask>();
        if (database == null || partialTextureIds == null)
            return masks;

        resolution = Mathf.Clamp(resolution, 2, 64);
        greenFractionThreshold = Mathf.Clamp01(greenFractionThreshold);

        foreach (int id in partialTextureIds)
        {
            if (masks.ContainsKey(id))
                continue;

            var ground = database.Get<GroundTexture>(id);
            if (ground?.JpgData == null || ground.JpgData.Length == 0)
            {
                Debug.LogWarning($"GrassMaskLibrary: GroundTexture {id} missing or empty - treating tile as fully grass.");
                continue;
            }

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
            try
            {
                if (!tex.LoadImage(ground.JpgData, markNonReadable: false))
                {
                    Debug.LogWarning($"GrassMaskLibrary: failed to decode GroundTexture {id}.");
                    continue;
                }

                GrassMask mask = BuildMask(tex, resolution, greenFractionThreshold);
                masks[id] = mask;

                if (logCoverage)
                {
                    Debug.Log(
                        $"GrassMaskLibrary: GroundTexture {id} ({tex.width}x{tex.height}) -> " +
                        $"{mask.Coverage * 100f:F1}% grass coverage at {resolution}x{resolution}, " +
                        $"threshold {greenFractionThreshold:F2}.");
                }
            }
            finally
            {
                UnityEngine.Object.Destroy(tex);
            }
        }

        return masks;
    }

    static GrassMask BuildMask(Texture2D tex, int resolution, float greenFractionThreshold)
    {
        Color32[] pixels = tex.GetPixels32();
        int w = tex.width;
        int h = tex.height;
        var cells = new bool[resolution * resolution];

        for (int cy = 0; cy < resolution; cy++)
        {
            int y0 = cy * h / resolution;
            int y1 = Math.Max(y0 + 1, (cy + 1) * h / resolution);

            for (int cx = 0; cx < resolution; cx++)
            {
                int x0 = cx * w / resolution;
                int x1 = Math.Max(x0 + 1, (cx + 1) * w / resolution);

                int total = 0;
                int green = 0;
                for (int y = y0; y < y1; y++)
                {
                    int row = y * w;
                    for (int x = x0; x < x1; x++)
                    {
                        total++;
                        if (IsGrassColour(pixels[row + x]))
                            green++;
                    }
                }

                cells[cx + cy * resolution] = total > 0 && green / (float)total >= greenFractionThreshold;
            }
        }

        return new GrassMask(resolution, cells);
    }

    /// <summary>
    /// HSV green-band test. AO's grass art runs yellow-green through to fairly blue-green,
    /// so the hue band is wide; saturation and value floors reject grey rock and near-black
    /// shadow that happens to sit slightly green.
    /// </summary>
    static bool IsGrassColour(Color32 c)
    {
        Color.RGBToHSV(new Color(c.r / 255f, c.g / 255f, c.b / 255f), out float hue, out float sat, out float val);
        float degrees = hue * 360f;
        return degrees >= 50f && degrees <= 175f && sat >= 0.12f && val >= 0.08f;
    }
}
