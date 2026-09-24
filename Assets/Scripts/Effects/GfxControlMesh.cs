using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// typeCode 3010 (0xbc2), stock <c>_GfxControlMesh_t</c>. The mechanics are <see cref="MeshSim"/>;
/// this loads the model field 9 names and draws it.
///
/// Stock's visual is a plain <c>VisualMesh_t</c> — a real model, not a GfxVisual — driven by three
/// calls a frame: <c>SetPosition</c>, <c>SetRotation</c> and <c>SetTransparency</c>. All four models
/// are tower wrecks, which is what the Destruction nanos leave behind.
///
/// The whole model is drawn, every part of it, unlike VulcanRocks where stock deliberately takes only
/// the first mesh.
/// </summary>
public sealed class GfxControlMesh : GfxControl
{
    /// <summary>How far above and below to look for ground, as <see cref="GfxControlGroundRing"/>.</summary>
    const float ProbeUp = 200f;
    const float ProbeDown = 400f;

    readonly MeshSim _sim;
    readonly EffectModels _models;
    readonly List<EffectBillboardBatch.MeshDraw> _draws = new List<EffectBillboardBatch.MeshDraw>();

    EffectModels.Model _model;
    bool _looked;

    public MeshSim Sim => _sim;

    public GfxControlMesh(GfxTweakRecord record, EffectLocator locator, EffectModels models)
        : base(record, locator)
    {
        _models = models;
        _sim = new MeshSim(record?.Fields) { GroundHeight = SampleGround };
        SetDurationFromTemplate();
    }

    protected override void OnProcess(float dt)
    {
        Matrix4x4 world = Matrix4x4.identity;
        bool valid = Locator != null && Locator.TryResolve(out world);
        Vector3 origin = valid ? (Vector3)world.GetColumn(3) : new Vector3(_sim.X, _sim.Y, _sim.Z);

        _sim.Step(Age, valid, origin.x, origin.y, origin.z);
        if (_sim.Dead)
            ReadyFlag = true;
    }

    /// <summary>Stock's <c>100d33f3</c>, as a downward ray.</summary>
    float SampleGround(float x, float z)
    {
        return LostEden.Vehicles.WorldCollision.GroundAt(
            new Vector3(x, _sim.Y, z), ProbeUp, ProbeDown, out Vector3 surfaceHit, out _)
            ? surfaceHit.y
            : _sim.Y;
    }

    public override void CollectMeshes(List<EffectBillboardBatch.MeshDraw> dest, Camera camera)
    {
        if (!IsAlive || dest == null || _sim.Dead)
            return;

        if (!_looked)
        {
            _looked = true;
            _model = _sim.ModelName != null ? _models?.Get(_sim.ModelName) : null;
        }
        if (_model == null || _model.Parts.Length == 0)
            return;

        float alpha = Mathf.Clamp01(_sim.Alpha);
        if (alpha <= 0f)
            return;

        Matrix4x4 place = Matrix4x4.TRS(
            new Vector3(_sim.X, _sim.Y, _sim.Z),
            Quaternion.AngleAxis(_sim.Angle * Mathf.Rad2Deg, Vector3.up),
            Vector3.one);

        int used = 0;
        for (int i = 0; i < _model.Parts.Length; i++)
        {
            EffectModels.Part part = _model.Parts[i];
            // As EffectMesh does: the opaque material while the ramp is full, the fading one otherwise.
            Material material = alpha >= 1f ? part.Lit : part.LitFade;
            if (part.Mesh == null || material == null)
                continue;

            if (used == _draws.Count)
                _draws.Add(new EffectBillboardBatch.MeshDraw());
            EffectBillboardBatch.MeshDraw draw = _draws[used++];
            draw.Mesh = part.Mesh;
            draw.Material = material;
            draw.Matrix = place * part.Local;
            Color tint = part.Desc.Diffuse;
            // SetTransparency (10154394): the model's own colour, faded by the ramp.
            tint.a *= alpha;
            draw.Color = tint;
            dest.Add(draw);
        }
    }
}
