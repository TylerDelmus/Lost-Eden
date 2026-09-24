using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// typeCode 3030 (0xbd6), stock <c>GfxControlGroundGrid_t</c>. The rules are <see cref="GroundGridSim"/>;
/// this lays the grid over the ground (<see cref="EffectGround"/>) and draws it as one mesh in the
/// material, shaded per vertex the way the visual's Update does (<c>100164b4</c>).
///
/// The emitter is the locator's local-mode position (<c>10106306</c>); the visual sits there with no turn
/// (<c>1010e7d5</c> only sets its position). Where the port finds no ground under a vertex it uses the
/// emitter's own height.
///
/// Stock hands the visual (N - 1) triangle strips of 2N vertices, one per row pair, so a vertex shared
/// by two rows is written twice with the same position and colour. This keeps one indexed mesh instead,
/// which produces the same 2(N - 1)^2 triangles from shared vertices.
/// </summary>
public sealed class GfxControlGroundGrid : GfxControl
{
    readonly GroundGridSim _sim;
    readonly Texture2D _texture;
    readonly Mesh _mesh;
    readonly Vector3[] _points;
    readonly Vector3[] _vertices;
    readonly Vector2[] _uvs;
    readonly Color32[] _colours;
    readonly EffectBillboardBatch.MeshDraw _draw;
    Quaternion _turn = Quaternion.identity;
    Vector3 _emitter;
    bool _laid;

    public GroundGridSim Sim => _sim;

    public GfxControlGroundGrid(GfxTweakRecord record, EffectLocator locator, Texture2D texture)
        : base(record, locator)
    {
        _texture = texture;
        _sim = new GroundGridSim(record?.Fields);
        base.SetDuration(_sim.Duration);

        int n = _sim.Size;
        _points = new Vector3[n * n];
        _vertices = new Vector3[n * n];
        _uvs = new Vector2[n * n];
        _colours = new Color32[n * n];
        _mesh = new Mesh { name = "GroundGrid", indexFormat = n * n > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        _mesh.MarkDynamic();
        _draw = new EffectBillboardBatch.MeshDraw
        {
            Mesh = _mesh,
            Texture = texture,
            Additive = _sim.Additive,
            VertexColors = true,
        };

        if (!_sim.Supported || n < 2)
            return;

        var triangles = new int[(n - 1) * (n - 1) * 6];
        int t = 0;
        for (int i = 0; i + 1 < n; i++)
        {
            for (int j = 0; j + 1 < n; j++)
            {
                int a = i * n + j, b = a + 1, c = a + n, d = c + 1;
                triangles[t++] = a; triangles[t++] = c; triangles[t++] = b;
                triangles[t++] = b; triangles[t++] = c; triangles[t++] = d;
            }
        }

        if (Locator == null || !Locator.TryResolve(out Matrix4x4 m))
        {
            ReadyFlag = true;
            return;
        }
        _emitter = (_sim.Flags & 2) != 0 ? (Vector3)m.GetColumn(3) : Vector3.zero;
        Lay();
        _mesh.vertices = _points;
        _mesh.SetTriangles(triangles, 0, calculateBounds: false);
        // 1010eb6e..1010eba8: the init runs one Update, which is what first fills the vertex buffer.
        Build();

        // 1010eb0c: the random turn is chosen after the first lay, so a grid laid once never uses it.
        if ((_sim.Flags & 0x20000) != 0)
            _turn = Quaternion.AngleAxis(Random.value * 360f, Vector3.up);
    }

    /// <summary>1010e7e7..1010e877 / 1010ea73..1010eaf5.</summary>
    void Lay()
    {
        int n = _sim.Size;
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                _sim.Point(i, j, out float x, out float z);
                Vector3 p = _turn * new Vector3(x, 0f, z);
                Vector3 world = _emitter + p;
                float g = EffectGround.HeightAt(world.x, world.y, world.z);
                if (float.IsNaN(g))
                    g = _emitter.y;
                _points[i * n + j] = new Vector3(p.x, g + _sim.Height - _emitter.y, p.z);
            }
        }
        _laid = true;
    }

    /// <summary>
    /// The visual's Update (<c>100164b4</c>): every vertex gets the laid point lifted by the ripple,
    /// its UV, and a colour faded by the mode's fall-off. The phase moves on at the end, as stock does
    /// at <c>1001690b</c>, so the frame just built used the phase it came in with.
    /// </summary>
    void Build(float dt = 0f)
    {
        int n = _sim.Size;
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                int k = i * n + j;
                float t = _sim.Falloff(i, j);
                Vector3 p = _points[k];
                _vertices[k] = new Vector3(p.x, p.y + _sim.RippleHeight(t), p.z);
                _colours[k] = EffectBillboardBatch.ToColor32(_sim.VertexArgb(t));
                _sim.Uv(i, j, out float u, out float v);
                // D3D v runs down the image, Unity's up.
                _uvs[k] = new Vector2(u, 1f - v);
            }
        }
        _mesh.vertices = _vertices;
        _mesh.colors32 = _colours;
        _mesh.uv = _uvs;
        _mesh.RecalculateBounds();
        _sim.AdvancePhase(dt);
    }

    protected override void OnArmed() => Body(0f);

    protected override void OnProcess(float dt) => Body(dt);

    void Body(float dt)
    {
        if (!_sim.Supported)
            return;

        if (Locator == null || !Locator.TryResolve(out Matrix4x4 m))
        {
            if ((_sim.Flags & GroundGridSim.FlagKeep) == 0)
            {
                ReadyFlag = true;
                return;
            }
        }
        else if ((_sim.Flags & GroundGridSim.FlagOnce) == 0)
        {
            _emitter = (_sim.Flags & 2) != 0 ? (Vector3)m.GetColumn(3) : Vector3.zero;
            Lay();
        }

        _sim.Advance(Age, dt);
        // 1010e8d0..1010e908: with every UV rate at zero stock never calls Update again, so the
        // vertices — and the ripple with them — stay exactly as the init left them.
        if (_sim.Animates)
            Build(dt);
    }

    protected override void OnReleased(bool immediate)
    {
        if (_mesh != null)
            Object.Destroy(_mesh);
    }

    public override void CollectMeshes(List<EffectBillboardBatch.MeshDraw> dest, Camera camera)
    {
        if (!IsAlive || dest == null || !_laid || _texture == null)
            return;

        // The record's colour is already in every vertex; what is left is the control's own fade,
        // which stock sends down as the texture factor (SetAlpha, 1001692d).
        _draw.Matrix = Matrix4x4.Translate(_emitter);
        _draw.Color = new Color(1f, 1f, 1f, Mathf.Clamp01(_sim.Alpha));
        dest.Add(_draw);
    }
}
