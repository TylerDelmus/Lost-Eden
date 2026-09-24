using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// typeCode 3034 (0xbda), stock <c>GfxControlShield2_t</c>. The rules are <see cref="Shield2Sim"/>;
/// this bakes the host's skinned mesh and draws the layers.
///
/// Stock's visual takes the host's skinned vertices through the CAT mesh's vertex callback
/// (<c>1001dc74</c>, registered in slot 8 <c>1001deb2</c>), pushes each one out along its normal and
/// draws the result once per layer (<c>1001d3ee</c>), each layer at scale <c>1 + (i / N) * alpha</c>.
/// Unity skins on the GPU, so this bakes each skinned submesh every frame exactly as
/// <see cref="GfxControlShield"/> does, then adds one draw per layer.
///
/// The visual sits on the host CAT node's position, rotation and scale (<c>100d46f2</c> /
/// <c>100d4721</c> / <c>100edc21</c>), which in the port is the renderer's own transform. Stock hides
/// the shield while the host's node is under 0.95 built (<c>1011120f</c>) — the port has no such
/// staged build, so it draws as soon as the renderer is active (Docs §9).
///
/// Field 20 (0-9) puts the shield on an EP03 mech .abiff instead of the host's body; that path is not
/// ported and those records reach no nano (Docs §9).
/// </summary>
public sealed class GfxControlShield2 : GfxControl
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
        public readonly List<EffectBillboardBatch.MeshDraw> Draws = new List<EffectBillboardBatch.MeshDraw>(8);
    }

    readonly Shield2Sim _sim;
    readonly Texture _texture;
    readonly List<Shell> _shells = new List<Shell>(4);

    public Shield2Sim Sim => _sim;

    public GfxControlShield2(GfxTweakRecord record, EffectLocator locator, Texture texture)
        : base(record, locator)
    {
        _sim = new Shield2Sim(record?.Fields);
        base.SetDuration(InfiniteDuration);

        // 10111146: no dynel, no shield.
        if (locator == null || !locator.TryGetHighlightRoot(out GameObject root) || root == null)
        {
            ReadyFlag = true;
            return;
        }

        foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(false))
        {
            if (renderer != null && renderer.sharedMesh != null)
                _shells.Add(new Shell { Renderer = renderer });
        }

        _texture = _sim.HostMaterial ? HostMaterialTexture(_shells) ?? texture : texture;

        if (_shells.Count == 0)
            ReadyFlag = true;
    }

    /// <summary>The host's material 0, as <see cref="GfxControlShield"/> resolves it (flag 0x10000).</summary>
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

    /// <summary>Stock slot 8 is the plain <c>1010340b</c>: +0x10 = seconds.</summary>
    public override void SetDuration(float seconds) => base.SetDuration(seconds);

    /// <summary>Stock slot 6 (<c>10111071</c>): open the 2 s window.</summary>
    protected override void OnTerminateGracefully() => _sim.Terminate(Age);

    protected override void OnProcess(float dt)
    {
        if (!_sim.Step(Age))
        {
            ReadyFlag = true;
            return;
        }

        // 101112e5: the control terminates itself 2 s before the duration.
        if (_sim.ShouldTerminate(Age))
            _sim.Terminate(Age);
    }

    public override void CollectMeshes(List<EffectBillboardBatch.MeshDraw> dest, Camera camera)
    {
        if (!IsAlive || dest == null || _texture == null || _sim.Layers <= 0)
            return;

        bool perVertex = _sim.ColourMode == 1 || _sim.ColourMode == 2;
        float flicker = _sim.Flicker();
        uint rgb = _sim.Argb;
        // Colour mode 0 scales the whole mesh's alpha; above 2 stock leaves it alone.
        int flatAlpha = _sim.ColourMode == 0 ? _sim.ScaleAlpha(flicker) : (int)(rgb >> 24);
        if (!perVertex && flatAlpha <= 0)
            return;

        var colour = perVertex
            ? Color.white
            : new Color(
                ((rgb >> 16) & 0xff) / 255f,
                ((rgb >> 8) & 0xff) / 255f,
                (rgb & 0xff) / 255f,
                (flatAlpha & 0xff) / 255f);

        for (int i = 0; i < _shells.Count; i++)
        {
            Shell shell = _shells[i];
            if (shell.Renderer == null || !shell.Renderer.gameObject.activeInHierarchy)
                continue;
            if (!Build(shell, perVertex, flicker))
                continue;

            Transform t = shell.Renderer.transform;
            for (int layer = 0; layer < _sim.Layers; layer++)
            {
                float amount = _sim.LayerAmount(layer);
                float scale = _sim.LayerScale(layer);
                Vector3 position = t.position;
                // 1001d4ce: flag 0x1000 drops each layer by its own amount.
                if (_sim.DropsLayers)
                    position.y -= amount;

                EffectBillboardBatch.MeshDraw draw = Draw(shell, layer);
                draw.Mesh = shell.Mesh;
                draw.Matrix = Matrix4x4.TRS(position, t.rotation, Vector3.one * scale);
                draw.Texture = _texture;
                draw.Color = colour;
                draw.VertexColors = perVertex;
                draw.Additive = _sim.Additive;
                dest.Add(draw);
            }
        }
    }

    static EffectBillboardBatch.MeshDraw Draw(Shell shell, int layer)
    {
        while (shell.Draws.Count <= layer)
            shell.Draws.Add(new EffectBillboardBatch.MeshDraw());
        return shell.Draws[layer];
    }

    bool Build(Shell shell, bool perVertex, float flicker)
    {
        if (shell.Baked == null)
        {
            shell.Baked = new Mesh { name = "Shield2Baked" };
            shell.Mesh = new Mesh { name = "Shield2Shell" };
            shell.Mesh.MarkDynamic();
        }

        shell.Renderer.BakeMesh(shell.Baked, true);
        shell.Baked.GetVertices(shell.Positions);
        shell.Baked.GetNormals(shell.Normals);
        shell.Baked.GetUVs(0, shell.SourceUvs);
        int count = Mathf.Min(shell.Positions.Count, shell.Normals.Count);
        if (count == 0)
            return false;
        // 1001d428: with 0x40000 stock skips a mesh over 1000 vertices.
        if ((_sim.Flags & Shield2Sim.FlagSkipBigMesh) != 0 && count > Shield2Sim.BigMesh)
            return false;

        if (shell.Triangles == null)
        {
            var all = new List<int>();
            for (int s = 0; s < shell.Baked.subMeshCount; s++)
                all.AddRange(shell.Baked.GetTriangles(s));
            shell.Triangles = all.ToArray();
        }

        shell.Uvs.Clear();
        if (perVertex)
            shell.Colors.Clear();
        uint rgb = _sim.Argb;
        byte r = (byte)(rgb >> 16), g = (byte)(rgb >> 8), b = (byte)rgb;

        for (int v = 0; v < count; v++)
        {
            Vector3 p = shell.Positions[v];
            Vector2 uv = v < shell.SourceUvs.Count ? shell.SourceUvs[v] : Vector2.zero;
            // Stock UVs run down the texture; Unity's run up.
            _sim.Uv(uv.x, 1f - uv.y, p.x, p.y, p.z, out float u, out float tv);
            shell.Uvs.Add(new Vector2(u, 1f - tv));

            if (perVertex)
            {
                int a = _sim.ScaleAlpha(_sim.VertexScale(p.x, p.y, p.z, flicker));
                shell.Colors.Add(new Color32(r, g, b, (byte)a));
            }

            // 1001d669: the shell is the source pushed out along its normal.
            shell.Positions[v] = p + shell.Normals[v] * _sim.Displacement(p.x, p.y, p.z);
        }

        shell.Mesh.Clear();
        shell.Mesh.SetVertices(shell.Positions, 0, count);
        shell.Mesh.SetUVs(0, shell.Uvs, 0, count);
        if (perVertex)
            shell.Mesh.SetColors(shell.Colors, 0, count);
        shell.Mesh.SetTriangles(shell.Triangles, 0, calculateBounds: true);
        return true;
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
