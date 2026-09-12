using AODB.Common.Enums;
using AODB.Common.RDBObjects;
using UnityEngine;

/// <summary>
/// Resolves nano visual effect ids from template stats.
/// Stock: cast 428 → tracer 419 (anim) → impact 414 then hit 361 on target.
/// </summary>
public static class NanoEffectResolver
{
    public struct SpellFx
    {
        public int EffectId;
        public Color Color;
        public bool HasColor;
    }

    public static bool TryResolveCast(NanoSpell nano, out SpellFx fx)
        => TryResolveStat(nano, StatId.casteffecttype, out fx);

    public static bool TryResolveTrace(NanoSpell nano, out SpellFx fx)
        => TryResolveStat(nano, StatId.tracereffecttype, out fx);

    public static bool TryResolveImpact(NanoSpell nano, out SpellFx fx)
        => TryResolveStat(nano, StatId.impacteffecttype, out fx);

    public static bool TryResolveHit(NanoSpell nano, out SpellFx fx)
        => TryResolveStat(nano, StatId.hiteffecttype, out fx);

    /// <summary>Legacy alias — cast effect only.</summary>
    public static bool TryResolve(NanoSpell nano, out SpellFx fx)
        => TryResolveCast(nano, out fx);

    static bool TryResolveStat(NanoSpell nano, StatId stat, out SpellFx fx)
    {
        fx = new SpellFx { Color = Color.white };
        ItemBase template = nano?.Template;
        if (template?.Stats == null)
            return false;

        if (!template.Stats.TryGetValue(stat, out uint effect) || effect == 0)
            return false;

        fx.EffectId = (int)effect;
        return fx.EffectId != 0 && fx.EffectId != EffectTypeTags.RejectedEffectId;
    }

    public static Color FromArgb(int argb)
    {
        byte a = (byte)((argb >> 24) & 0xff);
        byte r = (byte)((argb >> 16) & 0xff);
        byte g = (byte)((argb >> 8) & 0xff);
        byte b = (byte)(argb & 0xff);
        float af = a == 0 ? 1f : a / 255f;
        return new Color(r / 255f, g / 255f, b / 255f, af);
    }
}
