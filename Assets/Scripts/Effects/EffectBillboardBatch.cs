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

        /// <summary>Width. Also the height unless <see cref="Height"/> is set.</summary>
        public float Scale;

        /// <summary>
        /// Height, when it differs from the width. Stock sprites carry two independent size
        /// channels (template fields 12-15), and most Flare templates are strongly elongated —
        /// 46002's hand spikes are 0.05 wide by 1.0 long. Zero means "square, use Scale".
        /// Ignored when <see cref="Stretch"/> is set, which supplies the length directly.
        /// </summary>
        public float Height;

        public Color Color;
        public Texture Texture;
        public bool Additive;

        /// <summary>
        /// World-space long axis, magnitude = length. When non-zero the quad is stretched along it
        /// and spun about it to face the camera, instead of drawn as a square camera-facing quad.
        /// Tracers need this: stock builds them as a strip running from one point to another, and
        /// their textures are tall tapered streaks that only read correctly along that axis.
        /// <see cref="Matrix"/>'s translation is the centre of the stretched quad.
        ///
        /// Only use it where the long axis genuinely is a world direction. An elongated quad pointed
        /// end-on at the camera collapses to nothing, so sprites — which are emitted in every
        /// direction — spin in screen space via <see cref="Roll"/> instead.
        /// </summary>
        public Vector3 Stretch;

        /// <summary>
        /// Rotation about the view axis, in degrees. Stock sprites are screen-facing quads carrying
        /// their own angle: <c>InitSpriteDefault</c> seeds three random values per sprite and
        /// <c>ProcessSprites</c> animates one of them. That is what lets a fan of thin streaks read
        /// as spiky plasma while staying visible from every camera angle.
        /// </summary>
        public float Roll;
    }

    readonly Mesh _quad;
    readonly Material _additive;
    readonly Material _alpha;
    readonly List<Quad> _additiveQuads = new List<Quad>(32);
    readonly List<Quad> _alphaQuads = new List<Quad>(16);
    readonly List<(float Dist, int Index)> _order = new List<(float, int)>(32);
    // Graphics.DrawMesh only queues a draw, so every quad in a frame would share whatever texture
    // was last written to a single Material. One material per texture keeps each draw honest.
    readonly Dictionary<Texture, Material> _additiveByTexture = new Dictionary<Texture, Material>();
    readonly Dictionary<Texture, Material> _alphaByTexture = new Dictionary<Texture, Material>();
    readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();
    bool _cacheLimitWarned;
    readonly int _unlitMapId;
    readonly int _unlitColorId;
    readonly int _unlitMapStId;

    /// <summary>Distinct frame textures per blend mode before we stop cloning materials.</summary>
    const int MaxCachedMaterials = 512;

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

    /// <summary>
    /// Returns a material bound to <paramref name="texture"/>. Writing the texture onto a shared
    /// material inside the submit loop does not work: Graphics.DrawMesh defers rendering, so all
    /// queued quads would resolve to the last texture written. Because the draws are sorted by
    /// distance, which quad won depended on the camera angle, and sprites would flip to another
    /// effect's frame — a muzzleflash or plume cone showing up on unrelated particles.
    /// </summary>
    Material ResolveMaterial(Material template, Texture texture)
    {
        if (template == null || texture == null)
            return template;

        Dictionary<Texture, Material> cache =
            ReferenceEquals(template, _additive) ? _additiveByTexture : _alphaByTexture;

        if (cache.TryGetValue(texture, out Material cached) && cached != null)
            return cached;

        if (cache.Count >= MaxCachedMaterials)
        {
            if (!_cacheLimitWarned)
            {
                _cacheLimitWarned = true;
                Debug.LogWarning(
                    $"[Effects] billboard material cache reached {MaxCachedMaterials} textures; "
                    + "falling back to the shared material, so some quads may draw the wrong frame.");
            }
            return template;
        }

        var material = new Material(template) { name = $"{template.name}_{texture.name}" };
        material.SetTexture(_unlitMapId, texture);
        SetFullSt(material);
        cache[texture] = material;
        return material;
    }

    void SubmitList(List<Quad> quads, Material template, Camera camera)
    {
        int count = quads.Count;
        if (count == 0 || template == null)
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
            Matrix4x4 matrix = BuildQuadMatrix(quad, pos, scale, camPos, camRot);
            Material material = ResolveMaterial(template, quad.Texture);

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

    /// <summary>
    /// Square camera-facing quad, or — when the quad carries a <see cref="Quad.Stretch"/> axis —
    /// a quad whose local Y runs along that axis at its full length, rotated about the axis so its
    /// face still points at the camera. The unit quad's V runs along local Y, which is what maps a
    /// tall streak texture onto the direction of travel.
    /// </summary>
    static Matrix4x4 BuildQuadMatrix(Quad quad, Vector3 pos, float scale, Vector3 camPos, Quaternion camRot)
    {
        float lengthSq = quad.Stretch.sqrMagnitude;
        if (lengthSq < 1e-8f)
        {
            float height = quad.Height > 0f ? quad.Height : scale;
            Quaternion rotation = quad.Roll != 0f
                ? camRot * Quaternion.AngleAxis(quad.Roll, Vector3.forward)
                : camRot;
            return Matrix4x4.TRS(pos, rotation, new Vector3(scale, height, 1f));
        }

        float length = Mathf.Sqrt(lengthSq);
        Vector3 axis = quad.Stretch / length;

        // Edge-on to the camera the normal is undefined; fall back to the camera's own up.
        Vector3 normal = Vector3.Cross(axis, camPos - pos);
        if (normal.sqrMagnitude < 1e-8f)
            normal = Vector3.Cross(axis, camRot * Vector3.up);
        if (normal.sqrMagnitude < 1e-8f)
            return Matrix4x4.TRS(pos, camRot, new Vector3(scale, length, 1f));

        // The unit quad's triangles wind so its front face looks down -Z, and the camera-aligned path
        // above orients local +Z along the camera's forward, which leaves that front face pointing back
        // at the viewer. Matching it here means taking the axis component that points away from the
        // camera, not towards it, so a stretched quad has the same facing as every other one.
        Vector3 awayFromCamera = Vector3.Cross(axis, normal.normalized);
        return Matrix4x4.TRS(
            pos,
            Quaternion.LookRotation(awayFromCamera, axis),
            new Vector3(scale, length, 1f));
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
