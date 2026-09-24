using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// typeCode 1029 (0x405), stock <c>_GfxControlVulcanRocks_t</c>: rocks thrown up from the locator that
/// bounce on the ground and settle. The rules are <see cref="VulcanRocksSim"/>; this owns the model table,
/// the rock budget, the clock and the drawing.
///
/// Each rock is a DisplaySystem <c>VisualTinyRock_t</c>: the first mesh of its slot in
/// <c>VisualEnvFX_t</c>'s model table, placed at the rock and turned by (axis, angle) in
/// <c>GfxVisualRockList::ProcessRocks</c> (<c>1001c435</c>), drawn with the model's own material. That table
/// is filled only when the environment effects start (<c>VisualEnvFX_t::ActivateFX</c> through
/// <c>100616e4</c>), which the port doesn't have. By decision (user, 2026-09-22) the port takes slot 4,
/// rock01.abiff, as loaded, as it is once any GenericMeshObject environment effect (FXID 4) has run. Every
/// other slot the records pick either holds a texture (5-10, 42, 43), nothing (44, 45) or a model no
/// effect loads (40, 41, 46), and stock gets no rock from those.
///
/// Stock runs the body once per frame and counts a resting rock once per call, so its end and bounce
/// follow the frame rate. The port replays it at the fixed stock rate (<see cref="EffectFrameRate"/>) and
/// draws between the last two steps.
/// </summary>
public sealed class GfxControlVulcanRocks : GfxControl
{
    /// <summary>The slots of the model table the port treats as loaded, with their models.</summary>
    public static readonly Dictionary<int, string> LoadedModels = new Dictionary<int, string>
    {
        { 4, "rock01.abiff" },
    };

    /// <summary><c>GfxVisualRockHandlerInterface</c> is built with 256 (<c>1001c368</c>): rocks in all.</summary>
    public const int HandlerLimit = 256;

    /// <summary>10103bfb: a control goes once 50 newer ones exist.</summary>
    public const int NewerLimit = 50;

    const int MaxStepsPerFrame = 8;

    static int s_liveRocks;
    static int s_serial;

    readonly VulcanRocksSim _sim;
    readonly EffectModels _models;
    readonly int _serial;
    readonly List<EffectBillboardBatch.MeshDraw> _draws = new List<EffectBillboardBatch.MeshDraw>();
    readonly List<VulcanRocksSim.Rock> _previous = new List<VulcanRocksSim.Rock>();
    int _ownRocks;
    float _carry;
    float _stepAge;

    public VulcanRocksSim Sim => _sim;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        s_liveRocks = 0;
        s_serial = 0;
    }

    public GfxControlVulcanRocks(GfxTweakRecord record, EffectLocator locator, EffectModels models)
        : base(record, locator)
    {
        _models = models;
        _sim = new VulcanRocksSim(record?.Fields, () => Random.value, Claim, Ground);
        base.SetDuration(_sim.Duration < 0f ? InfiniteDuration : _sim.Duration);
        // 10103595: +0x94, the instance counter.
        _serial = s_serial++;
    }

    /// <summary>Stock slot 8 is the empty <c>10079931</c>.</summary>
    public override void SetDuration(float seconds) { }

    /// <summary>Stock slot 13 (<c>1010367d</c>): kept at +0x74 and never drawn.</summary>
    public override void SetColor(uint argb) => _sim.Argb = argb;

    bool Claim(int slot)
    {
        if (s_liveRocks >= HandlerLimit || Model(slot) == null)
            return false;
        s_liveRocks++;
        _ownRocks++;
        return true;
    }

    EffectModels.Model Model(int slot)
        => LoadedModels.TryGetValue(slot, out string name) ? _models?.Get(name) : null;

    static bool Ground(float x, float y, float z, out float height, out float nx, out float ny, out float nz)
    {
        bool found = EffectGround.TryGround(x, y, z, out height, out Vector3 n);
        nx = n.x;
        ny = n.y;
        nz = n.z;
        return found;
    }

    // Stock's first Process arms the control and still runs the body at age 0.
    protected override void OnArmed() => Body(0f, 0f);

    protected override void OnProcess(float dt)
    {
        float step = EffectFrameRate.StockProcessSeconds;
        int steps = EffectFrameRate.TakeFixedSteps(ref _carry, dt, step, MaxStepsPerFrame);
        for (int s = 0; s < steps && !ReadyFlag; s++)
        {
            Snapshot();
            _stepAge += step;
            Body(_stepAge, step);
        }
    }

    void Body(float age, float dt)
    {
        if (!Frame(out VulcanRocksSim.Frame frame))
        {
            ReadyFlag = true;
            return;
        }

        if (_serial + NewerLimit < s_serial)
            ReadyFlag = true;
        if (!_sim.Step(age, dt, frame))
            ReadyFlag = true;
    }

    bool Frame(out VulcanRocksSim.Frame frame)
    {
        frame = default;
        if (Locator == null || !Locator.TryResolve(out Matrix4x4 world))
            return false;

        Vector3 p = world.GetColumn(3);
        Vector3 x = ((Vector3)world.GetColumn(0)).normalized;
        Vector3 y = ((Vector3)world.GetColumn(1)).normalized;
        Vector3 z = ((Vector3)world.GetColumn(2)).normalized;
        frame = new VulcanRocksSim.Frame
        {
            X = p.x, Y = p.y, Z = p.z,
            XX = x.x, XY = x.y, XZ = x.z,
            YX = y.x, YY = y.y, YZ = y.z,
            ZX = z.x, ZY = z.y, ZZ = z.z,
        };
        return true;
    }

    void Snapshot()
    {
        IReadOnlyList<VulcanRocksSim.Rock> rocks = _sim.Rocks;
        for (int i = 0; i < rocks.Count; i++)
        {
            if (i == _previous.Count)
                _previous.Add(new VulcanRocksSim.Rock());
            VulcanRocksSim.Rock r = rocks[i], q = _previous[i];
            q.X = r.X; q.Y = r.Y; q.Z = r.Z;
            q.AxisX = r.AxisX; q.AxisY = r.AxisY; q.AxisZ = r.AxisZ;
            q.Angle = r.Angle;
        }
    }

    protected override void OnReleased(bool immediate)
    {
        // 1001c602 / DeleteRocks: the rocks go with the list.
        s_liveRocks = Mathf.Max(0, s_liveRocks - _ownRocks);
        _ownRocks = 0;
    }

    public override void CollectMeshes(List<EffectBillboardBatch.MeshDraw> dest, Camera camera)
    {
        if (!IsAlive || dest == null)
            return;

        // Between the last two steps: the time carried towards the next one says how far.
        float blend = Mathf.Clamp01(_carry / EffectFrameRate.StockProcessSeconds);
        int used = 0;
        IReadOnlyList<VulcanRocksSim.Rock> rocks = _sim.Rocks;
        for (int i = 0; i < rocks.Count; i++)
        {
            VulcanRocksSim.Rock r = rocks[i];
            EffectModels.Model model = Model(r.Model);
            if (model == null || model.Parts.Length == 0)
                continue;

            var position = new Vector3(r.X, r.Y, r.Z);
            var axis = new Vector3(r.AxisX, r.AxisY, r.AxisZ);
            float angle = r.Angle;
            // A rock thrown this step has no earlier place; a bounce that turned its axis is drawn as it is.
            if (i < _previous.Count)
            {
                VulcanRocksSim.Rock q = _previous[i];
                position = Vector3.LerpUnclamped(new Vector3(q.X, q.Y, q.Z), position, blend);
                if (q.AxisX == r.AxisX && q.AxisY == r.AxisY && q.AxisZ == r.AxisZ)
                    angle = q.Angle + (r.Angle - q.Angle) * blend;
            }

            // 100612c3(0): the first mesh of the model only.
            EffectModels.Part part = model.Parts[0];
            if (part.Lit == null)
                continue;

            if (used == _draws.Count)
                _draws.Add(new EffectBillboardBatch.MeshDraw());
            EffectBillboardBatch.MeshDraw draw = _draws[used++];
            draw.Mesh = part.Mesh;
            draw.Material = part.Lit;
            draw.Matrix = Matrix4x4.TRS(position, Quaternion.AngleAxis(angle * Mathf.Rad2Deg, axis), Vector3.one)
                * part.Local;
            draw.Color = part.Desc.Diffuse;
            dest.Add(draw);
        }
    }
}
