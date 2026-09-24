using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// typeCode 3003 (0xbbb), stock <c>_GfxControlShield_t</c>: a textured shell over the host's own
/// skinned mesh. The rules are <see cref="ShieldSim"/>. Stock's GfxVisualShield takes the skinned
/// vertices through <c>CATRender_t::RegisterVertexProcessCallback</c> (<c>1001ce63</c> / <c>1001cd09</c>);
/// Unity skins on the GPU, so this bakes each skinned submesh every frame, pushes the vertices out
/// along their normals, sets their UVs and draws the result with the shield's material in the
/// renderer's frame.
///
/// Every vertex takes the one colour when the template has no wave or ripple (flags 0x800 / 0x2000).
/// With either, each vertex gets its own alpha (<c>1001cbca</c> / <c>1001cc7d</c>) and the shell is drawn
/// with the vertex-colour effect shader. The renderers sit in the dynel's own frame, so their baked
/// positions are stock's model space. With flag 0x10000 the shell takes the host's material 0 instead
/// of field 9's (<c>100edfd6</c>): the texture of the submesh that uses entry 0 of the CAT file's
/// material table.
/// </summary>
public sealed class GfxControlShield : GfxControl
{
    sealed class Shell
    {
        public SkinnedMeshRenderer Renderer;
        public Mesh Baked;
        public Mesh Mesh;
        public int[] Triangles;
        public readonly List<Vector3> Positions = new List<Vector3>(1024);
        public readonly List<Vector3> Normals = new List<Vector3>(1024);
        public readonly List<Vector2> SourceUvs = new List<Vector2>(1024);
        public readonly List<Vector2> Uvs = new List<Vector2>(1024);
        public readonly List<Color32> Colors = new List<Color32>(1024);
        public Vector3[] Source;
        public readonly EffectBillboardBatch.MeshDraw Draw = new EffectBillboardBatch.MeshDraw();
    }

    readonly ShieldSim _sim;
    readonly Texture _texture;
    readonly List<Shell> _shells = new List<Shell>(4);

    public ShieldSim Sim => _sim;

    public GfxControlShield(GfxTweakRecord record, EffectLocator locator, Texture texture)
        : base(record, locator)
    {
        _sim = new ShieldSim(record?.Fields);
        // Stock expiry, with its fade, is replayed here.
        base.SetDuration(InfiniteDuration);

        // Stock readies the control when its dynel is gone (100edd31).
        if (locator == null || !locator.TryGetHighlightRoot(out GameObject root) || root == null)
        {
            ReadyFlag = true;
            return;
        }

        foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(false))
        {
            if (renderer != null && renderer.sharedMesh != null)
            {
                renderer.TryGetComponent(out CatMeshSourceVertices source);
                _shells.Add(new Shell { Renderer = renderer, Source = source != null ? source.Positions : null });
            }
        }

        _texture = _sim.HostMaterial ? HostMaterialTexture(_shells) ?? texture : texture;

        if (_shells.Count == 0)
            ReadyFlag = true;
    }

    /// <summary>
    /// The texture of the host's material 0 (CATRender +0x1d8, kept by material index): the submesh that
    /// uses entry 0 of the CAT file's material table. A body where no submesh uses entry 0 takes the
    /// lowest entry it has, since the port only keeps the materials its submeshes use.
    /// </summary>
    static Texture HostMaterialTexture(List<Shell> shells)
    {
        Texture best = null;
        int bestId = int.MaxValue;
        for (int i = 0; i < shells.Count; i++)
        {
            SkinnedMeshRenderer renderer = shells[i].Renderer;
            if (!renderer.TryGetComponent(out CatMeshSourceVertices source) || source.MaterialId < 0
                || source.MaterialId >= bestId)
                continue;
            Material material = renderer.sharedMaterial;
            Texture texture = material != null ? material.mainTexture : null;
            if (texture == null)
                continue;
            best = texture;
            bestId = source.MaterialId;
        }
        return best;
    }

    /// <summary>Stock slot 8: +0x10 = seconds (a Sequencer sets its window this way).</summary>
    public override void SetDuration(float seconds) => _sim.Duration = seconds;

    /// <summary>Stock slot 6 (<c>100edc9c</c>): start the fade.</summary>
    protected override void OnTerminateGracefully() => _sim.Terminate(Age);

    protected override void OnProcess(float dt)
    {
        // _GfxControl_t::Process readies it once 0 <= duration < age; the body still runs.
        bool expired = _sim.Duration >= 0f && _sim.Duration < Age;
        if (_sim.Step(Age) || expired)
            ReadyFlag = true;
    }

    public override void CollectMeshes(List<EffectBillboardBatch.MeshDraw> dest, Camera camera)
    {
        if (!IsAlive || dest == null || _texture == null)
            return;

        bool perVertex = !_sim.UniformColour;
        int alpha = _sim.UniformAlpha();
        if (alpha <= 0 && !perVertex)
            return;

        uint rgb = _sim.Colour;
        var colour = perVertex
            ? Color.white
            : new Color(
                ((rgb >> 16) & 0xff) / 255f,
                ((rgb >> 8) & 0xff) / 255f,
                (rgb & 0xff) / 255f,
                (alpha & 0xff) / 255f);

        for (int i = 0; i < _shells.Count; i++)
        {
            Shell shell = _shells[i];
            if (shell.Renderer == null || !shell.Renderer.gameObject.activeInHierarchy)
                continue;
            if (!Build(shell, perVertex, alpha))
                continue;

            Transform t = shell.Renderer.transform;
            shell.Draw.Mesh = shell.Mesh;
            shell.Draw.Matrix = Matrix4x4.TRS(t.position, t.rotation, Vector3.one);
            shell.Draw.Texture = _texture;
            shell.Draw.Color = colour;
            shell.Draw.VertexColors = perVertex;
            shell.Draw.Additive = (_sim.Flags & ShieldSim.FlagAdditive) != 0;
            dest.Add(shell.Draw);
        }
    }

    bool Build(Shell shell, bool perVertex, int uniformAlpha)
    {
        if (shell.Baked == null)
        {
            shell.Baked = new Mesh { name = "ShieldBaked" };
            shell.Mesh = new Mesh { name = "ShieldShell" };
            shell.Mesh.MarkDynamic();
        }

        shell.Renderer.BakeMesh(shell.Baked, true);
        shell.Baked.GetVertices(shell.Positions);
        shell.Baked.GetNormals(shell.Normals);
        shell.Baked.GetUVs(0, shell.SourceUvs);
        int count = Mathf.Min(shell.Positions.Count, shell.Normals.Count);
        if (count == 0)
            return false;

        if (shell.Triangles == null)
        {
            var all = new List<int>();
            for (int s = 0; s < shell.Baked.subMeshCount; s++)
                all.AddRange(shell.Baked.GetTriangles(s));
            shell.Triangles = all.ToArray();
        }

        shell.Uvs.Clear();
        float offset = _sim.Offset;
        for (int v = 0; v < count; v++)
        {
            Vector3 p = shell.Positions[v];
            Vector2 uv = v < shell.SourceUvs.Count ? shell.SourceUvs[v] : Vector2.zero;
            // Stock UVs run down the texture; Unity's run up.
            _sim.Uv(uv.x, 1f - uv.y, p.x, p.y, p.z, out float u, out float tv);
            shell.Uvs.Add(new Vector2(u, 1f - tv));
            shell.Positions[v] = p + shell.Normals[v] * offset;
        }

        shell.Mesh.Clear();
        shell.Mesh.SetVertices(shell.Positions, 0, count);
        shell.Mesh.SetUVs(0, shell.Uvs, 0, count);
        if (perVertex)
        {
            BuildColours(shell, count, uniformAlpha);
            shell.Mesh.SetColors(shell.Colors, 0, count);
        }
        shell.Mesh.SetTriangles(shell.Triangles, 0, calculateBounds: true);
        return true;
    }

    /// <summary>
    /// <c>1001cab2</c>..<c>1001cd00</c>: field 10's RGB on every vertex; the alpha is the wave's at the
    /// pushed-out position (0x800) or the uniform fade-in, then rippled over the source position (0x2000).
    /// </summary>
    void BuildColours(Shell shell, int count, int uniformAlpha)
    {
        uint rgb = _sim.Colour;
        byte r = (byte)(rgb >> 16), g = (byte)(rgb >> 8), b = (byte)rgb;
        bool ripple = _sim.Ripple && shell.Source != null;
        shell.Colors.Clear();
        for (int v = 0; v < count; v++)
        {
            int alpha = uniformAlpha;
            if (_sim.Wave)
            {
                Vector3 p = shell.Positions[v];
                alpha = _sim.WaveAlpha(_sim.WaveDistance(p.x, p.y, p.z));
            }
            if (ripple && v < shell.Source.Length)
            {
                Vector3 s = shell.Source[v];
                alpha = _sim.RippleAlpha(alpha & 0xff, s.x, s.y, s.z);
            }
            shell.Colors.Add(new Color32(r, g, b, (byte)alpha));
        }
    }

    protected override void OnReleased(bool immediate)
    {
        for (int i = 0; i < _shells.Count; i++)
        {
            if (_shells[i].Baked != null)
                Object.Destroy(_shells[i].Baked);
            if (_shells[i].Mesh != null)
                Object.Destroy(_shells[i].Mesh);
        }
        _shells.Clear();
    }
}
