using UnityEngine;

/// <summary>
/// CPU composites armor over skin using AO green-key transparency (#00FF00).
/// JPEG compression and armor edges rarely decode as exact key green, so matching
/// uses a soft threshold (bright green, low red/blue) instead of an exact compare.
/// </summary>
public static class TextureCompositor
{
    const byte GreenMin = 170;
    const byte RbMax = 80;
    const byte FringeGreenMin = 130;
    const byte GreenLeadMin = 40;

    public static Texture2D BakeGreenKey(Texture2D skin, Texture2D armor, string name = "BakedSkinArmor")
    {
        if (skin == null)
            return null;

        int width = skin.width;
        int height = skin.height;
        if (width <= 0 || height <= 0)
            return null;

        Color32[] skinPixels = skin.GetPixels32();
        Color32[] armorPixels = null;
        Texture2D mismatchedArmor = null;
        if (armor != null)
        {
            if (armor.width == width && armor.height == height)
                armorPixels = armor.GetPixels32();
            else
                mismatchedArmor = armor;
        }

        Color32[] result = BakeGreenKeyPixels(skinPixels, armorPixels, width, height, mismatchedArmor);
        return CreateTexture(result, width, height, name);
    }

    /// <summary>
    /// Thread-safe pixel bake. Pass matching-size armorPixels, or null to copy skin.
    /// When armor size differs, pass the armor Texture2D only for bilinear sampling (main thread).
    /// </summary>
    public static Color32[] BakeGreenKeyPixels(
        Color32[] skinPixels,
        Color32[] armorPixels,
        int width,
        int height,
        Texture2D mismatchedArmor = null)
    {
        if (skinPixels == null || width <= 0 || height <= 0)
            return null;

        var result = new Color32[skinPixels.Length];

        if (armorPixels == null && mismatchedArmor == null)
        {
            System.Array.Copy(skinPixels, result, skinPixels.Length);
        }
        else if (armorPixels != null && armorPixels.Length == skinPixels.Length)
        {
            for (int i = 0; i < result.Length; i++)
            {
                Color32 a = armorPixels[i];
                result[i] = IsGreenKey(a) ? skinPixels[i] : WithOpaque(a);
            }
        }
        else if (mismatchedArmor != null)
        {
            float invW = width > 1 ? 1f / (width - 1) : 0f;
            float invH = height > 1 ? 1f / (height - 1) : 0f;
            for (int y = 0; y < height; y++)
            {
                float v = y * invH;
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    float u = x * invW;
                    Color a = mismatchedArmor.GetPixelBilinear(u, v);
                    int i = row + x;
                    result[i] = IsGreenKey(a) ? skinPixels[i] : WithOpaque(a);
                }
            }
        }
        else
        {
            System.Array.Copy(skinPixels, result, skinPixels.Length);
        }

        if (armorPixels != null || mismatchedArmor != null)
            DespillGreenFringe(result, skinPixels);

        return result;
    }

    public static Texture2D CreateTexture(Color32[] pixels, int width, int height, string name)
    {
        if (pixels == null || width <= 0 || height <= 0)
            return null;

        var baked = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false);
        baked.name = name;
        baked.wrapMode = TextureWrapMode.Repeat;
        baked.filterMode = FilterMode.Bilinear;
        baked.SetPixels32(pixels);
        baked.Apply(updateMipmaps: false, makeNoLongerReadable: true);
        return baked;
    }

    public static bool IsGreenKey(Color32 c)
    {
        if (c.g >= GreenMin && c.r <= RbMax && c.b <= RbMax)
            return true;

        int maxRb = c.r > c.b ? c.r : c.b;
        return c.g >= FringeGreenMin && (c.g - maxRb) >= GreenLeadMin;
    }

    public static bool IsGreenKey(Color c)
    {
        var c32 = new Color32(
            (byte)Mathf.RoundToInt(c.r * 255f),
            (byte)Mathf.RoundToInt(c.g * 255f),
            (byte)Mathf.RoundToInt(c.b * 255f),
            (byte)Mathf.RoundToInt(c.a * 255f));
        return IsGreenKey(c32);
    }

    static void DespillGreenFringe(Color32[] result, Color32[] skinPixels)
    {
        for (int i = 0; i < result.Length; i++)
        {
            if (IsGreenKey(result[i]))
                result[i] = skinPixels[i];
        }
    }

    static Color32 WithOpaque(Color32 c)
    {
        c.a = 255;
        return c;
    }

    static Color32 WithOpaque(Color c) => new Color32(
        (byte)Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255),
        (byte)Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255),
        (byte)Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255),
        255);
}
