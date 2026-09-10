using UnityEngine;

/// <summary>
/// Shared PNG → Texture2D decode for UVGA payloads (runtime and editor).
/// </summary>
public static class UvgaTextureDecoder
{
    public static Texture2D DecodePng(byte[] pngData, string name)
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
        if (pngData == null || pngData.Length == 0 || !tex.LoadImage(pngData, markNonReadable: false))
        {
            Debug.LogWarning($"[UvgaTextureDecoder] Failed to decode '{name}'.");
            DestroyTexture(tex);
            return null;
        }

        tex.name = name;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Point;
#if UNITY_EDITOR
        tex.hideFlags = HideFlags.DontSave;
#endif
        return tex;
    }

    public static void DestroyTexture(Texture2D texture)
    {
        if (texture == null)
            return;

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            Object.DestroyImmediate(texture);
            return;
        }
#endif
        Object.Destroy(texture);
    }
}
