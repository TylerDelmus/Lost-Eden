using System.Collections.Generic;
using Stat = AOSharp.Common.GameData.Stat;
using UnityEngine;

/// <summary>
/// typeCode 3025 (0xbd1), stock <c>GfxControlEffectMesh_t</c>. The rules are <see cref="EffectMeshSim"/>;
/// this places the model (<see cref="EffectModels"/>) and draws it.
///
/// Placement (<c>1010d071</c>..<c>1010d44c</c>): the locator's local-mode position (<c>10106306</c>; the
/// ground under it with flag 0x100) plus the offset; the turn is the axis-angle quaternion times the
/// locator's local-mode turn (<c>1007ca22</c>), which the port composes as locator turn, then the spin in
/// the locator's frame. Every record spins about up on a yaw-only host or not at all, so the order never
/// shows. Flag 0x400 turns the model's up axis to the camera instead (shortest arc, <c>100ad4f8</c>).
///
/// Drawing follows <c>VisualMesh_t::SetRenderingEffect</c> (DisplaySystem <c>1006ce7d</c>, per node
/// <c>1006c61d</c>). Effects 4, 5 and 6 (Transparent, Transparent2, Transparent4X delta states) draw the
/// model's texture blended SrcAlpha/One with no depth write, no fog and no culling: 4 lit
/// (D3D lighting on, so the material's emissive plus lit diffuse, clamped; the port takes full light,
/// which for the blast wave's emissive 1 is exact), 5 and 6 unlit white. Any other effect draws the
/// model's own material, which the port does with its HDRP Lit material, faded when below full alpha.
/// Transparency (curve alpha) multiplies the alpha either way.
///
/// On a dynel the breed and the body scale come from its stats (<see cref="EffectMeshSim"/>). The port's body
/// scale is stat 360 / 100, or 1 when the stat is unset, as <c>VisualDynel.ApplyScale</c> draws the body. The
/// dynel ctor's visibility switch (<c>1010c9c9</c>, as Trail2's) isn't ported; the port always draws.
/// </summary>
public sealed class GfxControlEffectMesh : GfxControl
{
    readonly EffectMeshSim _sim;
    readonly EffectModels.Model _model;
    readonly EffectBillboardBatch.MeshDraw[] _draws;
    readonly StatCollection _stats;
    // Per part: the UV track's cached key (FAFAnim_t +0x38) and its tiling / offset this call.
    readonly int[] _uvIndex;
    readonly Vector4[] _uvSt;
    Vector3 _position;
    Quaternion _turn = Quaternion.identity;
    bool _placed;

    public EffectMeshSim Sim => _sim;
    public EffectModels.Model Model => _model;
    public Vector3 Position => _position;

    public GfxControlEffectMesh(GfxTweakRecord record, EffectLocator locator, EffectModels models)
        : base(record, locator)
    {
        // 1010cce0: the breed and body scale of the dynel it was built on; none on a point.
        StatCollection stats = null;
        bool onDynel = locator != null && EffectBodyTable.TryGetHostStats(locator, out stats);
        _stats = onDynel ? stats : null;
        _sim = new EffectMeshSim(record?.Fields, _stats != null ? _stats.Get(Stat.Breed) : 0);
        ReadBodyScale();
        base.SetDuration(_sim.Duration);

        string name = _sim.Model;
        _model = name != null ? models?.Get(name) : null;
        if (_model == null)
        {
            ReadyFlag = true;
            _draws = System.Array.Empty<EffectBillboardBatch.MeshDraw>();
            return;
        }

        _draws = new EffectBillboardBatch.MeshDraw[_model.Parts.Length];
        for (int i = 0; i < _draws.Length; i++)
            _draws[i] = new EffectBillboardBatch.MeshDraw { Mesh = _model.Parts[i].Mesh };
        _uvIndex = new int[_draws.Length];
        _uvSt = new Vector4[_draws.Length];
        for (int i = 0; i < _uvSt.Length; i++)
            _uvSt[i] = new Vector4(1f, 1f, 0f, 0f);
    }

    /// <summary>With 0x200, stock +0xd0 = the dynel's body scale (read at load and every call).</summary>
    void ReadBodyScale()
    {
        if (_stats == null || (_sim.Flags & EffectMeshSim.FlagBodyScale) == 0)
            return;
        int scale = _stats.Get(Stat.Scale);
        _sim.BodyScale = scale > 0 ? (float)(scale / 100.0) : 1f;
    }

    protected override void OnArmed() => Body(0f);

    protected override void OnProcess(float dt) => Body(dt);

    void Body(float dt)
    {
        if (Locator == null || !Locator.TryResolve(out Matrix4x4 world))
        {
            ReadyFlag = true;
            return;
        }

        ReadBodyScale();
        _sim.Step(dt, Age);
        if (_model != null && _sim.StepAnimation(dt, _model.AnimationTotalTime))
            Animate();

        bool local = (_sim.Flags & EffectMeshSim.FlagLocalMode) != 0;
        Vector3 p = local ? (Vector3)world.GetColumn(3) : Vector3.zero;
        if ((_sim.Flags & EffectMeshSim.FlagGround) != 0)
        {
            float g = EffectGround.HeightAt(p.x, p.y, p.z);
            if (!float.IsNaN(g))
                p.y = g;
        }
        _position = p + new Vector3(_sim.OffsetX, _sim.OffsetY, _sim.OffsetZ);

        Quaternion host = local ? Turn(world) : Quaternion.identity;
        var axis = new Vector3(_sim.AxisX, _sim.AxisY, _sim.AxisZ);
        _turn = host * Quaternion.AngleAxis(_sim.Angle * Mathf.Rad2Deg, axis);
        _placed = true;
    }

    /// <summary>
    /// SetAnimationTime on every part's node: its time (<see cref="StockUvTrack.NodeTime"/>), then its UV
    /// track. The bake stores v as -v, so a D3D (tu, tv, ou, ov) is Unity ST (tu, tv, ou, -ov).
    /// </summary>
    void Animate()
    {
        for (int i = 0; i < _uvSt.Length; i++)
        {
            StockUvTrack uv = _model.Parts[i].Uv;
            if (uv == null || !(0f < uv.TotalTime))
                continue;
            float t = StockUvTrack.NodeTime(_sim.AnimTime, uv.TotalTime);
            uv.Evaluate(t, ref _uvIndex[i], out float tu, out float tv, out float ou, out float ov);
            _uvSt[i] = new Vector4(tu, tv, ou, -ov);
        }
    }

    static Quaternion Turn(Matrix4x4 m)
    {
        Vector3 forward = m.GetColumn(2);
        Vector3 up = m.GetColumn(1);
        if (forward.sqrMagnitude < 1e-12f || up.sqrMagnitude < 1e-12f)
            return Quaternion.identity;
        return Quaternion.LookRotation(forward, up);
    }

    public override void CollectMeshes(List<EffectBillboardBatch.MeshDraw> dest, Camera camera)
    {
        if (!IsAlive || dest == null || !_placed || _model == null)
            return;

        Quaternion turn = _turn;
        float alpha = _sim.Alpha;
        if (camera != null && (_sim.Flags & (EffectMeshSim.FlagFaceCamera | EffectMeshSim.FlagDistanceFade)) != 0)
        {
            Vector3 d = camera.transform.position - _position;
            float len = d.magnitude;
            if ((_sim.Flags & EffectMeshSim.FlagDistanceFade) != 0)
                alpha = EffectMeshSim.DistanceAlpha(len, alpha);
            if ((_sim.Flags & EffectMeshSim.FlagFaceCamera) != 0 && len > 0f)
                turn = Quaternion.FromToRotation(Vector3.up, d / len);
        }
        alpha *= _sim.Fade;
        if (alpha <= 0f)
            return;

        Matrix4x4 root = Matrix4x4.TRS(_position, turn, Vector3.one * _sim.DrawScale);
        int effect = _sim.RenderingEffect;
        // 5 and 6 are the unlit additive pair. 4 is additive too, but with D3DRS_LIGHTING on
        // (DisplaySystem 1006c912), so it goes through a lit material of its own.
        bool transparent = effect == 5 || effect == 6;
        for (int i = 0; i < _draws.Length; i++)
        {
            EffectModels.Part part = _model.Parts[i];
            EffectBillboardBatch.MeshDraw draw = _draws[i];
            draw.Matrix = root * part.Local;
            draw.TextureST = _uvSt[i];
            if (effect == 4)
            {
                // Lit and additive: the model's own diffuse, weighted by the fade, through a material
                // the engine actually lights — the same standing the effect-0 path has.
                Material material = part.LitAdditive;
                if (material == null)
                    continue;
                Color c = part.Desc.Diffuse;
                draw.Material = material;
                draw.Color = new Color(c.r, c.g, c.b, c.a * Mathf.Min(alpha, 1f));
            }
            else if (transparent)
            {
                if (part.Texture == null)
                    continue;
                draw.Material = null;
                draw.Texture = part.Texture;
                draw.Additive = true;
                draw.Color = new Color(1f, 1f, 1f, alpha);
            }
            else
            {
                Material material = alpha >= 1f ? part.Lit : part.LitFade;
                if (material == null)
                    continue;
                Color c = part.Desc.Diffuse;
                draw.Material = material;
                draw.Color = new Color(c.r, c.g, c.b, c.a * Mathf.Min(alpha, 1f));
            }
            dest.Add(draw);
        }
    }
}
