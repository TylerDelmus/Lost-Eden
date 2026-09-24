using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// typeCode 3029 (0xbd5), stock <c>GfxControlScatter_t</c>. The schedule and the scatter maths are
/// <see cref="ScatterSim"/>; this places, owns and draws the copies.
///
/// Stock makes each copy through the handler's factories **without registering it** (<c>100cea4b</c>
/// for a world point, <c>100cf4fe</c> / <c>100cfdc6</c> for a locator — none of them followed by
/// <c>100ce4e5</c>), so the Scatter processes and draws its own children, exactly as Toggle does
/// (§5.36). The port does the same through <see cref="IEffectSpawnFactory.CreateOwnedControl"/>.
///
/// Without flag 0x2000 a copy is made at a world point: the locator's position, plus the slot's
/// scatter offset, dropped to the ground with 0x1000, plus fields 1-3. Such a copy does **not** follow
/// the locator afterwards. With 0x2000 it is made on the locator itself and the offset is unused.
///
/// Stock's locator modes pick the base differently (a stored point, a matrix it snapshotted from the
/// host's mesh frame, the dynel's position, or the locator object's matrix); the port takes the
/// locator's resolved position for all of them, and its matrix snapshot is the locator itself (§9).
/// </summary>
public sealed class GfxControlScatter : GfxControl
{
    readonly ScatterSim _sim;
    readonly IEffectSpawnFactory _factory;
    readonly GfxControl[] _children;
    readonly System.Func<float, float, float, float> _ground = EffectGround.HeightAt;

    public ScatterSim Sim => _sim;

    public GfxControlScatter(
        GfxTweakRecord record,
        EffectLocator locator,
        IEffectSpawnFactory factory,
        Color tint)
        : base(record, locator)
    {
        _factory = factory;
        _sim = new ScatterSim(record?.Fields, () => Random.value);
        _children = new GfxControl[_sim.Count];
        // Field 7 is the base timer's duration (+0x10).
        base.SetDuration(_sim.Duration > 0f ? _sim.Duration : InfiniteDuration);
    }

    /// <summary>Stock slot 6 is <c>100d2b34</c>, the base flag only; 0x4000 ends it at the next call.</summary>
    protected override void OnTerminateGracefully() { }

    protected override void OnProcess(float dt)
    {
        // 101108bc: with 0x4000 a terminate ends it at once.
        if (IsTerminating && _sim.EndsOnTerminate)
        {
            ReadyFlag = true;
            return;
        }

        if (_factory == null || _sim.Count == 0 || _sim.ChildId == 0)
        {
            ReadyFlag = true;
            return;
        }

        float elapsed = _sim.Elapsed(Age);
        bool allDone = true;

        for (int i = 0; i < _sim.Count; i++)
        {
            if (!_sim.Fired(i))
            {
                // 101109b1: a slot whose time has not come keeps the Scatter alive.
                if (elapsed < _sim.FireTime(i))
                {
                    allDone = false;
                    continue;
                }

                _children[i] = Spawn();
                _sim.MarkFired(i);
                if (_children[i] != null)
                    allDone = false;
            }

            GfxControl child = _children[i];
            if (child == null)
                continue;

            // 10110c4a: the Scatter drives its own children and drops them once they are ready.
            if (child.Process(dt))
                allDone = false;
            else
            {
                child.Release(true);
                _children[i] = null;
            }
        }

        if (!allDone)
            return;

        // 10110c8d: 0x400 starts the whole schedule again instead of ending.
        if (_sim.Repeats && !IsTerminating)
            _sim.Rearm(Age);
        else
            ReadyFlag = true;
    }

    GfxControl Spawn()
    {
        _sim.NextOffset(out float ox, out float oy, out float oz);

        if (_sim.OnLocator)
            return _factory.CreateOwnedControl(_sim.ChildId, Locator);

        if (Locator == null || !Locator.TryResolve(out Matrix4x4 world))
            return null;

        Vector3 p = (Vector3)world.GetColumn(3) + new Vector3(ox, oy, oz);
        // 10110b86: 0x1000 puts the copy on the ground before fields 1-3 are added.
        if (_sim.Ground)
        {
            float g = _ground(p.x, p.y, p.z);
            if (!float.IsNaN(g))
                p.y = g;
        }
        p += new Vector3(_sim.OffsetX, _sim.OffsetY, _sim.OffsetZ);
        return _factory.CreateOwnedControl(_sim.ChildId, EffectLocator.WorldPoint(p, Quaternion.identity));
    }

    public override void CollectBillboards(List<EffectBillboardBatch.Quad> dest, Camera camera)
    {
        if (!IsAlive)
            return;
        for (int i = 0; i < _children.Length; i++)
            _children[i]?.CollectBillboards(dest, camera);
    }

    public override void CollectStrips(List<EffectBillboardBatch.Strip> dest, Camera camera)
    {
        if (!IsAlive)
            return;
        for (int i = 0; i < _children.Length; i++)
            _children[i]?.CollectStrips(dest, camera);
    }

    public override void CollectMeshes(List<EffectBillboardBatch.MeshDraw> dest, Camera camera)
    {
        if (!IsAlive)
            return;
        for (int i = 0; i < _children.Length; i++)
            _children[i]?.CollectMeshes(dest, camera);
    }

    protected override void OnReleased(bool immediate)
    {
        for (int i = 0; i < _children.Length; i++)
        {
            _children[i]?.Release(true);
            _children[i] = null;
        }
    }
}
