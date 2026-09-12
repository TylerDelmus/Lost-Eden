using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Crops atlas cells into standalone textures via GPU Blit (no CPU-readable source required).
/// </summary>
public sealed class EffectAtlasFrames
{
    readonly Dictionary<(EntityId AtlasId, int Cols, int Rows, int Frame), Texture2D> _frames =
        new Dictionary<(EntityId, int, int, int), Texture2D>();

    public Texture2D GetFrame(Texture atlas, int cols, int rows, int frame)
    {
        if (atlas == null)
            return null;

        InferGrid(atlas, ref cols, ref rows);
        cols = Mathf.Max(1, cols);
        rows = Mathf.Max(1, rows);
        int cellCount = cols * rows;
        frame = ((frame % cellCount) + cellCount) % cellCount;

        if (cols == 1 && rows == 1)
            return atlas as Texture2D;

        var key = (atlas.GetEntityId(), cols, rows, frame);
        if (_frames.TryGetValue(key, out Texture2D cached) && cached != null)
            return cached;

        Texture2D cropped = CropBlit(atlas, cols, rows, frame);
        if (cropped != null)
            _frames[key] = cropped;
        return cropped != null ? cropped : atlas as Texture2D;
    }

    /// <summary>
    /// Keep the stock material-table grid when the texture divides cleanly.
    /// Collapse to 1×1 only when dimensions cannot support that grid.
    /// </summary>
    public static void InferGrid(Texture atlas, ref int cols, ref int rows)
    {
        if (atlas == null)
            return;

        int w = atlas.width;
        int h = atlas.height;
        if (w <= 0 || h <= 0)
            return;

        cols = Mathf.Max(1, cols);
        rows = Mathf.Max(1, rows);
        if (cols == 1 && rows == 1)
            return;

        if (w % cols != 0 || h % rows != 0)
        {
            cols = 1;
            rows = 1;
        }
    }

    static Texture2D CropBlit(Texture atlas, int cols, int rows, int frame)
    {
        int cellW = Mathf.Max(1, atlas.width / cols);
        int cellH = Mathf.Max(1, atlas.height / rows);
        int col = frame % cols;
        int row = frame / cols;

        // Graphics.Blit scale/offset: Unity UV origin bottom-left; AO frame 0 is top-left.
        var scale = new Vector2(1f / cols, 1f / rows);
        var offset = new Vector2(col * scale.x, 1f - (row + 1) * scale.y);

        RenderTexture rt = null;
        RenderTexture prev = RenderTexture.active;
        try
        {
            rt = RenderTexture.GetTemporary(cellW, cellH, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(atlas, rt, scale, offset);

            RenderTexture.active = rt;
            var tex = new Texture2D(cellW, cellH, TextureFormat.RGBA32, mipChain: false)
            {
                name = $"{atlas.name}_f{frame}",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            tex.ReadPixels(new Rect(0, 0, cellW, cellH), 0, 0, recalculateMipMaps: false);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            return tex;
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"EffectAtlasFrames: blit crop failed for {atlas.name} ({ex.Message}).");
            return null;
        }
        finally
        {
            RenderTexture.active = prev;
            if (rt != null)
                RenderTexture.ReleaseTemporary(rt);
        }
    }
}
