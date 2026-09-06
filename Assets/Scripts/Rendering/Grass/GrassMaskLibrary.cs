using System;
using System.Collections.Generic;
using AODB.Common.RDBObjects;
using UnityEngine;

public enum GrassClassification
{
    /// <summary>Not enough grass in the art to be worth scattering on.</summary>
    NotGrass = 0,

    /// <summary>Transition/edge art: scatter, but only where the texture is grass.</summary>
    Partial = 1,

    /// <summary>Grass over effectively the whole tile: scatter everywhere on it.</summary>
    Full = 2
}

public sealed class GrassTextureInfo
{
    public readonly GrassClassification Classification;

    /// <summary>Null for Full and NotGrass - only Partial tiles need per-point rejection.</summary>
    public readonly GrassMask Mask;

    public readonly float Coverage;

    public GrassTextureInfo(GrassClassification classification, GrassMask mask, float coverage)
    {
        Classification = classification;
        Mask = mask;
        Coverage = coverage;
    }
}

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

    /// <summary>Coverage as a fraction of cells.</summary>
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

public struct GrassClassifySettings
{
    public int MaskResolution;

    /// <summary>Fraction of a cell's pixels that must read as grass for the cell to accept blades.</summary>
    public float CellGrassFraction;

    /// <summary>Coverage at or above which a texture counts as fully grass.</summary>
    public float FullCoverage;

    /// <summary>Coverage below which a texture is ignored entirely.</summary>
    public float MinCoverage;

    public float MinSaturation;

    /// <summary>How far green must lead the other channels. The main defence against grey-green rock.</summary>
    public float MinGreenDominance;

    /// <summary>Always treated as fully grass, whatever the pixels say.</summary>
    public HashSet<int> ForceGrass;

    /// <summary>Never grass, whatever the pixels say.</summary>
    public HashSet<int> Exclude;

    public int DecodeSignature =>
        (MaskResolution * 397) ^
        Mathf.RoundToInt(CellGrassFraction * 1000f) * 31 ^
        Mathf.RoundToInt(MinSaturation * 1000f) * 17 ^
        Mathf.RoundToInt(MinGreenDominance * 1000f) * 7;
}

public static class GrassMaskLibrary
{
    struct Decoded
    {
        public GrassMask Mask;
        public float Coverage;
    }

    // GroundTexture ids are global RDB ids, so a decoded mask stays valid across
    // playfields. Zoning back and forth would otherwise re-decode the same textures every
    // time.
    static readonly Dictionary<int, Decoded> Cache = new();
    static int _cacheSignature;

    public static void ClearCache()
    {
        Cache.Clear();
        _cacheSignature = 0;
    }

    /// <summary>
    /// Decides, from the pixels alone, which of a playfield's ground textures are grass and
    /// how much of each tile they cover. Replaces hand-maintained id lists: AO has far too
    /// many ground textures to enumerate by hand, and the ids are not consistent between
    /// playfields anyway.
    ///
    /// Must run on the main thread (Texture2D decode). The returned dictionary is immutable
    /// afterwards, so the placement pass can read it concurrently from worker threads.
    /// </summary>
    public static Dictionary<int, GrassTextureInfo> Classify(
        ResourceDatabase database,
        IEnumerable<int> textureIds,
        GrassClassifySettings settings,
        bool logTable)
    {
        var result = new Dictionary<int, GrassTextureInfo>();
        if (database == null || textureIds == null)
            return result;

        settings.MaskResolution = Mathf.Clamp(settings.MaskResolution, 2, 64);
        settings.CellGrassFraction = Mathf.Clamp01(settings.CellGrassFraction);

        if (_cacheSignature != settings.DecodeSignature)
        {
            Cache.Clear();
            _cacheSignature = settings.DecodeSignature;
        }

        System.Text.StringBuilder log = logTable ? new System.Text.StringBuilder() : null;
        log?.AppendLine("GrassMaskLibrary: ground texture classification (id, coverage, verdict)");

        foreach (int id in textureIds)
        {
            if (result.ContainsKey(id))
                continue;

            if (settings.Exclude != null && settings.Exclude.Contains(id))
            {
                result[id] = new GrassTextureInfo(GrassClassification.NotGrass, null, 0f);
                log?.AppendLine($"  {id,6}      -    excluded (override)");
                continue;
            }

            if (settings.ForceGrass != null && settings.ForceGrass.Contains(id))
            {
                result[id] = new GrassTextureInfo(GrassClassification.Full, null, 1f);
                log?.AppendLine($"  {id,6}      -    FULL (override)");
                continue;
            }

            if (!TryDecode(database, id, settings, out Decoded decoded))
                continue;

            GrassClassification classification;
            GrassMask mask = null;

            if (decoded.Coverage >= settings.FullCoverage)
            {
                // Near-total coverage: drop the mask so the odd stray non-green cell (a
                // pebble, a dark patch) does not punch holes in an otherwise solid lawn.
                classification = GrassClassification.Full;
            }
            else if (decoded.Coverage >= settings.MinCoverage)
            {
                classification = GrassClassification.Partial;
                mask = decoded.Mask;
            }
            else
            {
                classification = GrassClassification.NotGrass;
            }

            result[id] = new GrassTextureInfo(classification, mask, decoded.Coverage);
            log?.AppendLine($"  {id,6}  {decoded.Coverage * 100f,5:F1}%   {classification}");
        }

        if (log != null)
            Debug.Log(log.ToString());

        return result;
    }

    static bool TryDecode(ResourceDatabase database, int id, GrassClassifySettings settings, out Decoded decoded)
    {
        if (Cache.TryGetValue(id, out decoded))
            return true;

        decoded = default;

        var ground = database.Get<GroundTexture>(id);
        if (ground?.JpgData == null || ground.JpgData.Length == 0)
            return false;

        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
        try
        {
            if (!tex.LoadImage(ground.JpgData, markNonReadable: false))
            {
                Debug.LogWarning($"GrassMaskLibrary: failed to decode GroundTexture {id}.");
                return false;
            }

            GrassMask mask = BuildMask(tex, settings);
            decoded = new Decoded { Mask = mask, Coverage = mask.Coverage };
            Cache[id] = decoded;
            return true;
        }
        finally
        {
            UnityEngine.Object.Destroy(tex);
        }
    }

    static GrassMask BuildMask(Texture2D tex, GrassClassifySettings settings)
    {
        Color32[] pixels = tex.GetPixels32();
        int w = tex.width;
        int h = tex.height;
        int resolution = settings.MaskResolution;
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
                        if (IsGrassColour(pixels[row + x], settings.MinSaturation, settings.MinGreenDominance))
                            green++;
                    }
                }

                cells[cx + cy * resolution] = total > 0 && green / (float)total >= settings.CellGrassFraction;
            }
        }

        return new GrassMask(resolution, cells);
    }

    /// <summary>
    /// AO's grass art runs yellow-green through to fairly blue-green, so the hue band is
    /// wide. The green-dominance test is what stops grey rock and muddy brown from
    /// sneaking in: a desaturated surface can land inside the hue band by accident, but it
    /// cannot have green meaningfully ahead of both red and blue.
    /// </summary>
    static bool IsGrassColour(Color32 c, float minSaturation, float minGreenDominance)
    {
        float r = c.r / 255f;
        float g = c.g / 255f;
        float b = c.b / 255f;

        if (g - Mathf.Max(r, b) < minGreenDominance)
            return false;

        Color.RGBToHSV(new Color(r, g, b), out float hue, out float sat, out float val);
        float degrees = hue * 360f;
        return degrees >= 50f && degrees <= 175f && sat >= minSaturation && val >= 0.08f;
    }
}