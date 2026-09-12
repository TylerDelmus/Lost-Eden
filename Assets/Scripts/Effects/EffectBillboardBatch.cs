using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Camera-facing quads via HDRP Unlit.
/// Color goes through a MaterialPropertyBlock so particles can differ per draw.
/// Texture is still set on the material (HDRP Unlit often ignores MPB texture overrides).
/// </summary>
public sealed class EffectBillboardBatch
{
    public struct Quad
    {
        public Matrix4x4 Matrix;
        public float Scale;
        public Color Color;
        public Texture Texture;
        public bool Additive;
    }

    readonly Mesh _quad;
    readonly Material _additive;
    readonly Material _alpha;
    readonly List<Quad> _additiveQuads = new List<Quad>(32);
    readonly List<Quad> _alphaQuads = new List<Quad>(16);
    readonly List<(float Dist, int Index)> _order = new List<(float, int)>(32);
    readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();
    readonly int _unlitMapId;
    readonly int _unlitColorId;
    readonly int _unlitMapStId;

    public EffectBillboardBatch()
    {
        _quad = CreateUnitQuad();
        _additive = HdrpUnlitMaterialFactory.CreateAdditive("EffectBillboardAdditive");
        _alpha = HdrpUnlitMaterialFactory.CreateAlphaBlend("EffectBillboardAlpha");
        if (_additive.HasProperty("_TransparentSortPriority"))
            _additive.SetFloat("_TransparentSortPriority", 6f);
        if (_alpha.HasProperty("_TransparentSortPriority"))
            _alpha.SetFloat("_TransparentSortPriority", 6f);

        _unlitMapId = Shader.PropertyToID("_UnlitColorMap");
        _unlitColorId = Shader.PropertyToID("_UnlitColor");
        _unlitMapStId = Shader.PropertyToID("_UnlitColorMap_ST");

        // Full-frame UVs; frames are pre-cropped textures.
        SetFullSt(_additive);
        SetFullSt(_alpha);
    }

    void SetFullSt(Material material)
    {
        if (material != null && material.HasProperty(_unlitMapStId))
            material.SetVector(_unlitMapStId, new Vector4(1f, 1f, 0f, 0f));
    }

    public void Clear()
    {
        _additiveQuads.Clear();
        _alphaQuads.Clear();
    }

    public void Add(Quad quad)
    {
        if (quad.Additive)
            _additiveQuads.Add(quad);
        else
            _alphaQuads.Add(quad);
    }

    public void Submit(Camera camera)
    {
        if (camera == null)
            camera = Camera.main;
        if (camera == null)
            return;

        SubmitList(_additiveQuads, _additive, camera);
        SubmitList(_alphaQuads, _alpha, camera);
    }

    void SubmitList(List<Quad> quads, Material material, Camera camera)
    {
        int count = quads.Count;
        if (count == 0 || material == null)
            return;

        Vector3 camPos = camera.transform.position;
        _order.Clear();
        for (int i = 0; i < count; i++)
        {
            Vector3 pos = quads[i].Matrix.GetColumn(3);
            _order.Add(((pos - camPos).sqrMagnitude, i));
        }

        _order.Sort((a, b) => b.Dist.CompareTo(a.Dist));

        Quaternion camRot = camera.transform.rotation;
        for (int o = 0; o < _order.Count; o++)
        {
            Quad quad = quads[_order[o].Index];
            Vector3 pos = quad.Matrix.GetColumn(3);
            float scale = Mathf.Max(0.01f, quad.Scale);
            var matrix = Matrix4x4.TRS(pos, camRot, new Vector3(scale, scale, scale));

            // Texture on the material (MPB texture overrides are unreliable with HDRP Unlit).
            if (quad.Texture != null && material.HasProperty(_unlitMapId))
                material.SetTexture(_unlitMapId, quad.Texture);
            SetFullSt(material);

            // Color via MPB so each particle keeps its own tint (material.SetColor
            // would leave every deferred DrawMesh on the last color written).
            // Additive quads get an HDR boost so Bloom (threshold 0) can catch them —
            // LDR 0–1 Unlit color barely blooms at intensity 0.05.
            Color drawColor = quad.Color;
            if (quad.Additive)
            {
                const float BloomBoost = 3f;
                drawColor = new Color(
                    drawColor.r * BloomBoost,
                    drawColor.g * BloomBoost,
                    drawColor.b * BloomBoost,
                    drawColor.a);
            }

            _mpb.Clear();
            if (material.HasProperty(_unlitColorId))
                _mpb.SetColor(_unlitColorId, drawColor);

            Graphics.DrawMesh(
                _quad,
                matrix,
                material,
                0,
                camera,
                0,
                _mpb,
                ShadowCastingMode.Off,
                receiveShadows: false);
        }
    }

    static Mesh CreateUnitQuad()
    {
        var mesh = new Mesh { name = "EffectBillboardQuad" };
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3(0.5f, -0.5f, 0f),
            new Vector3(-0.5f, 0.5f, 0f),
            new Vector3(0.5f, 0.5f, 0f),
        };
        mesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
        };
        mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        mesh.RecalculateBounds();
        return mesh;
    }
}
