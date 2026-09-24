using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// typeCode 3038 (0xbde), stock <c>GfxControlVolGrid_t</c>: a box of crossed textured slices that grows and
/// fades over its life. The curves and the geometry are <see cref="VolGridSim"/>; this owns the frame and the
/// drawing.
///
/// The visual (<c>GfxVisualVolGrid</c>, <c>1002f410</c>) draws unlit, texture × vertex colour, SrcAlpha/One,
/// no Z write, no culling, no fog. Its place is the locator's local-mode position at creation (read again
/// every call with 0x800; on the ground with 0x2000), its turn the locator's local-mode turn every call,
/// orthonormalised (<c>10116182</c>), after a spin about y by an angle that starts at 0 (r · 360 with 0x1000,
/// taken as radians) and grows by field 13 · dt.
/// </summary>
public sealed class GfxControlVolGrid : GfxControl
{
    readonly VolGridSim _sim;
    readonly Texture2D _texture;
    readonly bool _localMode;
    readonly float[] _positions;
    readonly uint[] _colours;
    readonly float[] _uvs;
    readonly Vector3[] _vertices;
    readonly Color32[] _vertexColours;
    readonly Vector2[] _vertexUvs;
    readonly EffectBillboardBatch.MeshDraw _draw = new EffectBillboardBatch.MeshDraw();
    Mesh _mesh;
    Vector3 _position;
    Quaternion _turn = Quaternion.identity;
    float _angle;

    public VolGridSim Sim => _sim;
    public Vector3 Position => _position;

    public GfxControlVolGrid(GfxTweakRecord record, EffectLocator locator, Texture2D texture)
        : base(record, locator)
    {
        _texture = texture;
        _sim = new VolGridSim(record?.Fields);
        _localMode = (_sim.Flags & VolGridSim.FlagLocalFrame) != 0;
        // Expiry is the sim's: age against field 8, or what SetDuration puts there.
        base.SetDuration(InfiniteDuration);

        int count = _sim.SliceCount * VolGridSim.VerticesPerSlice;
        _positions = new float[count * 3];
        _colours = new uint[count];
        _uvs = new float[count * 2];
        _vertices = new Vector3[count];
        _vertexColours = new Color32[count];
        _vertexUvs = new Vector2[count];

        // 10115eb5: the place once, and the start angle.
        Frame(out _position, out _turn);
        _angle = (_sim.Flags & VolGridSim.FlagRandomAngle) != 0 ? (float)(Random.value * 360.0 + 0.0) : 0f;
    }

    /// <summary>Stock slot 8 (<c>1010340b</c>): +0x10.</summary>
    public override void SetDuration(float seconds)
    {
        if (_sim != null)
            _sim.Duration = seconds;
    }

    /// <summary>Stock slot 6 is an empty <c>ret</c> (<c>10115eb4</c>): it runs to its end.</summary>
    protected override void OnTerminateGracefully() { }

    // Stock's first Process arms the control and still runs the body at age 0.
    protected override void OnArmed() => Body(0f);

    protected override void OnProcess(float dt) => Body(dt);

    void Body(float dt)
    {
        if (!_sim.Step(Age))
        {
            ReadyFlag = true;
            return;
        }

        bool resolved = Frame(out Vector3 position, out _turn);
        if ((_sim.Flags & VolGridSim.FlagFollow) != 0)
        {
            if (!resolved)
            {
                ReadyFlag = true;
                return;
            }
            _position = position;
        }

        _angle = (float)(_sim.Spin * (double)dt + _angle);
    }

    /// <summary>The locator's local-mode position (on the ground with 0x2000) and turn; false when it is lost.</summary>
    bool Frame(out Vector3 position, out Quaternion turn)
    {
        Matrix4x4 world = Matrix4x4.identity;
        bool resolved = Locator != null && Locator.TryResolve(out world);
        position = Vector3.zero;
        turn = Quaternion.identity;
        if (_localMode && resolved)
        {
            Vector3 x = ((Vector3)world.GetColumn(0)).normalized;
            Vector3 y = ((Vector3)world.GetColumn(1)).normalized;
            Vector3 z = ((Vector3)world.GetColumn(2)).normalized;
            if (x != Vector3.zero && y != Vector3.zero && z != Vector3.zero)
            {
                var m = new Matrix4x4(x, y, z, new Vector4(0f, 0f, 0f, 1f));
                turn = m.rotation;
            }
            position = world.GetColumn(3);
        }

        if ((_sim.Flags & VolGridSim.FlagGround) != 0)
        {
            float g = EffectGround.HeightAt(position.x, position.y, position.z);
            if (!float.IsNaN(g))
                position.y = g;
        }
        return resolved;
    }

    Quaternion Rotation => Quaternion.AngleAxis(_angle * Mathf.Rad2Deg, Vector3.up) * _turn;

    public override void CollectMeshes(List<EffectBillboardBatch.MeshDraw> dest, Camera camera)
    {
        if (!IsAlive || dest == null || _texture == null || _vertices.Length == 0)
            return;

        _sim.Build(_positions, _colours, _uvs);
        Quaternion rotation = Rotation;
        if ((_sim.Flags & VolGridSim.FlagEdgeFade) != 0 && camera != null)
        {
            // 1002f69e: the camera-to-grid direction in the grid's own frame.
            Vector3 d = _position - camera.transform.position;
            if (d != Vector3.zero)
            {
                d = Quaternion.Inverse(rotation) * d.normalized;
                for (int s = 0; s < _sim.SliceCount; s++)
                    VolGridSim.EdgeFade(_positions, _colours, s, d.x, d.y, d.z);
            }
        }

        for (int v = 0; v < _vertices.Length; v++)
        {
            _vertices[v] = new Vector3(_positions[v * 3], _positions[v * 3 + 1], _positions[v * 3 + 2]);
            _vertexColours[v] = EffectBillboardBatch.ToColor32(_colours[v]);
            // D3D v runs down the image, Unity's up.
            _vertexUvs[v] = new Vector2(_uvs[v * 2], 1f - _uvs[v * 2 + 1]);
        }

        if (_mesh == null)
        {
            _mesh = new Mesh { name = "VolGrid" };
            _mesh.MarkDynamic();
            _mesh.vertices = _vertices;
            _mesh.triangles = Fans(_sim.SliceCount);
        }
        _mesh.vertices = _vertices;
        _mesh.colors32 = _vertexColours;
        _mesh.uv = _vertexUvs;
        _mesh.RecalculateBounds();

        _draw.Mesh = _mesh;
        _draw.Matrix = Matrix4x4.TRS(_position, rotation, Vector3.one);
        _draw.Texture = _texture;
        _draw.Color = Color.white;
        _draw.VertexColors = true;
        _draw.Additive = true;
        dest.Add(_draw);
    }

    /// <summary>Each slice's fan 0 1 2 3 4 1 as four triangles.</summary>
    static int[] Fans(int slices)
    {
        int[] fan = VolGridSim.FanIndices;
        var triangles = new int[slices * (fan.Length - 2) * 3];
        int t = 0;
        for (int s = 0; s < slices; s++)
        {
            int b = s * VolGridSim.VerticesPerSlice;
            for (int k = 1; k < fan.Length - 1; k++)
            {
                triangles[t++] = b + fan[0];
                triangles[t++] = b + fan[k];
                triangles[t++] = b + fan[k + 1];
            }
        }
        return triangles;
    }

    protected override void OnReleased(bool immediate)
    {
        if (_mesh != null)
            Object.Destroy(_mesh);
        _mesh = null;
    }
}
