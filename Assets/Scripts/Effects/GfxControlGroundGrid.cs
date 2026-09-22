using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// typeCode 3030 (0xbd6), stock <c>GfxControlGroundGrid_t</c>. The rules are <see cref="GroundGridSim"/>;
/// this lays the grid over the ground (<see cref="EffectGround"/>) and draws it as one mesh in the
/// material. Only visual mode 0 (a uniform colour) is drawn; modes 1 and 2 draw nothing yet.
///
/// The emitter is the locator's local-mode position (<c>10106306</c>); the visual sits there with no turn
/// (<c>1010e7d5</c> only sets its position). Where the port finds no ground under a vertex it uses the
/// emitter's own height.
/// </summary>
public sealed class GfxControlGroundGrid : GfxControl
{
    readonly GroundGridSim _sim;
    readonly Texture2D _texture;
    readonly Mesh _mesh;
    readonly Vector3[] _vertices;
    readonly Vector2[] _uvs;
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
        _vertices = new Vector3[n * n];
        _uvs = new Vector2[n * n];
        _mesh = new Mesh { name = "GroundGrid", indexFormat = n * n > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        _mesh.MarkDynamic();
        _draw = new EffectBillboardBatch.MeshDraw { Mesh = _mesh, Texture = texture, Additive = _sim.Additive };

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
        _mesh.SetTriangles(triangles, 0, calculateBounds: false);
        _mesh.RecalculateBounds();
        UpdateUvs();

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
                _vertices[i * n + j] = new Vector3(p.x, g + _sim.Height - _emitter.y, p.z);
            }
        }
        _mesh.vertices = _vertices;
        _laid = true;
    }

    void UpdateUvs()
    {
        int n = _sim.Size;
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                _sim.Uv(i, j, out float u, out float v);
                _uvs[i * n + j] = new Vector2(u, 1f - v);
            }
        }
        _mesh.uv = _uvs;
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

        float uScale = _sim.UScale, vScale = _sim.VScale, uOff = _sim.UOffset, vOff = _sim.VOffset;
        _sim.Advance(Age, dt);
        if (_sim.UScale != uScale || _sim.VScale != vScale || _sim.UOffset != uOff || _sim.VOffset != vOff)
            UpdateUvs();
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

        uint argb = _sim.Argb;
        _draw.Matrix = Matrix4x4.Translate(_emitter);
        _draw.Color = new Color(
            ((argb >> 16) & 0xff) / 255f,
            ((argb >> 8) & 0xff) / 255f,
            (argb & 0xff) / 255f,
            ((argb >> 24) & 0xff) / 255f * Mathf.Clamp01(_sim.Alpha));
        dest.Add(_draw);
    }
}
