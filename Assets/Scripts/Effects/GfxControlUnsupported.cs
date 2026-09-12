using UnityEngine;

/// <summary>
/// Unsupported / unknown typeCode placeholder — finishes immediately with no draw.
/// Prevents bogus billboard fallbacks (white squares from wrong material fields).
/// </summary>
public sealed class GfxControlUnsupported : GfxControl
{
    public GfxControlUnsupported(GfxTweakRecord record, EffectLocator locator)
        : base(record, locator)
    {
        ReadyFlag = true;
    }
}
