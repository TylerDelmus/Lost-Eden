using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml.Linq;
using UnityEngine;

/// <summary>
/// Resolves stock's <c>color</c> attribute, which takes two forms in the shipped GUI XML:
///
///   color="DEFAULT"          a name from cd_image/gui/Default/GUIColors.xml
///   color="0x0080E9F3"       a literal
///
/// The literal form is <b>0x00RRGGBB</b>, not ARGB: every value stock ships has a zero top
/// byte, and treating that as alpha would make the whole UI invisible. The low 24 bits are the
/// colour and alpha is opaque; a view's own <c>alpha</c> attribute carries transparency
/// separately.
///
/// The six names are compiled in as a fallback so the UI resolves without an AO install, and
/// GUIColors.xml overrides them when the path is known.
/// </summary>
public static class AoColors
{
    const string ColorsRelativePath = "cd_image/gui/Default/GUIColors.xml";

    static readonly Dictionary<string, Color> Named = new(System.StringComparer.OrdinalIgnoreCase)
    {
        // Values as shipped in GUIColors.xml.
        { "Default", FromAoHex(0x0080e9f3) },
        { "Selected", FromAoHex(0x00ffffcc) },
        { "Hover", FromAoHex(0x00a5ffdb) },
        { "Text", FromAoHex(0x0099ccaa) },
        { "TextSelected", FromAoHex(0x0099ccaa) },
        { "TextHover", FromAoHex(0x0099ccaa) }
    };

    static bool _loaded;

    /// <summary>Re-reads GUIColors.xml from the AO install, replacing the compiled defaults.</summary>
    public static void LoadFrom(string aoBasePath)
    {
        if (string.IsNullOrEmpty(aoBasePath))
            return;

        string path = Path.Combine(aoBasePath, ColorsRelativePath);
        if (!File.Exists(path))
            return;

        try
        {
            XDocument doc = XDocument.Load(path);
            foreach (XElement e in doc.Descendants("Color"))
            {
                string name = (string)e.Attribute("name");
                string value = (string)e.Attribute("color");
                if (!string.IsNullOrEmpty(name) && TryParseLiteral(value, out Color c))
                    Named[name] = c;
            }

            _loaded = true;
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[AoColors] Failed to read '{path}': {ex.Message}");
        }
    }

    static void EnsureLoaded()
    {
        if (_loaded)
            return;

        _loaded = true;                         // one attempt; defaults stand if it fails
        LoadFrom(AoInstall.Path);
    }

    /// <summary>Resolves a name or a literal. Returns false for null/empty/unknown.</summary>
    public static bool TryParse(string spec, out Color color)
    {
        color = Color.white;
        if (string.IsNullOrWhiteSpace(spec))
            return false;

        string s = spec.Trim();
        if (TryParseLiteral(s, out color))
            return true;

        EnsureLoaded();
        return Named.TryGetValue(s, out color);
    }

    public static Color Resolve(string spec, Color fallback)
    {
        return TryParse(spec, out Color c) ? c : fallback;
    }

    static bool TryParseLiteral(string s, out Color color)
    {
        color = Color.white;
        if (string.IsNullOrEmpty(s))
            return false;

        string hex = s;
        if (hex.StartsWith("0x", System.StringComparison.OrdinalIgnoreCase))
            hex = hex.Substring(2);
        else if (hex[0] == '#')
            hex = hex.Substring(1);
        else
            return false;

        if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v))
            return false;

        color = FromAoHex(v);
        return true;
    }

    static Color FromAoHex(uint v)
    {
        return new Color(
            ((v >> 16) & 0xFF) / 255f,
            ((v >> 8) & 0xFF) / 255f,
            (v & 0xFF) / 255f,
            1f);
    }
}
