using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Creates HDRP Lit materials from a Resources reference so player builds keep
/// required shader variants (including alpha clipping).
/// </summary>
public static class HdrpLitMaterialFactory
{
    public const string AlphaClipResourcePath = "Materials/HdrpLitAlphaClip";

    static Material _alphaClipTemplate;
    static Shader _litShader;

    public static Material Create(string name = "HdrpLit")
    {
        EnsureLoaded();
        var material = new Material(_litShader) { name = name };
        return material;
    }

    public static Material CreateAlphaClip(string name = "HdrpLitAlphaClip", float cutoff = 0.5f)
    {
        Material material = Create(name);
        HDMaterial.SetAlphaClipping(material, true);
        HDMaterial.SetAlphaCutoff(material, cutoff);
        HDMaterial.ValidateMaterial(material);
        return material;
    }

    static void EnsureLoaded()
    {
        if (_litShader != null)
            return;

        _alphaClipTemplate = Resources.Load<Material>(AlphaClipResourcePath);
        if (_alphaClipTemplate != null && _alphaClipTemplate.shader != null)
        {
            _litShader = _alphaClipTemplate.shader;
            return;
        }

        _litShader = Shader.Find("HDRP/Lit");
        if (_litShader == null)
            _litShader = Shader.Find("Universal Render Pipeline/Lit");
        if (_litShader == null)
            _litShader = Shader.Find("Standard");

        if (_litShader == null)
            throw new System.InvalidOperationException(
                "HdrpLitMaterialFactory: Failed to resolve Lit shader. Ensure Resources/Materials/HdrpLitAlphaClip exists.");
    }
}
