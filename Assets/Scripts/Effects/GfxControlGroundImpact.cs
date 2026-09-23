using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// typeCode 5000 (0x1388), stock <c>_GfxControlGroundImpact_c</c>. The rules are
/// <see cref="GroundImpactSim"/>.
///
/// The control itself is a shell: its loader discards all four record fields, it takes -1 as its
/// duration, and all it does per call is keep the locator and let the visual rebuild itself. Stock
/// hands the locator over as the visual's *connector* (<c>100e1583</c> calls SetConnector), so the
/// blade is built in the locator's own frame and drawn through its matrix.
///
/// Material index 1, drawn additively: InitMesh's state block sets DESTBLEND to ONE with no Z-write,
/// no fog and no culling (<c>10015749</c>).
/// </summary>
public sealed class GfxControlGroundImpact : GfxControl
{
    /// <summary>100e1570: the build asks for material 1, not a record field.</summary>
    public const int MaterialIndex = 1;

    readonly GroundImpactSim _sim;
    readonly Texture2D _texture;
    readonly Mesh _mesh;
    readonly Vector3[] _vertices;
    readonly Vector2[] _uvs;
    readonly Color32[] _colours;
    readonly EffectBillboardBatch.MeshDraw _draw;
    Matrix4x4 _frame = Matrix4x4.identity;
    bool _built;

    public GroundImpactSim Sim => _sim;

    public GfxControlGroundImpact(GfxTweakRecord record, EffectLocator locator, Texture2D texture)
        : base(record, locator)
    {
        _texture = texture;
        _sim = new GroundImpactSim(() => Random.value);
        // 100e14d8: the loader ends by taking -1.0 as the duration whatever the record holds.
        base.SetDuration(InfiniteDuration);

        _vertices = new Vector3[_sim.VertexCount];
        _uvs = new Vector2[_sim.VertexCount];
        _colours = new Color32[_sim.VertexCount];
        _mesh = new Mesh
        {
            name = "GroundImpact",
            indexFormat = _sim.VertexCount > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16,
        };
        _mesh.MarkDynamic();
        _draw = new EffectBillboardBatch.MeshDraw
        {
            Mesh = _mesh,
            Texture = texture,
            Additive = true,
            VertexColors = true,
            Color = Color.white,
        };

        for (int v = 0; v < _sim.VertexCount; v++)
        {
            // D3D v runs down the image, Unity's up.
            _uvs[v] = new Vector2(_sim.Uvs[v * 2], 1f - _sim.Uvs[v * 2 + 1]);
        }
    }

    protected override void OnArmed() => Body();

    protected override void OnProcess(float dt) => Body();

    void Body()
    {
        // 100e1423: the control readies itself the moment its locator is gone.
        if (Locator == null || !Locator.TryResolve(out Matrix4x4 m))
        {
            ReadyFlag = true;
            return;
        }
        _frame = m;

        // 10015f21: the whole lattice is rebuilt every call, so the jitter and flicker are per frame.
        _sim.Step();

        for (int v = 0; v < _sim.VertexCount; v++)
        {
            _vertices[v] = new Vector3(_sim.Positions[v * 3], _sim.Positions[v * 3 + 1], _sim.Positions[v * 3 + 2]);
            _colours[v] = EffectBillboardBatch.ToColor32(_sim.Colours[v]);
        }

        _mesh.vertices = _vertices;
        _mesh.colors32 = _colours;
        if (!_built)
        {
            _mesh.uv = _uvs;
            _mesh.SetTriangles(_sim.Indices, 0, calculateBounds: false);
            _built = true;
        }
        _mesh.RecalculateBounds();
    }

    /// <summary>100e147a forwards to the visual's SetSize (<c>100156c9</c>).</summary>
    public override void SetDuration(float seconds)
    {
    }

    /// <summary>100e144d forwards to the visual's SetColor (<c>1001564b</c>), rgb only.</summary>
    public override void SetColor(uint argb)
    {
        _sim.SetColour(
            ((argb >> 16) & 0xff) / 255f,
            ((argb >> 8) & 0xff) / 255f,
            (argb & 0xff) / 255f);
    }

    protected override void OnReleased(bool immediate)
    {
        if (_mesh != null)
            Object.Destroy(_mesh);
    }

    public override void CollectMeshes(List<EffectBillboardBatch.MeshDraw> dest, Camera camera)
    {
        if (!IsAlive || dest == null || !_built || _texture == null)
            return;

        _draw.Matrix = _frame;
        dest.Add(_draw);
    }
}
