using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;
using FontAsset = UnityEngine.TextCore.Text.FontAsset;
using TextCharacter = UnityEngine.TextCore.Text.Character;

/// <summary>
/// Imports a <c>.pixelfont</c> atlas as a static, bitmap UI Toolkit <see cref="FontAsset"/>.
///
/// A <c>.pixelfont</c> is the <c>pixelfont-1</c> JSON written by aowebui's <c>export_font.py</c>:
/// every glyph already rasterised by GDI to one bit per pixel, through the same
/// <c>CreateFontA</c> + <c>TextOut</c> path as stock <c>FontInfo_t::GetGlyph</c>
/// (GUI.dll 0x1012e828). So the pixels here are the pixels the game draws; nothing in Unity
/// rasterises Verdana, and no font file ships.
///
/// The asset is built so that <c>font-size</c> equal to the cell height (<c>tmHeight</c>) is a
/// 1:1 blit: <c>faceInfo.pointSize</c> is the cell height, the atlas is Alpha8 with point
/// filtering, and the render mode is a bitmap mode so TextCore rounds the baseline. It only
/// renders through the <b>standard</b> text generator (<c>-unity-text-generator: standard</c>);
/// the advanced one shapes from a font file, which these assets deliberately do not have.
///
/// Cell layout in the source: row 0 is the top of the cell, the baseline sits <c>ascent</c>
/// rows down, a glyph's pen starts at column 0, and ink is a CLEAR bit (GDI draws black on a
/// white 1bpp target). Cells are twice the advance wide so overhang survives.
/// </summary>
[ScriptedImporter(1, "pixelfont")]
public sealed class PixelFontImporter : ScriptedImporter
{
    const int AtlasWidth = 256;
    const int Gap = 1;

    struct Cropped
    {
        public uint Unicode;
        public int Advance;
        public int X0, Y0, W, H;   // ink box inside the source cell
        public bool[] Ink;         // W*H, row 0 = top
        public GlyphRect Rect;     // placement in the atlas, y from the bottom
    }

    public override void OnImportAsset(AssetImportContext ctx)
    {
        JObject json = JObject.Parse(File.ReadAllText(ctx.assetPath));
        if ((string)json["format"] != "pixelfont-1")
        {
            ctx.LogImportError($"{ctx.assetPath}: not a pixelfont-1 atlas");
            return;
        }

        string face = (string)json["face"];
        bool bold = (bool?)json["bold"] ?? false;
        bool italic = (bool?)json["italic"] ?? false;
        int cellHeight = (int)json["tmHeight"];
        int ascent = (int)json["ascent"];
        int descent = (int)json["descent"];

        var glyphs = new List<Cropped>();
        foreach (JProperty prop in ((JObject)json["glyphs"]).Properties())
            glyphs.Add(Crop(uint.Parse(prop.Name), (JObject)prop.Value));

        Texture2D atlas = Pack(glyphs, out int atlasHeight);
        atlas.name = Path.GetFileNameWithoutExtension(ctx.assetPath) + " Atlas";

        var material = new Material(BitmapTextShader()) { name = Path.GetFileNameWithoutExtension(ctx.assetPath) + " Material" };
        material.SetTexture("_MainTex", atlas);

        FontAsset font = ScriptableObject.CreateInstance<FontAsset>();
        font.name = Path.GetFileNameWithoutExtension(ctx.assetPath);
#pragma warning disable CS0618 // Static is deprecated, but a pre-rasterised atlas is static by nature
        font.atlasPopulationMode = UnityEngine.TextCore.Text.AtlasPopulationMode.Static;
#pragma warning restore CS0618
        font.atlasTextures = new[] { atlas };
        font.isMultiAtlasTexturesEnabled = false;
        font.material = material;
        SetInternal(font, "m_Version", "1.1.0");
        SetInternal(font, "m_AtlasRenderMode", GlyphRenderMode.RASTER_HINTED);
        SetInternal(font, "m_AtlasWidth", AtlasWidth);
        SetInternal(font, "m_AtlasHeight", atlasHeight);
        SetInternal(font, "m_AtlasPadding", 0);
        SetInternal(font, "m_ClearDynamicDataOnBuild", false);

        Cropped? capX = null, meanX = null;
        int spaceAdvance = 0;
#pragma warning disable CS0618 // glyph/character tables are the standard generator's data
        uint index = 1;
        foreach (Cropped g in glyphs)
        {
            var metrics = new GlyphMetrics(g.W, g.H, g.X0, ascent - g.Y0, g.Advance);
            var glyph = new Glyph(index++, metrics, g.Rect, 1f, 0);
            font.glyphTable.Add(glyph);
            font.characterTable.Add(new TextCharacter(g.Unicode, font, glyph));

            if (g.Unicode == 'X') capX = g;
            if (g.Unicode == 'x') meanX = g;
            if (g.Unicode == ' ') spaceAdvance = g.Advance;
        }
#pragma warning restore CS0618

        float capLine = capX.HasValue ? ascent - capX.Value.Y0 : ascent;
        float meanLine = meanX.HasValue ? ascent - meanX.Value.Y0 : capLine * 0.5f;
        font.faceInfo = new FaceInfo
        {
            familyName = CultureName(face),
            styleName = bold ? (italic ? "Bold Italic" : "Bold") : (italic ? "Italic" : "Regular"),
            pointSize = cellHeight,
            scale = 1f,
            lineHeight = cellHeight,
            ascentLine = ascent,
            capLine = capLine,
            meanLine = meanLine,
            baseline = 0f,
            descentLine = -descent,
            superscriptOffset = ascent,
            superscriptSize = 0.5f,
            subscriptOffset = -descent,
            subscriptSize = 0.5f,
            underlineOffset = -1f,
            underlineThickness = 1f,
            strikethroughOffset = Mathf.Round(meanLine * 0.5f),
            strikethroughThickness = 1f,
            // Stock folds tab to space (GetGlyph), so a tab stop is one space wide.
            tabWidth = spaceAdvance,
        };

        ctx.AddObjectToAsset("font", font);
        ctx.AddObjectToAsset("atlas", atlas);
        ctx.AddObjectToAsset("material", material);
        ctx.SetMainObject(font);

        JArray clipped = (JArray)json["clipped"];
        if (clipped != null && clipped.Count > 0)
            ctx.LogImportWarning($"{ctx.assetPath}: {clipped.Count} glyphs were clipped at the 64px rasteriser cell");
    }

    static Cropped Crop(uint unicode, JObject g)
    {
        int w = (int)g["w"], h = (int)g["h"];
        var rows = (JArray)g["rows"];
        var full = new bool[w * h];
        int x0 = w, y0 = h, x1 = -1, y1 = -1;
        for (int y = 0; y < h; y++)
        {
            byte[] row = HexToBytes((string)rows[y]);
            for (int x = 0; x < w; x++)
            {
                bool ink = (row[x >> 3] & (0x80 >> (x & 7))) == 0;
                if (!ink) continue;
                full[y * w + x] = true;
                if (x < x0) x0 = x;
                if (y < y0) y0 = y;
                if (x > x1) x1 = x;
                if (y > y1) y1 = y;
            }
        }

        var c = new Cropped { Unicode = unicode, Advance = (int)g["adv"] };
        if (x1 < 0)
            return c; // blank glyph: advance only

        c.X0 = x0; c.Y0 = y0; c.W = x1 - x0 + 1; c.H = y1 - y0 + 1;
        c.Ink = new bool[c.W * c.H];
        for (int y = 0; y < c.H; y++)
            for (int x = 0; x < c.W; x++)
                c.Ink[y * c.W + x] = full[(y + y0) * w + x + x0];
        return c;
    }

    /// <summary>Shelf-packs every inked glyph, tallest first, with a gap so nothing can bleed.</summary>
    static Texture2D Pack(List<Cropped> glyphs, out int atlasHeight)
    {
        var order = new List<int>();
        for (int i = 0; i < glyphs.Count; i++)
            if (glyphs[i].W > 0) order.Add(i);
        order.Sort((a, b) => glyphs[b].H != glyphs[a].H ? glyphs[b].H.CompareTo(glyphs[a].H) : glyphs[a].Unicode.CompareTo(glyphs[b].Unicode));

        // First pass: top-down shelf positions.
        var topDown = new Dictionary<int, Vector2Int>();
        int penX = Gap, shelfY = Gap, shelfH = 0;
        foreach (int i in order)
        {
            Cropped g = glyphs[i];
            if (penX + g.W + Gap > AtlasWidth)
            {
                penX = Gap;
                shelfY += shelfH + Gap;
                shelfH = 0;
            }
            topDown[i] = new Vector2Int(penX, shelfY);
            penX += g.W + Gap;
            shelfH = Math.Max(shelfH, g.H);
        }
        atlasHeight = Mathf.NextPowerOfTwo(shelfY + shelfH + Gap);

        var pixels = new byte[AtlasWidth * atlasHeight];
        foreach (int i in order)
        {
            Cropped g = glyphs[i];
            Vector2Int p = topDown[i];
            int bottom = atlasHeight - (p.y + g.H); // texture rows count up from the bottom
            for (int y = 0; y < g.H; y++)
                for (int x = 0; x < g.W; x++)
                    if (g.Ink[y * g.W + x])
                        pixels[(bottom + g.H - 1 - y) * AtlasWidth + p.x + x] = 255;
            g.Rect = new GlyphRect(p.x, bottom, g.W, g.H);
            glyphs[i] = g;
        }

        var tex = new Texture2D(AtlasWidth, atlasHeight, TextureFormat.Alpha8, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            anisoLevel = 0,
        };
        tex.SetPixelData(pixels, 0);
        tex.Apply(false, false);
        return tex;
    }

    static byte[] HexToBytes(string hex)
    {
        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return bytes;
    }

    static string CultureName(string face) =>
        face.Length == 0 ? face : char.ToUpperInvariant(face[0]) + face.Substring(1);

    /// <summary>TextCore's own bitmap text shader, the one it gives dynamic bitmap assets.</summary>
    static Shader BitmapTextShader()
    {
        Type utils = typeof(FontAsset).Assembly.GetType("UnityEngine.TextCore.Text.TextShaderUtilities");
        PropertyInfo prop = utils?.GetProperty("ShaderRef_MobileBitmap", BindingFlags.NonPublic | BindingFlags.Static);
        var shader = prop?.GetValue(null) as Shader;
        return shader != null ? shader : Shader.Find("UI/Default");
    }

    static void SetInternal(FontAsset font, string field, object value)
    {
        FieldInfo f = typeof(FontAsset).GetField(field, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
                      ?? typeof(FontAsset).BaseType?.GetField(field, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
        if (f == null)
            throw new MissingFieldException(nameof(FontAsset), field);
        f.SetValue(font, value);
    }
}
