using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Creates HDRP Unlit materials for background/sky geometry that should not receive lighting.
/// </summary>
public static class HdrpUnlitMaterialFactory
{
    static Shader _unlitShader;

    public static Material Create(string name = "HdrpUnlit")
    {
        EnsureLoaded();
        return new Material(_unlitShader) { name = name };
    }

    public static Material CreateAlphaClip(string name = "HdrpUnlitAlphaClip", float cutoff = 0.5f)
    {
        Material material = Create(name);
        HDMaterial.SetAlphaClipping(material, true);
        HDMaterial.SetAlphaCutoff(material, cutoff);
        HDMaterial.ValidateMaterial(material);
        return material;
    }

    /// <summary>
    /// Transparent additive Unlit (One + One). Used for AO sky/star/cloud background meshes.
    /// </summary>
    public static Material CreateAdditive(string name = "HdrpUnlitAdditive")
    {
        Material material = Create(name);
        HDMaterial.SetSurfaceType(material, transparent: true);
        // HDRP blend modes: 0 Alpha, 1 Additive, 2 Premultiply
        if (material.HasProperty("_BlendMode"))
            material.SetFloat("_BlendMode", 1f);
        if (material.HasProperty("_SrcBlend"))
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
        if (material.HasProperty("_DstBlend"))
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
        if (material.HasProperty("_ZWrite"))
            material.SetFloat("_ZWrite", 0f);
        HDMaterial.ValidateMaterial(material);
        return material;
    }

    static void EnsureLoaded()
    {
        if (_unlitShader != null)
            return;

        _unlitShader = Shader.Find("HDRP/Unlit");
        if (_unlitShader == null)
            throw new System.InvalidOperationException(
                "HdrpUnlitMaterialFactory: Failed to resolve HDRP/Unlit shader.");
    }
}
