using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Draws the effects' quads, strips and meshes. Stock draws every effect visual as texture x vertex
/// colour, so they all go through the vertex-colour effect shader (<c>Hidden/LostEden/EffectVertexColor</c>,
/// HDRP/Unlit's transparent pass with a vertex colour): additive quads are batched into one mesh per
/// texture with their colours on the vertices; alpha quads stay one draw each, sorted back to front;
/// strips carry their own colours or the strip's one. Lit model materials (<see cref="MeshDraw.Material"/>)
/// and meshes without vertex colours keep HDRP's own shaders. If the shader is missing, everything falls
/// back to HDRP/Unlit.
/// The texture is set on the material (HDRP Unlit often ignores MPB texture overrides).
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

        /// <summary>
        /// A colour per vertex (stock's D3DCOLOR bytes, gamma), multiplied in with <see cref="Color"/> by the
        /// vertex-colour effect shader. Null for a strip in one colour.
        /// </summary>
        public Color32[] Colors;
    }

    /// <summary>A stock D3DCOLOR (0xAARRGGBB) as a vertex colour.</summary>
    public static Color32 ToColor32(uint argb)
        => new Color32((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, (byte)(argb >> 24));

    /// <summary>
    /// A whole mesh drawn with an effect material in one colour, for stock visuals that redraw a
    /// model's own geometry (GfxVisualShield). The owner keeps the mesh.
    /// </summary>
    public sealed class MeshDraw
    {
        public Mesh Mesh;
        public Matrix4x4 Matrix;
        public Texture Texture;
        public Color Color;
        public bool Additive;

        /// <summary>
        /// The texture's tiling and offset (Unity ST: x, y scale, z, w offset) on the textured paths; a model's
        /// UV animation sets it (EffectMesh flag 0x1000).
        /// </summary>
        public Vector4 TextureST = new Vector4(1f, 1f, 0f, 0f);

        /// <summary>
        /// The mesh carries a colour per vertex, multiplied in with <see cref="Color"/> and the texture
        /// (the <c>Hidden/LostEden/EffectVertexColor</c> shader; HDRP/Unlit has no vertex colour).
        /// </summary>
        public bool VertexColors;

        /// <summary>
        /// A lit material of the model's own (EffectMesh with no rendering effect, MParticle). Drawn as is,
        /// with <see cref="Color"/> as its base colour; <see cref="Texture"/> and <see cref="Additive"/>
        /// are then unused.
        /// </summary>
        public Material Material;
    }

    readonly Mesh _quad;
    readonly Material _additive;
    readonly Material _alpha;
    readonly List<Quad> _additiveQuads = new List<Quad>(32);
    readonly List<Quad> _alphaQuads = new List<Quad>(16);
    readonly List<Strip> _strips = new List<Strip>(8);
    readonly List<MeshDraw> _meshes = new List<MeshDraw>(4);
    readonly List<Mesh> _stripMeshes = new List<Mesh>(8);
    readonly Dictionary<int, int[]> _stripTriangles = new Dictionary<int, int[]>();
    readonly Dictionary<int, int[]> _quadTriangles = new Dictionary<int, int[]>();
    readonly List<(float Dist, int Index)> _order = new List<(float, int)>(32);
    // Graphics.DrawMesh only queues a draw, so every quad in a frame would share whatever texture
    // was last written to a single Material. One material per texture keeps each draw honest.
    readonly Dictionary<Texture, Material> _additiveByTexture = new Dictionary<Texture, Material>();
    readonly Dictionary<Texture, Material> _alphaByTexture = new Dictionary<Texture, Material>();
    readonly Material _additiveVertexColor;
    readonly Material _alphaVertexColor;
    readonly Dictionary<Texture, Material> _additiveVertexColorByTexture = new Dictionary<Texture, Material>();
    readonly Dictionary<Texture, Material> _alphaVertexColorByTexture = new Dictionary<Texture, Material>();
    readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();
    bool _cacheLimitWarned;
    readonly int _unlitMapId;
    readonly int _unlitColorId;
    readonly int _unlitMapStId;
    readonly int _baseColorId = Shader.PropertyToID("_BaseColor");
    readonly int _vertexGammaScaleId = Shader.PropertyToID("_VertexGammaScale");

    // Additive quads, batched per texture: the groups this frame and a mesh per group, reused.
    readonly Dictionary<Texture, List<int>> _additiveByTextureThisFrame = new Dictionary<Texture, List<int>>();
    readonly List<List<int>> _groupPool = new List<List<int>>();
    readonly List<Mesh> _batchMeshes = new List<Mesh>(8);
    readonly List<Vector3> _batchPositions = new List<Vector3>(512);
    readonly List<Vector2> _batchUvs = new List<Vector2>(512);
    readonly List<Color> _batchColors = new List<Color>(512);
    readonly List<int> _batchTriangles = new List<int>(768);
    Color[] _stripFill = new Color[64];

    /// <summary>
    /// RGB multiplier on additive quads, so HDRP Bloom has energy to catch. Not stock: DisplaySystem draws
    /// vertex colour × texture unscaled (GfxVisualDiaBill builder FUN_1001105e) and has no bloom; 1 is stock
    /// brightness. At 1 a sprite never passes a bloom threshold of 1, so effects barely glow. Kept at 3
    /// by the user's choice (2026-09-22), trading exact stock colours for bloom.
    /// </summary>
    public static float AdditiveHdrBoost = 3f;

    /// <summary>
    /// Draw through the vertex-colour effect shader (on by default). Off, every draw uses HDRP/Unlit in
    /// one colour as before: for side-by-side checks, and as a fallback.
    /// </summary>
    public static bool VertexColorPath = true;

    /// <summary>Quads submitted last frame, and the draw calls they took. For debug tooling.</summary>
    public int LastQuadCount { get; private set; }
    public int LastQuadDraws { get; private set; }

    Material AdditiveVertexColor => VertexColorPath ? _additiveVertexColor : null;
    Material AlphaVertexColor => VertexColorPath ? _alphaVertexColor : null;

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

        Shader vertexColor = Resources.Load<Shader>("Effects/EffectVertexColor");
        if (vertexColor != null)
        {
            _additiveVertexColor = CreateVertexColor(vertexColor, "EffectVertexColorAdditive", BlendMode.One, _additive);
            _alphaVertexColor = CreateVertexColor(vertexColor, "EffectVertexColorAlpha", BlendMode.OneMinusSrcAlpha, _alpha);
        }
    }

    /// <summary>A vertex-colour material, queued with the HDRP/Unlit material it stands in for.</summary>
    Material CreateVertexColor(Shader shader, string name, BlendMode dst, Material queueLike)
    {
        var material = new Material(shader) { name = name };
        material.SetFloat("_DstBlend", (float)dst);
        material.renderQueue = queueLike.renderQueue;
        SetFullSt(material);
        return material;
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
        _meshes.Clear();
    }

    public void Add(MeshDraw draw)
    {
        if (draw != null && draw.Mesh != null)
            _meshes.Add(draw);
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

        LastQuadCount = _additiveQuads.Count + _alphaQuads.Count;
        if (AdditiveVertexColor != null)
        {
            SubmitAdditiveBatched(camera);
            LastQuadDraws = (_additiveQuads.Count > 0 ? _additiveByTextureThisFrame.Count : 0) + _alphaQuads.Count;
        }
        else
        {
            SubmitList(_additiveQuads, _additive, camera);
            LastQuadDraws = LastQuadCount;
        }
        SubmitList(_alphaQuads, AlphaVertexColor != null ? AlphaVertexColor : _alpha, camera);
        SubmitStrips(camera);
        SubmitMeshes(camera);
    }

    /// <summary>
    /// The property block for a vertex-colour draw: <paramref name="color"/> as it is, and the additive
    /// boost on the vertex colours, in gamma, which is where HDRP/Unlit takes it (the draw colour is
    /// linearised after the multiply).
    /// </summary>
    void SetVertexColorBlock(Color color, bool additive)
    {
        _mpb.Clear();
        _mpb.SetColor(_unlitColorId, color);
        _mpb.SetFloat(_vertexGammaScaleId, additive ? AdditiveHdrBoost : 1f);
    }

    /// <summary>The HDRP/Unlit block: the draw colour, boosted when additive.</summary>
    void SetUnlitBlock(Material material, Color color, bool additive)
    {
        if (additive)
        {
            float boost = AdditiveHdrBoost;
            color = new Color(color.r * boost, color.g * boost, color.b * boost, color.a);
        }
        _mpb.Clear();
        if (material.HasProperty(_unlitColorId))
            _mpb.SetColor(_unlitColorId, color);
    }

    static bool IsVertexColor(Material material) => material != null && material.HasProperty("_VertexGammaScale");

    void SubmitMeshes(Camera camera)
    {
        for (int i = 0; i < _meshes.Count; i++)
        {
            MeshDraw draw = _meshes[i];
            if (draw.Material != null)
            {
                _mpb.Clear();
                if (draw.Material.HasProperty(_baseColorId))
                    _mpb.SetColor(_baseColorId, draw.Color);
                for (int s = 0; s < draw.Mesh.subMeshCount; s++)
                {
                    Graphics.DrawMesh(
                        draw.Mesh, draw.Matrix, draw.Material, 0, camera, s, _mpb, ShadowCastingMode.Off, receiveShadows: true);
                }
                continue;
            }

            Material template = draw.Additive ? _additive : _alpha;
            if (draw.VertexColors)
            {
                Material withColours = draw.Additive ? AdditiveVertexColor : AlphaVertexColor;
                if (withColours != null)
                    template = withColours;
            }
            if (template == null)
                continue;

            Material material = ResolveMaterial(template, draw.Texture);
            if (IsVertexColor(material))
                SetVertexColorBlock(draw.Color, draw.Additive);
            else
                SetUnlitBlock(material, draw.Color, draw.Additive);
            if (material.HasProperty(_unlitMapStId))
                _mpb.SetVector(_unlitMapStId, draw.TextureST);

            for (int s = 0; s < draw.Mesh.subMeshCount; s++)
            {
                Graphics.DrawMesh(
                    draw.Mesh, draw.Matrix, material, 0, camera, s, _mpb, ShadowCastingMode.Off, receiveShadows: false);
            }
        }
    }

    void SubmitStrips(Camera camera)
    {
        for (int i = 0; i < _strips.Count; i++)
        {
            Strip strip = _strips[i];
            Material template = strip.Additive ? AdditiveVertexColor : AlphaVertexColor;
            if (template == null)
                template = strip.Additive ? _additive : _alpha;
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

            Material material = ResolveMaterial(template, strip.Texture);
            bool vertexColor = IsVertexColor(material);
            Color drawColor = strip.Color;
            if (vertexColor)
            {
                if (strip.Colors != null)
                {
                    mesh.SetColors(strip.Colors, 0, strip.Count);
                }
                else
                {
                    // One colour: on the vertices, so the boost multiplies it before it is linearised.
                    if (_stripFill.Length < strip.Count)
                        _stripFill = new Color[Mathf.NextPowerOfTwo(strip.Count)];
                    for (int v = 0; v < strip.Count; v++)
                        _stripFill[v] = strip.Color;
                    mesh.SetColors(_stripFill, 0, strip.Count);
                    drawColor = Color.white;
                }
            }

            mesh.SetTriangles(
                strip.Quads ? QuadTriangles(strip.Count) : StripTriangles(strip.Count), 0, calculateBounds: true);

            if (vertexColor)
                SetVertexColorBlock(drawColor, strip.Additive);
            else
                SetUnlitBlock(material, drawColor, strip.Additive);

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

    /// <summary>
    /// Additive quads, one mesh per texture: each quad's corners are worked out here instead of by a
    /// per-quad matrix, and its colour goes on its four vertices. Additive blending doesn't depend on
    /// draw order, so grouping by texture changes nothing on screen, and a 128-sprite effect is one draw
    /// per frame of its atlas instead of 128.
    /// </summary>
    void SubmitAdditiveBatched(Camera camera)
    {
        if (_additiveQuads.Count == 0)
            return;

        foreach (List<int> group in _additiveByTextureThisFrame.Values)
        {
            group.Clear();
            _groupPool.Add(group);
        }
        _additiveByTextureThisFrame.Clear();

        for (int i = 0; i < _additiveQuads.Count; i++)
        {
            // A quad with no texture drew the material's default white before batching; it still does.
            Texture texture = _additiveQuads[i].Texture != null ? _additiveQuads[i].Texture : Texture2D.whiteTexture;
            if (!_additiveByTextureThisFrame.TryGetValue(texture, out List<int> group))
            {
                if (_groupPool.Count > 0)
                {
                    group = _groupPool[_groupPool.Count - 1];
                    _groupPool.RemoveAt(_groupPool.Count - 1);
                }
                else
                {
                    group = new List<int>(32);
                }
                _additiveByTextureThisFrame[texture] = group;
            }
            group.Add(i);
        }

        Vector3 camPos = camera.transform.position;
        Quaternion camRot = camera.transform.rotation;
        int meshIndex = 0;
        foreach (KeyValuePair<Texture, List<int>> entry in _additiveByTextureThisFrame)
        {
            List<int> group = entry.Value;
            _batchPositions.Clear();
            _batchUvs.Clear();
            _batchColors.Clear();
            _batchTriangles.Clear();
            for (int g = 0; g < group.Count; g++)
            {
                Quad quad = _additiveQuads[group[g]];
                Vector3 pos = quad.Matrix.GetColumn(3);
                float scale = Mathf.Max(0.01f, quad.Scale);
                Matrix4x4 m = BuildQuadMatrix(quad, pos, scale, camPos, camRot);
                int v = _batchPositions.Count;
                // The unit quad's corners and UVs (CreateUnitQuad), and its winding.
                _batchPositions.Add(m.MultiplyPoint3x4(new Vector3(-0.5f, -0.5f, 0f)));
                _batchPositions.Add(m.MultiplyPoint3x4(new Vector3(0.5f, -0.5f, 0f)));
                _batchPositions.Add(m.MultiplyPoint3x4(new Vector3(-0.5f, 0.5f, 0f)));
                _batchPositions.Add(m.MultiplyPoint3x4(new Vector3(0.5f, 0.5f, 0f)));
                _batchUvs.Add(new Vector2(0f, 0f));
                _batchUvs.Add(new Vector2(1f, 0f));
                _batchUvs.Add(new Vector2(0f, 1f));
                _batchUvs.Add(new Vector2(1f, 1f));
                for (int c = 0; c < 4; c++)
                    _batchColors.Add(quad.Color);
                _batchTriangles.Add(v);
                _batchTriangles.Add(v + 2);
                _batchTriangles.Add(v + 1);
                _batchTriangles.Add(v + 2);
                _batchTriangles.Add(v + 3);
                _batchTriangles.Add(v + 1);
            }

            while (_batchMeshes.Count <= meshIndex)
            {
                var created = new Mesh { name = "EffectQuadBatch", indexFormat = IndexFormat.UInt32 };
                created.MarkDynamic();
                _batchMeshes.Add(created);
            }
            Mesh mesh = _batchMeshes[meshIndex++];
            mesh.Clear();
            mesh.SetVertices(_batchPositions);
            mesh.SetUVs(0, _batchUvs);
            mesh.SetColors(_batchColors);
            mesh.SetTriangles(_batchTriangles, 0, calculateBounds: true);

            Material material = ResolveMaterial(_additiveVertexColor, entry.Key);
            SetVertexColorBlock(Color.white, additive: true);
            Graphics.DrawMesh(
                mesh, Matrix4x4.identity, material, 0, camera, 0, _mpb, ShadowCastingMode.Off, receiveShadows: false);
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
            ReferenceEquals(template, _additive) ? _additiveByTexture
            : ReferenceEquals(template, _alpha) ? _alphaByTexture
            : ReferenceEquals(template, _additiveVertexColor) ? _additiveVertexColorByTexture
            : _alphaVertexColorByTexture;

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

    /// <summary>
    /// One draw per quad, back to front: the alpha quads, whose order matters, and the additive ones
    /// when the vertex-colour shader is missing. On the vertex-colour shader the unit quad's white
    /// vertices take the quad's colour from the block.
    /// </summary>
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

            // Colour via MPB so each particle keeps its own tint (material.SetColor would leave every
            // deferred DrawMesh on the last colour written).
            if (IsVertexColor(material))
                SetVertexColorBlock(quad.Color, quad.Additive);
            else
                SetUnlitBlock(material, quad.Color, quad.Additive);

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
        // White, so on the vertex-colour shader the draw's colour is the quad's.
        mesh.colors32 = new[]
        {
            new Color32(255, 255, 255, 255),
            new Color32(255, 255, 255, 255),
            new Color32(255, 255, 255, 255),
            new Color32(255, 255, 255, 255),
        };
        mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        mesh.RecalculateBounds();
        return mesh;
    }
}
