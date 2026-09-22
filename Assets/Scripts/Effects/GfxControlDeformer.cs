using UnityEngine;

/// <summary>
/// typeCode 3001 (0xbb9), stock <c>_GfxControlDeformer_t</c>, mode 1: wobbles the host's CAT mesh along
/// its normals. The mechanics are <see cref="DeformerSim"/>; <see cref="CatMeshDeformHost"/> applies
/// them to the skinned mesh each frame. Draws nothing of its own.
/// </summary>
public sealed class GfxControlDeformer : GfxControl
{
    readonly DeformerSim _sim;
    readonly CatMeshDeformHost _host;

    public DeformerSim Sim => _sim;

    public GfxControlDeformer(GfxTweakRecord record, EffectLocator locator)
        : base(record, locator)
    {
        _sim = new DeformerSim(record?.Fields);
        base.SetDuration(_sim.Duration);

        // Stock readies the control when its dynel has no CAT render (100d7cd1).
        if (locator == null || !locator.TryGetHighlightRoot(out GameObject root) || root == null)
        {
            ReadyFlag = true;
            return;
        }

        _host = CatMeshDeformHost.For(root);
        _host.Add(this);
    }

    public override void SetDuration(float seconds)
    {
        base.SetDuration(seconds);
        _sim.Duration = seconds;
    }

    /// <summary>Stock slot 6 starts the fade-out rather than ending the effect.</summary>
    protected override void OnTerminateGracefully() => _sim.Terminate(Age);

    protected override void OnProcess(float dt)
    {
        // _GfxControl_t::Process readies the control before the body once 0 <= duration < age.
        if (Duration >= 0f && Duration < Age)
        {
            ReadyFlag = true;
            return;
        }
        if (_sim.Step(Age))
            ReadyFlag = true;
    }

    protected override void OnReleased(bool immediate) => _host?.Remove(this);

    /// <summary>Mode 1's push along the normal for a vertex with source position b, now.</summary>
    public float WobbleWeight(float bx, float by, float bz) => _sim.WobbleWeight(Age, bx, by, bz);
}
