using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Applies Highlight's (0x7db) tint to the renderers under a root: stock's
/// <c>RRefFrame_t::SetEmissive</c>, <c>SetTransparency</c> and, in mode 3, <c>SetSpecular</c>.
///
/// The body's materials are opaque <c>HDRP/Lit</c>, and HDRP ignores alpha in the opaque pass, so a
/// property block alone cannot fade anything. While a Highlight is running this swaps each renderer
/// onto a transparent clone of its own material and restores the originals on <see cref="Clear"/>.
/// The clones are made once per renderer and destroyed with the tint.
/// </summary>
public sealed class EffectMeshTint
{
    static readonly int EmissiveColorId = Shader.PropertyToID("_EmissiveColor");
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int SpecularColorId = Shader.PropertyToID("_SpecularColor");
    static readonly int UnlitColorId = Shader.PropertyToID("_UnlitColor");

    sealed class Target
    {
        public Renderer Renderer;
        public Material[] Original;
        public Material[] Clones;
    }

    readonly List<Target> _targets = new List<Target>(16);
    GameObject _root;

    public void Bind(GameObject root)
    {
        if (root == _root && _targets.Count > 0)
            return;

        Clear();
        _root = root;
        if (_root == null)
            return;

        var renderers = new List<Renderer>(16);
        _root.GetComponentsInChildren(true, renderers);
        for (int i = 0; i < renderers.Count; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || renderer.sharedMaterials == null || renderer.sharedMaterials.Length == 0)
                continue;
            _targets.Add(new Target { Renderer = renderer, Original = renderer.sharedMaterials });
        }
    }

    public void Apply(Color emissiveRgb, float transparency, bool writeSpecular)
    {
        if (_root == null || _targets.Count == 0)
            return;

        float alpha = Mathf.Clamp01(transparency);

        for (int i = 0; i < _targets.Count; i++)
        {
            Target target = _targets[i];
            if (target.Renderer == null)
                continue;

            if (target.Clones == null)
                target.Clones = MakeTransparent(target.Original, target.Renderer);

            for (int m = 0; m < target.Clones.Length; m++)
            {
                Material clone = target.Clones[m];
                if (clone == null)
                    continue;

                // Stock writes the ramp's rgb straight into the material's emissive.
                if (clone.HasProperty(EmissiveColorId))
                    clone.SetColor(EmissiveColorId, new Color(emissiveRgb.r, emissiveRgb.g, emissiveRgb.b, 1f));

                if (writeSpecular && clone.HasProperty(SpecularColorId))
                    clone.SetColor(SpecularColorId, new Color(emissiveRgb.r, emissiveRgb.g, emissiveRgb.b, 1f));

                int colourId = clone.HasProperty(BaseColorId) ? BaseColorId
                    : clone.HasProperty(UnlitColorId) ? UnlitColorId : 0;
                if (colourId != 0)
                {
                    Color c = clone.GetColor(colourId);
                    c.a = alpha;
                    clone.SetColor(colourId, c);
                }
            }
        }
    }

    /// <summary>A transparent copy of each material the renderer uses, swapped in for the tint's life.</summary>
    static Material[] MakeTransparent(Material[] source, Renderer renderer)
    {
        var clones = new Material[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            Material original = source[i];
            if (original == null)
                continue;

            var clone = new Material(original) { name = original.name + " (Highlight)" };
            HDMaterial.SetSurfaceType(clone, transparent: true);
            if (clone.HasProperty("_BlendMode"))
                clone.SetFloat("_BlendMode", 0f);   // alpha
            if (clone.HasProperty("_ZWrite"))
                clone.SetFloat("_ZWrite", 0f);
            HDMaterial.ValidateMaterial(clone);
            clones[i] = clone;
        }

        renderer.sharedMaterials = clones;
        return clones;
    }

    public void Clear()
    {
        for (int i = 0; i < _targets.Count; i++)
        {
            Target target = _targets[i];
            if (target.Renderer != null && target.Original != null)
                target.Renderer.sharedMaterials = target.Original;

            if (target.Clones == null)
                continue;
            for (int m = 0; m < target.Clones.Length; m++)
            {
                if (target.Clones[m] != null)
                    Object.Destroy(target.Clones[m]);
            }
        }

        _targets.Clear();
        _root = null;
    }
}
