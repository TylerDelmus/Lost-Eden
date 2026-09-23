using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// typeCode 3023 (0xbcf), stock <c>GfxControlEnergyBall_t</c>. The rules are <see cref="EnergyBallSim"/>;
/// this places the ball and emits its blades.
///
/// Stock keeps the blades in the visual's own ref frame and moves that frame
/// (<c>RRefFrame_t::SetRelativePosition</c> / <c>SetRelativeRotation</c> with a null parent, so both
/// are world), building every quad about the frame's origin. The port does the same: the sim gives the
/// blade in ball space and this multiplies it through the frame.
///
/// The spawn point is the locator's local-mode position once (<c>1010ddcc</c>); with field 26 it gets a
/// random offset of that length, and flag 0x2000 then drops it to the ground. After that the ball only
/// follows its own wander, not the locator.
/// </summary>
public sealed class GfxControlEnergyBall : GfxControl
{
    readonly EnergyBallSim _sim;
    readonly Texture2D _texture;
    readonly EffectBillboardBatch.Strip _strip;
    Vector3 _emitter;
    Quaternion _locatorTurn = Quaternion.identity;
    bool _placed;

    public EnergyBallSim Sim => _sim;

    public GfxControlEnergyBall(GfxTweakRecord record, EffectLocator locator, Texture2D texture)
        : base(record, locator)
    {
        _texture = texture;
        _sim = new EnergyBallSim(record?.Fields);
        // 1010e05e: the life is fields 10 + 11; field 8 is -1 on every record.
        base.SetDuration(_sim.Life);

        int vertices = _sim.QuadCount * 4;
        _strip = new EffectBillboardBatch.Strip
        {
            Positions = new Vector3[vertices],
            Uvs = new Vector2[vertices],
            Colors = new Color32[vertices],
            Color = Color.white,
            Texture = texture,
            Additive = _sim.Additive,
            Quads = true,
            Count = 0,
        };

        Place();
    }

    /// <summary>1010ddc1..1010de74: the spawn point, its jitter and the ground snap, all done once.</summary>
    void Place()
    {
        if (Locator == null || !Locator.TryResolve(out Matrix4x4 m))
            return;

        _emitter = m.GetColumn(3);
        _locatorTurn = m.rotation;

        // 1010dddd: a non-zero field 26 scatters the spawn inside a sphere of that radius.
        if (_sim.SpawnJitter != 0f)
        {
            var jitter = new Vector3(
                Random.value * 2f - 1f,
                Random.value * 2f - 1f,
                Random.value * 2f - 1f);
            if (jitter.sqrMagnitude > 0f)
                _emitter += jitter.normalized * _sim.SpawnJitter;
        }

        // 1010de68: flag 0x2000 puts the spawn on the ground under itself.
        if (_sim.GroundSnap)
        {
            float g = EffectGround.HeightAt(_emitter.x, _emitter.y, _emitter.z);
            if (!float.IsNaN(g))
                _emitter.y = g;
        }

        _placed = true;
    }

    protected override void OnArmed() => Step();

    protected override void OnProcess(float dt) => Step();

    void Step()
    {
        if (!_placed)
        {
            Place();
            if (!_placed)
                return;
        }

        _sim.Advance(Age);
        if (_sim.Finished)
            ReadyFlag = true;
    }

    public override void CollectStrips(List<EffectBillboardBatch.Strip> dest, Camera camera)
    {
        if (!IsAlive || dest == null || !_placed || _texture == null || _sim.QuadCount <= 0)
            return;

        // 1010e3d7 / 1010e4d4: the frame sits at the emitter plus the climb and the wander, turned by
        // the ball's own spin on top of the locator's.
        Vector3 centre = _emitter
            + new Vector3(0f, _sim.Climbed, 0f)
            + new Vector3(_sim.WanderX, _sim.WanderY, _sim.WanderZ);
        Quaternion turn =
            Quaternion.AngleAxis(_sim.SpinRadians * Mathf.Rad2Deg, new Vector3(_sim.AxisX, _sim.AxisY, _sim.AxisZ))
            * _locatorTurn;

        var top = EffectBillboardBatch.ToColor32(_sim.TopColour);
        var bottom = EffectBillboardBatch.ToColor32(_sim.BottomColour);
        float radius = _sim.Radius;

        int n = 0;
        for (int q = 0; q < _sim.QuadCount; q++)
        {
            _sim.Blade(
                q,
                out _, out _, out _, out _,
                out float ax, out float ay, out float az,
                out float bx, out float by, out float bz);

            for (int c = 0; c < 4; c++)
            {
                EnergyBallSim.Corner(c, radius, ax, ay, az, bx, by, bz, out float x, out float y, out float z);
                EnergyBallSim.Uv(c, _sim.SwapUv, out float u, out float v);
                _strip.Positions[n] = centre + turn * new Vector3(x, y, z);
                // D3D v runs down the image, Unity's up.
                _strip.Uvs[n] = new Vector2(u, 1f - v);
                // 10011f13: the first two corners take the top colour, the last two the bottom.
                _strip.Colors[n] = c < 2 ? top : bottom;
                n++;
            }
        }

        _strip.Count = n;
        _strip.Texture = _texture;
        _strip.Additive = _sim.Additive;
        dest.Add(_strip);
    }
}
