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
    readonly Dictionary<LitKey, Material> _litCache = new Dictionary<LitKey, Material>();
    readonly Dictionary<AbiffMaterialDesc, Material> _skyUnlitCache = new Dictionary<AbiffMaterialDesc, Material>();
    readonly Dictionary<int, Texture2D> _textureCache = new Dictionary<int, Texture2D>();

    public AbiffMaterialFactory(ResourceDatabase database)
    {
        _database = database;
    }

    public Material Get(AbiffMaterialDesc desc)
    {
        if (_materialCache.TryGetValue(desc, out Material cached))
            return cached;

        // Descs that differ only in what never reaches the material -- the FAF name, or a shininess
        // that remaps to the same smoothness -- share one Material. On pf 4310 that is 431 -> 259.
        // Distinct Materials cannot share a draw, so every duplicate cost a draw call per pass.
        LitKey key = LitKey.From(desc);
        if (!_litCache.TryGetValue(key, out Material material))
        {
            material = CreateLitMaterial(desc);
            _litCache[key] = material;
        }

        _materialCache[desc] = material;
        return material;
    }

    // ---- texture-array materials (AOLit shader graph) ------------------------

    /// <summary>The AOLit shader graph (<c>Assets/Scripts/Material/AOLit.shadergraph</c>).</summary>
    const string ArrayShaderName = "Shader Graphs/AOLit";

    readonly AbiffTextureArrays _arrays = new AbiffTextureArrays();
    readonly Dictionary<(bool alpha, bool twoSided, int set, Color tint, float smoothness), Material> _arrayMaterials =
        new Dictionary<(bool, bool, int, Color, float), Material>();
    Shader _arrayShader;
    bool _arrayShaderResolved;

    Shader ArrayShader
    {
        get
        {
            if (!_arrayShaderResolved)
            {
                _arrayShader = Shader.Find(ArrayShaderName);
                _arrayShaderResolved = true;
                if (_arrayShader == null)
                    Debug.LogWarning($"AbiffMaterialFactory: '{ArrayShaderName}' not found; statels stay on HDRP/Lit.");
            }
            return _arrayShader;
        }
    }

    /// <summary>
    /// A desc the array path can draw: a base texture and nothing the AOLit graph lacks -- no emission
    /// map and no emissive colour. Everything else stays on <see cref="Get"/>.
    /// </summary>
    public static bool IsArrayCandidate(AbiffMaterialDesc desc)
        => desc.DiffuseTextureId > 0
           && desc.EmissionTextureId <= 0
           && desc.Emissive.r == 0f && desc.Emissive.g == 0f && desc.Emissive.b == 0f;

    /// <summary>Loads every candidate's base texture into the arrays, in one batch.</summary>
    public void PrepareArrays(IEnumerable<AbiffMaterialDesc> descs)
    {
        if (ArrayShader == null)
            return;

        var ids = new List<int>();
        foreach (AbiffMaterialDesc desc in descs)
        {
            if (IsArrayCandidate(desc))
                ids.Add(desc.DiffuseTextureId);
        }

        _arrays.Add(ids, LoadTexture);
    }

    /// <summary>
    /// The shared array material for <paramref name="desc"/> and the slice its texture sits in. One
    /// material per (alpha clip, two-sided, array, tint, smoothness): the texture no longer splits
    /// materials. False when the desc is not a candidate or its texture was never prepared.
    /// </summary>
    public bool TryGetArrayMaterial(AbiffMaterialDesc desc, out Material material, out int slice)
    {
        material = null;
        slice = -1;
        if (ArrayShader == null || !IsArrayCandidate(desc)
            || !_arrays.TryGetSlice(desc.DiffuseTextureId, out int set, out slice))
            return false;

        float smoothness = RemapSmoothness(desc);
        var key = (desc.ApplyAlpha, desc.TwoSided, set, desc.Diffuse, smoothness);
        if (_arrayMaterials.TryGetValue(key, out material))
            return true;

        material = new Material(ArrayShader)
        {
            name = $"AOLitArray_{(set == 0 ? AbiffTextureArrays.SmallSize : AbiffTextureArrays.LargeSize)}"
                   + (desc.ApplyAlpha ? "_Clip" : "") + (desc.TwoSided ? "_2S" : ""),
        };
        material.SetColor("_BaseColor", desc.Diffuse);
        material.SetFloat("_Smoothness", smoothness);

        HDMaterial.SetAlphaClipping(material, desc.ApplyAlpha);
        if (desc.ApplyAlpha)
            HDMaterial.SetAlphaCutoff(material, 0.5f);      // HdrpLitMaterialFactory.CreateAlphaClip's cutoff

        if (desc.TwoSided)
        {
            material.SetFloat("_DoubleSidedEnable", 1f);
            material.SetFloat("_CullMode", (float)CullMode.Off);
            material.SetFloat("_CullModeForward", (float)CullMode.Off);
            material.doubleSidedGI = true;
        }

        HDMaterial.ValidateMaterial(material);
        _arrays.Bind(material, set);
        _arrayMaterials[key] = material;
        return true;
    }

    /// <summary>Everything <see cref="CreateLitMaterial"/> reads from a desc, as it reads it.</summary>
    readonly struct LitKey : IEquatable<LitKey>
    {
        readonly bool _applyAlpha;
        readonly bool _twoSided;
        readonly int _diffuseTexture;
        readonly int _emissionTexture;
        readonly Color _baseColor;
        readonly Color _emissive;
        readonly float _smoothness;

        LitKey(AbiffMaterialDesc desc)
        {
            _applyAlpha = desc.ApplyAlpha;
            _twoSided = desc.TwoSided;
            _diffuseTexture = desc.DiffuseTextureId > 0 ? desc.DiffuseTextureId : 0;
            _emissionTexture = desc.EmissionTextureId > 0 ? desc.EmissionTextureId : 0;
            _baseColor = desc.Diffuse;
            _emissive = desc.Emissive;
            _smoothness = RemapSmoothness(desc);
        }

        public static LitKey From(AbiffMaterialDesc desc) => new LitKey(desc);

        public bool Equals(LitKey other)
            => _applyAlpha == other._applyAlpha
               && _twoSided == other._twoSided
               && _diffuseTexture == other._diffuseTexture
               && _emissionTexture == other._emissionTexture
               && _baseColor == other._baseColor
               && _emissive == other._emissive
               && _smoothness.Equals(other._smoothness);

        public override bool Equals(object obj) => obj is LitKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = _diffuseTexture * 397;
                hash = (hash * 397) ^ _emissionTexture;
                hash = (hash * 397) ^ (_applyAlpha ? 1 : 0);
                hash = (hash * 397) ^ (_twoSided ? 2 : 0);
                hash = (hash * 397) ^ _baseColor.GetHashCode();
                hash = (hash * 397) ^ _emissive.GetHashCode();
                hash = (hash * 397) ^ _smoothness.GetHashCode();
                return hash;
            }
        }
    }

    public Material GetSkyUnlit(AbiffMaterialDesc desc)
    {
        if (_skyUnlitCache.TryGetValue(desc, out Material cached))
            return cached;

        Material material = CreateSkyUnlitMaterial(desc);
        _skyUnlitCache[desc] = material;
        return material;
    }

    /// <summary>An ABIFF texture by AOTexture id (repeat-wrapped, cached), as the materials use it.</summary>
    public Texture2D GetTexture(int texId) => texId > 0 ? LoadTexture(texId) : null;

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

    Material CreateSkyUnlitMaterial(AbiffMaterialDesc desc)
    {
        string name = string.IsNullOrEmpty(desc.Name) ? "AbiffSkyAdditive" : desc.Name + "_SkyAdditive";
        Material material = HdrpUnlitMaterialFactory.CreateAdditive(name);

        Texture2D diffuse = desc.DiffuseTextureId > 0 ? LoadTexture(desc.DiffuseTextureId) : null;
        Texture2D emission = desc.EmissionTextureId > 0 ? LoadTexture(desc.EmissionTextureId) : null;

        // Additive: material opacity (Diffuse.a / FAF opac) is intensity; bake into RGB.
        float intensity = Mathf.Max(0f, desc.Diffuse.a);
        Color tint = new Color(
            desc.Diffuse.r * intensity,
            desc.Diffuse.g * intensity,
            desc.Diffuse.b * intensity,
            1f);
        if (material.HasProperty("_UnlitColor"))
            material.SetColor("_UnlitColor", tint);
        else if (material.HasProperty("_Color"))
            material.SetColor("_Color", tint);

        if (diffuse != null)
        {
            if (material.HasProperty("_UnlitColorMap"))
                material.SetTexture("_UnlitColorMap", diffuse);
            else if (material.HasProperty("_BaseColorMap"))
                material.SetTexture("_BaseColorMap", diffuse);
            else if (material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", diffuse);
        }

        // Sky is already Unlit additive via _UnlitColor. Extra emissive double-counts HDR
        // energy and pulls Automatic Exposure down (washes shadows / weakens sun on meshes).
        HDMaterial.SetEmissiveColor(material, Color.black);
        if (material.HasProperty("_EmissiveIntensity"))
            material.SetFloat("_EmissiveIntensity", 0f);
        _ = emission; // AO emission maps unused for camera-locked sky unlit

        // Camera-locked sky: no depth write, LessEqual depth test.
        // AO sky meshes have mixed winding — always double-sided (cull off).
        if (material.HasProperty("_ZWrite"))
            material.SetFloat("_ZWrite", 0f);
        if (material.HasProperty("_ZTestDepthEqualForOpaque"))
            material.SetInt("_ZTestDepthEqualForOpaque", (int)CompareFunction.LessEqual);
        if (material.HasProperty("_ZTestTransparent"))
            material.SetInt("_ZTestTransparent", (int)CompareFunction.LessEqual);
        if (material.HasProperty("_ZTestGBuffer"))
            material.SetInt("_ZTestGBuffer", (int)CompareFunction.LessEqual);

        // Validate first — it can reset cull from double-sided defaults.
        HDMaterial.ValidateMaterial(material);
        ApplySkyDoubleSided(material);
        return material;
    }

    static void ApplySkyDoubleSided(Material material)
    {
        if (material == null)
            return;

        if (material.HasProperty("_DoubleSidedEnable"))
            material.SetFloat("_DoubleSidedEnable", 1f);
        material.doubleSidedGI = true;

        if (material.HasProperty("_CullMode"))
            material.SetFloat("_CullMode", (float)CullMode.Off);
        if (material.HasProperty("_CullModeForward"))
            material.SetFloat("_CullModeForward", (float)CullMode.Off);
        if (material.HasProperty("_TransparentCullMode"))
            material.SetFloat("_TransparentCullMode", (float)CullMode.Off);
        material.SetInt("_Cull", (int)CullMode.Off);
    }

    public void ApplySkyIntensity(Material material, float intensity)
    {
        if (material == null)
            return;

        intensity = Mathf.Max(0f, intensity);

        if (material.HasProperty("_UnlitColor"))
        {
            Color c = material.GetColor("_UnlitColor");
            c.r *= intensity;
            c.g *= intensity;
            c.b *= intensity;
            material.SetColor("_UnlitColor", c);
        }
        else if (material.HasProperty("_Color"))
        {
            Color c = material.GetColor("_Color");
            c.r *= intensity;
            c.g *= intensity;
            c.b *= intensity;
            material.SetColor("_Color", c);
        }

        HDMaterial.ValidateMaterial(material);
        ApplySkyDoubleSided(material);
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
