using System;
using System.Collections.Generic;
using AODB.Common.RDBObjects;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

public sealed class AbiffMaterialFactory
{
    // Legacy shin → HDRP smoothness: quadratic remap then hard cap.
    // Old D3D shin values read too glossy under physically based lighting.
    const float SmoothnessPower = 2f;
    const float SmoothnessCap = 0.35f;

    const float SpecularAaVariance = 0.15f;
    const float SpecularAaThreshold = 0.2f;

    readonly ResourceDatabase _database;
    readonly Dictionary<AbiffMaterialDesc, Material> _materialCache = new Dictionary<AbiffMaterialDesc, Material>();
    readonly Dictionary<int, Texture2D> _textureCache = new Dictionary<int, Texture2D>();

    public AbiffMaterialFactory(ResourceDatabase database)
    {
        _database = database;
    }

    public Material Get(AbiffMaterialDesc desc)
    {
        if (_materialCache.TryGetValue(desc, out Material cached))
            return cached;

        Material material = CreateLitMaterial(desc);
        _materialCache[desc] = material;
        return material;
    }

    Texture2D LoadTexture(int texId)
    {
        if (_textureCache.TryGetValue(texId, out Texture2D cached))
            return cached;

        AOTexture aoTex = _database.Get<AOTexture>(texId);
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: true);
        if (aoTex?.JpgData != null && aoTex.JpgData.Length > 0)
        {
            if (!tex.LoadImage(aoTex.JpgData, markNonReadable: true))
                Debug.LogWarning($"AbiffMaterialFactory: Failed to decode AOTexture {texId}.");
        }

        tex.name = $"AOTexture_{texId}";
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;
        _textureCache[texId] = tex;
        return tex;
    }

    Material CreateLitMaterial(AbiffMaterialDesc desc)
    {
        string name = string.IsNullOrEmpty(desc.Name) ? "AbiffMat" : desc.Name;
        Material material = desc.ApplyAlpha
            ? HdrpLitMaterialFactory.CreateAlphaClip(name)
            : HdrpLitMaterialFactory.Create(name);

        Texture2D diffuse = desc.DiffuseTextureId > 0 ? LoadTexture(desc.DiffuseTextureId) : null;
        Texture2D emission = desc.EmissionTextureId > 0 ? LoadTexture(desc.EmissionTextureId) : null;

        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", desc.Diffuse);
        else if (material.HasProperty("_Color"))
            material.SetColor("_Color", desc.Diffuse);

        if (diffuse != null)
        {
            if (material.HasProperty("_BaseColorMap"))
                material.SetTexture("_BaseColorMap", diffuse);
            else if (material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", diffuse);
        }

        float smoothness = RemapSmoothness(desc);
        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", smoothness);
        if (material.HasProperty("_Metallic"))
            material.SetFloat("_Metallic", 0f);

        bool isHdrp = material.shader != null && material.shader.name.StartsWith("HDRP/", StringComparison.Ordinal);

        if (isHdrp)
        {
            if (material.HasProperty("_EnableGeometricSpecularAA"))
            {
                material.SetFloat("_EnableGeometricSpecularAA", 1f);
                if (material.HasProperty("_SpecularAAScreenSpaceVariance"))
                    material.SetFloat("_SpecularAAScreenSpaceVariance", SpecularAaVariance);
                if (material.HasProperty("_SpecularAAThreshold"))
                    material.SetFloat("_SpecularAAThreshold", SpecularAaThreshold);
                material.EnableKeyword("_ENABLE_GEOMETRIC_SPECULAR_AA");
            }

            HDMaterial.SetEmissiveColor(material, desc.Emissive);
        }
        else if (material.HasProperty("_EmissiveColor"))
            material.SetColor("_EmissiveColor", desc.Emissive);
        else if (material.HasProperty("_EmissionColor"))
            material.SetColor("_EmissionColor", desc.Emissive);

        if (emission != null)
        {
            if (material.HasProperty("_EmissiveColorMap"))
                material.SetTexture("_EmissiveColorMap", emission);
            else if (material.HasProperty("_EmissionMap"))
                material.SetTexture("_EmissionMap", emission);
        }

        if (desc.ApplyAlpha && !isHdrp)
        {
            if (material.HasProperty("_AlphaClip"))
                material.SetFloat("_AlphaClip", 1f);
            else if (material.HasProperty("_Mode"))
                material.SetFloat("_Mode", 1f); // Cutout
        }

        if (desc.TwoSided)
        {
            if (material.HasProperty("_DoubleSidedEnable"))
                material.SetFloat("_DoubleSidedEnable", 1f);
            material.doubleSidedGI = true;
            if (material.HasProperty("_CullMode"))
                material.SetFloat("_CullMode", (float)CullMode.Off);
            if (material.HasProperty("_CullModeForward"))
                material.SetFloat("_CullModeForward", (float)CullMode.Off);
            material.SetInt("_Cull", (int)CullMode.Off);
        }

        if (isHdrp)
            HDMaterial.ValidateMaterial(material);

        return material;
    }

    /// <summary>
    /// aogltf: roughness = 1 - shin/128 ⇒ linear smoothness = shin/128.
    /// Softened with a quadratic curve and hard cap for HDRP.
    /// </summary>
    static float RemapSmoothness(AbiffMaterialDesc desc)
    {
        if (!desc.SpecularEnabled)
            return 0f;

        float t = Mathf.Clamp01(desc.Shininess / 128f);
        return Mathf.Min(Mathf.Pow(t, SmoothnessPower) * SmoothnessCap, SmoothnessCap);
    }
}
