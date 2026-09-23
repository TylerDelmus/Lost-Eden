using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// typeCode 3006 (0xbbe), stock <c>_GfxControlSkyFlash_t</c>: a column of nested cones of light standing on
/// the locator, fading through two phases per cycle. The rules are <see cref="SkyFlashSim"/>; this feeds it the
/// place and draws the cones.
///
/// The init (<c>100ef58b</c>) builds field 13 <c>GfxVisualCone</c>s (segments field 14, uv scales 15 / 16, the
/// uv swap 0x100, material field 9, additive 0x200), turns cone i about y by i · 6.28 / count with 0x2000
/// (<c>SetRelativeRotation</c>, once), takes the place from the locator (on the ground with 0x4000) and runs
/// Process once. Process re-reads the place only in a phase that follows (0x800 / 0x1000), and a lost
/// locator then ends it. Each cone draws a triangle strip of 2N + 2 FVF 0x142 vertices, texture × vertex
/// colour (the bottom colour on the bottom ring, the top colour on the top), SrcAlpha/One, no Z write, no
/// culling, the texture wrapping (<c>1000bf1e</c>).
///
/// The turn's sign is unchecked: the cones are round, so it only moves where each one's texture starts.
/// The torso radius for a negative field 18 is 1 here: the port doesn't read the CAT mesh's torso sphere
/// (Docs §9).
/// </summary>
public sealed class GfxControlSkyFlash : GfxControl
{
    /// <summary>Port-only: <c>GetTorsoSphereRadi</c>'s value for a mesh without a torso sphere.</summary>
    public const float TorsoRadius = 1f;

    readonly SkyFlashSim _sim;
    readonly Texture2D _texture;
    readonly Quaternion[] _turns;
    readonly EffectBillboardBatch.Strip[] _strips;
    Vector3 _position;

    public SkyFlashSim Sim => _sim;
    public Vector3 Position => _position;

    public GfxControlSkyFlash(GfxTweakRecord record, EffectLocator locator, Texture2D texture)
        : base(record, locator)
    {
        _texture = texture;
        _sim = new SkyFlashSim(record?.Fields);
        // Process clears the ready flag every call: only its own expiry or a lost locator ends it.
        base.SetDuration(InfiniteDuration);

        _turns = new Quaternion[_sim.ConeCount];
        _strips = new EffectBillboardBatch.Strip[_sim.ConeCount];
        bool turn = (_sim.Flags & SkyFlashSim.FlagTurnCones) != 0;
        for (int i = 0; i < _strips.Length; i++)
        {
            _turns[i] = turn ? Quaternion.AngleAxis(_sim.ConeTurn(i) * Mathf.Rad2Deg, Vector3.up) : Quaternion.identity;
            int vertices = 2 * _sim.Segments + 2;
            _strips[i] = new EffectBillboardBatch.Strip
            {
                Positions = new Vector3[vertices],
                Uvs = new Vector2[vertices],
                Colors = new Color32[vertices],
                Color = Color.white,
                Texture = _texture,
                Additive = true,
            };
        }

        // 100ef698: the place from the locator, lost or not, then the ground.
        TryPlace(out _position);
        if ((_sim.Flags & SkyFlashSim.FlagGround) != 0)
            _position.y = Ground(_position, _position.y);

        // 100ef894: the dynel ctor's radius flip; a point's ctors (100ef6eb, 100ef77d, 100ef8ef) have none.
        if (locator != null && !locator.IsWorldPoint)
            _sim.ScaleBottomRadius(TorsoRadius);
    }

    /// <summary>Process clears +0x14 at its end, so the duration stock's slot 8 sets never ends it.</summary>
    public override void SetDuration(float seconds) { }

    /// <summary>A terminate's ready flag is cleared by the next Process (<c>100ef4ce</c>).</summary>
    protected override void OnTerminateGracefully() { }

    // Stock's first Process arms the control and still runs the body at age 0.
    protected override void OnArmed() => Body();

    protected override void OnProcess(float dt) => Body();

    void Body()
    {
        if (!_sim.Step(Age))
        {
            ReadyFlag = true;
            return;
        }

        if (_sim.Follow)
        {
            if (!TryPlace(out Vector3 position))
            {
                ReadyFlag = true;
                return;
            }
            _position = position;
            if ((_sim.Flags & SkyFlashSim.FlagGround) != 0)
            {
                float ground = Ground(_position, float.NaN);
                if (!float.IsNaN(ground))
                {
                    if ((_sim.Flags & SkyFlashSim.FlagHeightFromGround) != 0)
                        _sim.SetHeight(_sim.HeightOverGround(_position.y, ground));
                    _position.y = ground;
                }
            }
        }

        _sim.Place(_position.x, (float)((double)_position.y + _sim.Lift), _position.z);
    }

    bool TryPlace(out Vector3 position)
    {
        position = Vector3.zero;
        if (Locator == null || !Locator.TryResolve(out Matrix4x4 m))
            return false;
        position = m.GetColumn(3);
        return true;
    }

    static float Ground(Vector3 p, float fallback)
    {
        float g = EffectGround.HeightAt(p.x, p.y, p.z);
        return float.IsNaN(g) ? fallback : g;
    }

    public override void CollectStrips(List<EffectBillboardBatch.Strip> dest, Camera camera)
    {
        if (!IsAlive || dest == null || _texture == null)
            return;

        for (int i = 0; i < _strips.Length; i++)
        {
            ShockWaveSim.Cone cone = _sim.Cones[i];
            if (cone.Vertices == null)
                continue;
            Fill(_strips[i], new Vector3(cone.X, cone.Y, cone.Z), _turns[i], cone);
            dest.Add(_strips[i]);
        }
    }

    /// <summary>Pairs (bottom, top) in strip order; D3D v runs down the image, Unity's up.</summary>
    static void Fill(EffectBillboardBatch.Strip strip, Vector3 origin, Quaternion turn, ShockWaveSim.Cone cone)
    {
        int count = cone.Vertices.Length / 3;
        Color32 bottom = EffectBillboardBatch.ToColor32(cone.Bottom), top = EffectBillboardBatch.ToColor32(cone.Top);
        for (int v = 0; v < count; v++)
        {
            var local = new Vector3(cone.Vertices[v * 3], cone.Vertices[v * 3 + 1], cone.Vertices[v * 3 + 2]);
            strip.Positions[v] = origin + turn * local;
            strip.Uvs[v] = new Vector2(cone.Uvs[v * 2], 1f - cone.Uvs[v * 2 + 1]);
            strip.Colors[v] = (v & 1) == 0 ? bottom : top;
        }
        strip.Count = count;
    }
}
