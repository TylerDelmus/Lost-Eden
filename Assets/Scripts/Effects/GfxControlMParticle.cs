using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// typeCode 3027 (0xbd3), stock <c>GfxControlMParticle_t</c>. The particle maths is
/// <see cref="MParticleSim"/>; this owns the locator, the clock and the drawing.
///
/// The emitter and the turn come from the locator's local-mode accessors (<c>10106306</c> /
/// <c>101062d5</c>): with field 0 bit 1 the locator's position and turn, without it the world origin and
/// no turn; flag 0x100 drops the emitter to the ground (<c>1010f99f</c>). Each particle is a
/// <c>VisualMesh_t</c> of its model at its position, turned by (axis, angle) and scaled; stock sets no
/// rendering effect, so the model draws with its own material, which the port does with its HDRP Lit
/// material, faded below full alpha.
/// </summary>
public sealed class GfxControlMParticle : GfxControl
{
    readonly MParticleSim _sim;
    readonly EffectModels.Model[] _models;
    readonly float[] _turn = new float[9];
    readonly System.Func<float, float, float, float> _ground = EffectGround.HeightAt;
    readonly List<EffectBillboardBatch.MeshDraw> _draws = new List<EffectBillboardBatch.MeshDraw>();

    public MParticleSim Sim => _sim;

    public GfxControlMParticle(GfxTweakRecord record, EffectLocator locator, EffectModels models)
        : base(record, locator)
    {
        _sim = new MParticleSim(record?.Fields, () => Random.value);
        base.SetDuration(_sim.Duration);

        int[] indices = _sim.Models;
        _models = new EffectModels.Model[indices.Length];
        for (int i = 0; i < indices.Length; i++)
        {
            // 1010f4c0: only the first ten names; past them the identity is 0 and nothing loads.
            string name = indices[i] >= 0 && indices[i] <= 9 ? EffectMeshSim.ModelName(indices[i]) : null;
            _models[i] = name != null ? models?.Get(name) : null;
        }

        if (!_sim.Init())
            ReadyFlag = true;
    }

    /// <summary>The emitter and turn this frame; fills <see cref="_turn"/>.</summary>
    Vector3 Emitter(out bool resolved)
    {
        Matrix4x4 world = Matrix4x4.identity;
        resolved = Locator != null && Locator.TryResolve(out world);
        Vector3 p = Vector3.zero;
        if ((_sim.Flags & MParticleSim.FlagLocalMode) == 0 || !resolved)
        {
            for (int i = 0; i < 9; i++)
                _turn[i] = i % 4 == 0 ? 1f : 0f;
        }
        else
        {
            Vector3 x = ((Vector3)world.GetColumn(0)).normalized;
            Vector3 y = ((Vector3)world.GetColumn(1)).normalized;
            Vector3 z = ((Vector3)world.GetColumn(2)).normalized;
            _turn[0] = x.x; _turn[1] = y.x; _turn[2] = z.x;
            _turn[3] = x.y; _turn[4] = y.y; _turn[5] = z.y;
            _turn[6] = x.z; _turn[7] = y.z; _turn[8] = z.z;
            p = world.GetColumn(3);
        }

        if ((_sim.Flags & MParticleSim.FlagGround) != 0)
        {
            float g = EffectGround.HeightAt(p.x, p.y, p.z);
            if (!float.IsNaN(g))
                p.y = g;
        }
        return p;
    }

    protected override void OnArmed() => Body(0f);

    protected override void OnProcess(float dt) => Body(dt);

    /// <summary>
    /// Port decision. Stock's bounce keeps 1 - 100 b dt of the horizontal speed and gives back 100 b dt
    /// of the vertical, so its restitution follows the frame rate: at 30 fps the 0.5 of 71045 gives back
    /// 165% and a chunk under the ground flips and grows every frame until it is hundreds of metres off.
    /// Each call is cut into pieces of at most 1/60 s, which is stock as it runs at 60 fps or more.
    /// </summary>
    const float MaxStep = 1f / 60f;

    void Body(float dt)
    {
        Vector3 emitter = Emitter(out bool resolved);
        if (!resolved)
        {
            ReadyFlag = true;
            return;
        }

        int pieces = Mathf.Max(1, Mathf.CeilToInt(dt / MaxStep));
        float step = dt / pieces;
        for (int i = 1; i <= pieces; i++)
        {
            float age = Age - dt + step * i;
            if (!_sim.Step(step, age, emitter.x, emitter.y, emitter.z, _turn, _ground))
            {
                ReadyFlag = true;
                return;
            }
        }
    }

    public override void CollectMeshes(List<EffectBillboardBatch.MeshDraw> dest, Camera camera)
    {
        if (!IsAlive || dest == null)
            return;

        int used = 0;
        MParticleSim.Particle[] particles = _sim.Particles;
        for (int i = 0; i < particles.Length; i++)
        {
            MParticleSim.Particle p = particles[i];
            if (!p.Visible || p.Alpha <= 0f)
                continue;
            EffectModels.Model model = p.Model >= 0 && p.Model < _models.Length ? _models[p.Model] : null;
            if (model == null)
                continue;

            var axis = new Vector3(p.AxisX, p.AxisY, p.AxisZ);
            Matrix4x4 root = Matrix4x4.TRS(
                new Vector3(p.X, p.Y, p.Z),
                Quaternion.AngleAxis(p.Angle * Mathf.Rad2Deg, axis),
                Vector3.one * p.Scale);
            float alpha = Mathf.Min(p.Alpha, 1f);
            for (int k = 0; k < model.Parts.Length; k++)
            {
                EffectModels.Part part = model.Parts[k];
                Material material = alpha >= 1f ? part.Lit : part.LitFade;
                if (material == null)
                    continue;

                if (used == _draws.Count)
                    _draws.Add(new EffectBillboardBatch.MeshDraw());
                EffectBillboardBatch.MeshDraw draw = _draws[used++];
                Color c = part.Desc.Diffuse;
                draw.Mesh = part.Mesh;
                draw.Material = material;
                draw.Matrix = root * part.Local;
                draw.Color = new Color(c.r, c.g, c.b, c.a * alpha);
                dest.Add(draw);
            }
        }
    }
}
