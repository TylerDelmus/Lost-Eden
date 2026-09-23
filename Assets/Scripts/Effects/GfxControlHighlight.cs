using UnityEngine;

/// <summary>
/// typeCode 2011 (0x7db), stock <c>_GfxControlHighlight_t</c>: no visual of its own — it tints the
/// target's existing mesh. The field map and the phase curves are <see cref="HighlightSim"/>.
///
/// Stock reads its colour ramp (<c>101086fa</c>) at that phase and pushes the four values straight into
/// randy31: the first becomes <c>RRefFrame_t::SetTransparency</c> and the other three
/// <c>SetEmissive</c>; mode 3 also calls <c>SetSpecular</c>. The port does the same through
/// <see cref="EffectMeshTint"/> over the locator's highlight root.
///
/// Two places the port is not stock, neither reached by a nano (§9): stock's mode 3 skips the body's own
/// node and only tints the attached items, filtered by their +4; and flag 0x400 makes stock register a
/// per-mesh callback (<c>100e309b</c> / <c>100e2e80</c>) instead of walking from the root.
/// </summary>
public sealed class GfxControlHighlight : GfxControl
{
    readonly HighlightSim _sim;
    readonly Color _start;
    readonly Color _stop;
    readonly EffectMeshTint _tint = new EffectMeshTint();

    public HighlightSim Sim => _sim;

    public GfxControlHighlight(GfxTweakRecord record, EffectLocator locator)
        : base(record, locator)
    {
        _sim = new HighlightSim(record?.Fields);

        // 101085de: eight floats from field 3, each block (transparency, r, g, b).
        _start = EffectColors.ReadArgbBlock(record, 3, new Color(0f, 0f, 0f, 0f));
        _stop = EffectColors.ReadArgbBlock(record, 7, Color.white);

        // 100e2e1e: mode 2 runs until something ends it; the others take field 2.
        base.SetDuration(_sim.Mode == HighlightSim.ModeHold ? InfiniteDuration : _sim.Duration);
    }

    /// <summary>Stock slot 8 (<c>100e2da5</c>): +0x10 = seconds.</summary>
    public override void SetDuration(float seconds) => base.SetDuration(seconds);

    /// <summary>
    /// Stock slot 6 (<c>100e2d91</c>): mode 2 gets <c>duration = pulse + age</c> so it fades out over one
    /// pulse; every mode then raises +0x38. It does <b>not</b> end the control — the duration does.
    /// </summary>
    protected override void OnTerminateGracefully()
    {
        if (_sim.Mode == HighlightSim.ModeHold)
            base.SetDuration(_sim.Pulse + Age);
        _sim.Terminate();
    }

    protected override void OnProcess(float dt)
    {
        if (Locator == null || !Locator.TryGetHighlightRoot(out GameObject root) || root == null)
        {
            _tint.Clear();
            ReadyFlag = true;
            return;
        }

        _tint.Bind(root);

        float t = Mathf.Clamp01(_sim.Phase(Age, Duration));
        Color c = Color.Lerp(_start, _stop, t);
        _tint.Apply(new Color(c.r, c.g, c.b, 1f), c.a, writeSpecular: _sim.WritesSpecular);
    }

    protected override void OnReleased(bool immediate)
    {
        _tint.Clear();
    }
}
