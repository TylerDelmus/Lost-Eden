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

        /// <summary>
        /// When set, the quad is the parallelogram <c>Matrix.translation ± AxisX/2 ± AxisY/2</c>, with
        /// UV (0,0) at <c>-AxisX/2 - AxisY/2</c>. For stock visuals whose corners are computed per
        /// sprite (GfxVisualFlareType0); Scale, Height, Stretch and Roll are ignored.
        /// </summary>
        public bool UseAxes;
        public Vector3 AxisX;
        public Vector3 AxisY;
    }

    /// <summary>
    /// A triangle strip in world space with its own texture coordinates, for stock visuals that build
    /// their own geometry (GfxVisualPlasma). Drawn from both sides. The owner keeps the arrays; only
    /// the first <see cref="Count"/> entries are used.
    /// </summary>
    public sealed class Strip
    {
        public Vector3[] Positions;
        public Vector2[] Uvs;
        public int Count;
        public Color Color;
        public Texture Texture;
        public bool Additive;
        /// <summary>Every four vertices are a quad of their own (0 1 2, 1 2 3) instead of one strip.</summary>
        public bool Quads;
    }

    readonly Mesh _quad;
    readonly Material _additive;
    readonly Material _alpha;
    readonly List<Quad> _additiveQuads = new List<Quad>(32);
    readonly List<Quad> _alphaQuads = new List<Quad>(16);
    readonly List<Strip> _strips = new List<Strip>(8);
    readonly List<Mesh> _stripMeshes = new List<Mesh>(8);
    readonly Dictionary<int, int[]> _stripTriangles = new Dictionary<int, int[]>();
    readonly Dictionary<int, int[]> _quadTriangles = new Dictionary<int, int[]>();
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

    /// <summary>
    /// RGB multiplier on additive quads, there so HDRP Bloom picks them up. Not stock: DisplaySystem
    /// draws vertex colour × texture unscaled (GfxVisualDiaBill builder FUN_1001105e) and has no bloom.
    /// 1 reproduces stock brightness.
    /// </summary>
    public static float AdditiveHdrBoost = 3f;

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
        _strips.Clear();
    }

    public void Add(Strip strip)
    {
        if (strip != null && strip.Count >= 3)
            _strips.Add(strip);
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
        SubmitStrips(camera);
    }

    void SubmitStrips(Camera camera)
    {
        for (int i = 0; i < _strips.Count; i++)
        {
            Strip strip = _strips[i];
            Material template = strip.Additive ? _additive : _alpha;
            if (template == null)
                continue;

            while (_stripMeshes.Count <= i)
            {
                var created = new Mesh { name = "EffectStrip" };
                created.MarkDynamic();
                _stripMeshes.Add(created);
            }

            // Graphics.DrawMesh only records the draw, so each strip in a frame needs its own mesh.
            Mesh mesh = _stripMeshes[i];
            mesh.Clear();
            mesh.SetVertices(strip.Positions, 0, strip.Count);
            mesh.SetUVs(0, strip.Uvs, 0, strip.Count);
            mesh.SetTriangles(
                strip.Quads ? QuadTriangles(strip.Count) : StripTriangles(strip.Count), 0, calculateBounds: true);

            Color drawColor = strip.Color;
            if (strip.Additive)
            {
                float boost = AdditiveHdrBoost;
                drawColor = new Color(drawColor.r * boost, drawColor.g * boost, drawColor.b * boost, drawColor.a);
            }

            Material material = ResolveMaterial(template, strip.Texture);
            _mpb.Clear();
            if (material.HasProperty(_unlitColorId))
                _mpb.SetColor(_unlitColorId, drawColor);

            Graphics.DrawMesh(
                mesh,
                Matrix4x4.identity,
                material,
                0,
                camera,
                0,
                _mpb,
                ShadowCastingMode.Off,
                receiveShadows: false);
        }
    }

    /// <summary>Strip order to a triangle list, each triangle wound both ways (D3D cull none).</summary>
    int[] StripTriangles(int count)
    {
        if (_stripTriangles.TryGetValue(count, out int[] cached))
            return cached;

        int triangles = count - 2;
        var indices = new int[triangles * 6];
        for (int t = 0; t < triangles; t++)
        {
            int o = t * 6;
            indices[o] = t;
            indices[o + 1] = t + 1;
            indices[o + 2] = t + 2;
            indices[o + 3] = t;
            indices[o + 4] = t + 2;
            indices[o + 5] = t + 1;
        }
        _stripTriangles[count] = indices;
        return indices;
    }

    /// <summary>Separate quads (0 1 2, 1 2 3 per four vertices) to a triangle list, wound both ways.</summary>
    int[] QuadTriangles(int count)
    {
        if (_quadTriangles.TryGetValue(count, out int[] cached))
            return cached;

        int quads = count / 4;
        var indices = new int[quads * 12];
        for (int q = 0; q < quads; q++)
        {
            int v = q * 4, o = q * 12;
            indices[o] = v;
            indices[o + 1] = v + 1;
            indices[o + 2] = v + 2;
            indices[o + 3] = v + 1;
            indices[o + 4] = v + 2;
            indices[o + 5] = v + 3;
            indices[o + 6] = v;
            indices[o + 7] = v + 2;
            indices[o + 8] = v + 1;
            indices[o + 9] = v + 1;
            indices[o + 10] = v + 3;
            indices[o + 11] = v + 2;
        }
        _quadTriangles[count] = indices;
        return indices;
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
                float boost = AdditiveHdrBoost;
                drawColor = new Color(
                    drawColor.r * boost,
                    drawColor.g * boost,
                    drawColor.b * boost,
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
        if (quad.UseAxes)
        {
            Vector3 facing = Vector3.Cross(quad.AxisX, quad.AxisY);
            facing = facing.sqrMagnitude > 1e-12f ? facing.normalized : camRot * Vector3.forward;
            var m = new Matrix4x4();
            m.SetColumn(0, quad.AxisX);
            m.SetColumn(1, quad.AxisY);
            m.SetColumn(2, facing);
            m.SetColumn(3, new Vector4(pos.x, pos.y, pos.z, 1f));
            return m;
        }

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
