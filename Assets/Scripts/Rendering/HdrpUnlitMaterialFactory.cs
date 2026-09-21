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
        MakeDoubleSided(material);
        HDMaterial.ValidateMaterial(material);
        return material;
    }

    /// <summary>
    /// Transparent additive Unlit (SrcAlpha + One). Used for AO combat / nano FX quads.
    /// </summary>
    public static Material CreateSrcAlphaAdditive(string name = "HdrpUnlitSrcAlphaAdditive")
    {
        Material material = Create(name);
        HDMaterial.SetSurfaceType(material, transparent: true);
        if (material.HasProperty("_BlendMode"))
            material.SetFloat("_BlendMode", 1f);
        if (material.HasProperty("_SrcBlend"))
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend"))
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
        if (material.HasProperty("_ZWrite"))
            material.SetFloat("_ZWrite", 0f);
        MakeDoubleSided(material);
        HDMaterial.ValidateMaterial(material);
        return material;
    }

    /// <summary>
    /// Transparent alpha Unlit (SrcAlpha + InvSrcAlpha). Used for non-additive FX quads.
    /// </summary>
    public static Material CreateAlphaBlend(string name = "HdrpUnlitAlphaBlend")
    {
        Material material = Create(name);
        HDMaterial.SetSurfaceType(material, transparent: true);
        if (material.HasProperty("_BlendMode"))
            material.SetFloat("_BlendMode", 0f);
        if (material.HasProperty("_SrcBlend"))
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend"))
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (material.HasProperty("_ZWrite"))
            material.SetFloat("_ZWrite", 0f);
        MakeDoubleSided(material);
        HDMaterial.ValidateMaterial(material);
        return material;
    }

    /// <summary>
    /// Turns off backface culling, which every FX quad needs and HDRP Unlit does not do by default.
    ///
    /// Stock does the same: the sprite visual's ctor sets D3DRS_CULLMODE to D3DCULL_NONE alongside the
    /// blend states, because an effect quad is oriented to suit its geometry and nothing keeps a
    /// consistent facing. Ours has the same problem — a camera-aligned quad and a quad stretched along
    /// a world axis end up with opposite facings, so with culling on only one of the two ever draws,
    /// and stretched quads (spikes, cords, tracers) silently vanish.
    /// </summary>
    static void MakeDoubleSided(Material material)
    {
        if (material.HasProperty("_DoubleSidedEnable"))
            material.SetFloat("_DoubleSidedEnable", 1f);
        if (material.HasProperty("_CullMode"))
            material.SetFloat("_CullMode", (float)UnityEngine.Rendering.CullMode.Off);
        if (material.HasProperty("_CullModeForward"))
            material.SetFloat("_CullModeForward", (float)UnityEngine.Rendering.CullMode.Off);
        material.EnableKeyword("_DOUBLESIDED_ON");
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
