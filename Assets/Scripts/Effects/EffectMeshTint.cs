using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Applies / clears Highlight (2011) emissive+alpha overrides on all renderers under a root.
/// Uses MaterialPropertyBlock so shared materials stay intact.
/// </summary>
public sealed class EffectMeshTint
{
    static readonly int EmissiveColorId = Shader.PropertyToID("_EmissiveColor");
    static readonly int EmissiveIntensityId = Shader.PropertyToID("_EmissiveIntensity");
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int SpecularColorId = Shader.PropertyToID("_SpecularColor");
    static readonly int UnlitColorId = Shader.PropertyToID("_UnlitColor");

    readonly List<Renderer> _renderers = new List<Renderer>(16);
    readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
    GameObject _root;
    bool _applied;

    public void Bind(GameObject root)
    {
        if (root == _root && _renderers.Count > 0)
            return;

        Clear();
        _root = root;
        if (_root == null)
            return;

        _root.GetComponentsInChildren(true, _renderers);
    }

    public void Apply(Color emissiveRgb, float transparency, bool writeSpecular)
    {
        if (_root == null || _renderers.Count == 0)
            return;

        // Stock SetEmissive(rgb) + SetTransparency(alpha). Drive HDRP emissive with alpha as intensity weight.
        float intensity = Mathf.Lerp(0.15f, 2.5f, Mathf.Clamp01(transparency));
        Color emissive = new Color(
            Mathf.Max(0f, emissiveRgb.r) * intensity,
            Mathf.Max(0f, emissiveRgb.g) * intensity,
            Mathf.Max(0f, emissiveRgb.b) * intensity,
            1f);

        for (int i = 0; i < _renderers.Count; i++)
        {
            Renderer renderer = _renderers[i];
            if (renderer == null)
                continue;

            renderer.GetPropertyBlock(_block);
            _block.SetColor(EmissiveColorId, emissive);
            _block.SetFloat(EmissiveIntensityId, intensity);

            if (writeSpecular)
                _block.SetColor(SpecularColorId, new Color(emissiveRgb.r, emissiveRgb.g, emissiveRgb.b, 1f));

            Material mat = renderer.sharedMaterial;
            if (mat != null && mat.HasProperty(BaseColorId))
            {
                Color baseColor = mat.GetColor(BaseColorId);
                baseColor.a = Mathf.Clamp01(transparency);
                _block.SetColor(BaseColorId, baseColor);
            }
            else if (mat != null && mat.HasProperty(UnlitColorId))
            {
                Color c = mat.GetColor(UnlitColorId);
                c.a = Mathf.Clamp01(transparency);
                _block.SetColor(UnlitColorId, c);
            }

            renderer.SetPropertyBlock(_block);
        }

        _applied = true;
    }

    public void Clear()
    {
        if (!_applied && _renderers.Count == 0)
        {
            _root = null;
            return;
        }

        for (int i = 0; i < _renderers.Count; i++)
        {
            Renderer renderer = _renderers[i];
            if (renderer == null)
                continue;
            renderer.SetPropertyBlock(null);
        }

        _renderers.Clear();
        _root = null;
        _applied = false;
    }
}
