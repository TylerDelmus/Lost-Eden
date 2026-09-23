using System;

/// <summary>
/// Stock <c>GfxControlShield2_t</c> (type 3034, 0xbda; vftable <c>Gamecode 1016f72c</c>, loader
/// <c>1011148b</c>, init <c>101116a2</c>, Process <c>10111110</c>, slot 6 <c>10111071</c>) and the
/// DisplaySystem <c>GfxVisualShield2</c> it drives (ctor <c>1001e35d</c>, draw slot 13 <c>1001d3ee</c>,
/// vertex transform <c>1001d569</c>, CAT callback <c>1001dc74</c>).
///
/// It is <see cref="ShieldSim"/>'s layered cousin: the host's own skinned mesh, pushed out along its
/// normals, drawn <b>N times</b> — each copy a little larger than the last.
///
/// Fields, read in order from 9 by the loader: 0 flags, 8 duration, 9 the displacement mode, 10 the UV
/// mode, 11 the colour mode, 12 material, 13/14 the u/v scale, 15/16 the u/v scroll per unit of t,
/// 17 (loaded and handed to the visual, which never reads it), 18 the flicker rate, 19 the layer count,
/// 20 an EP03 mech model (0-9 picks an .abiff; anything else keeps the host's own mesh), then a
/// <see cref="StockColorCurve"/>, a <see cref="StockFloatCurve"/>, and a trailing effect id.
///
/// Flags: 0x400 additive (the visual's base ctor), 0x800 adds the global flicker, 0x1000 drops each
/// layer by its own amount, 0x2000 (the visual's +0x1cc, unread by the draw), 0x4000 cull front faces,
/// 0x8000 cull back faces and render priority 3, 0x10000 take the host's own material instead of
/// field 12's, 0x20000 / 0x40000 skip the draw when stopped / when the mesh is over 1000 vertices.
///
/// Per call the control sets <c>t = age / duration</c>, the colour curve's colour, the float curve's
/// value as <see cref="Alpha"/>, and follows the host CAT node's position, rotation and scale.
///
/// The geometry (<c>1001d569</c>), per source vertex, with a = <see cref="Alpha"/>:
/// <list type="bullet">
/// <item>mode 0: p' = (p + n * a) * bodyScale.</item>
/// <item>mode 1: p' = (p + n * sin(2x + 3y + z + 30t)^2 * a) * bodyScale. No record uses it.</item>
/// </list>
/// The UV (field 10): mode 0 takes the source vertex's own uv, <c>(uv + speed * t) * scale</c>; mode 1
/// is cylindrical, <c>u = atan2(x, z) * field13 / 2pi + field15 * t</c> and
/// <c>v = y * field14 + field16 * t</c>. Modes 2-5 exist and no record uses them.
///
/// The colour (field 11) scales the curve colour's alpha: mode 0 by one flicker for the whole mesh,
/// modes 1 and 2 per vertex by a three-sine field over the source position and t (mode 2 also by
/// <c>t * 1.5</c>). Anything above 2 leaves the alpha alone.
///
/// The draw then repeats the mesh for i in 0..N-1 at scale <c>1 + (i / N) * a</c>, dropped by
/// <c>(i / N) * a</c> with flag 0x1000.
///
/// Slot 6 only starts a 2 s window (<c>10111071</c> stores the age); the visual never reads the ramp the
/// control computes from it, so a graceful stop is a delay, not a fade. The control also calls slot 6 on
/// itself once <c>age &gt; duration - 2</c>.
///
/// No Unity dependency so the recovered maths can be asserted from plain unit tests.
/// </summary>
public sealed class Shield2Sim
{
    public const int FlagAdditive = 0x400;
    public const int FlagFlicker = 0x800;
    public const int FlagDropLayers = 0x1000;
    public const int FlagCullFront = 0x4000;
    public const int FlagCullBack = 0x8000;
    public const int FlagHostMaterial = 0x10000;
    public const int FlagHideWhenStopped = 0x20000;
    public const int FlagSkipBigMesh = 0x40000;

    /// <summary>The draw skips a mesh over this many vertices with 0x40000 (<c>1001d428</c>).</summary>
    public const int BigMesh = 1000;

    /// <summary>The stop window slot 6 opens, a hard 2.0 in the loader (<c>10111555</c>).</summary>
    public const float StopSeconds = 2f;

    public readonly int Flags;
    public readonly float Duration;
    public readonly int DisplaceMode;
    public readonly int UvMode;
    public readonly int ColourMode;
    public readonly int Material;
    public readonly float UScale, VScale, USpeed, VSpeed;
    public readonly float Unused17;
    public readonly float FlickerRate;
    public readonly int Layers;
    public readonly int MechModel;
    public readonly int EndEffectId;

    readonly StockColorCurve _colour;
    readonly StockFloatCurve _fade;

    /// <summary>The control's t (<c>age / duration</c>), the visual's +0x200.</summary>
    public float T { get; private set; }

    /// <summary>The float curve at <see cref="T"/>, the visual's +0x1c8.</summary>
    public float Alpha { get; private set; }

    /// <summary>The colour curve at <see cref="T"/>, the visual's base colour +0x18c.</summary>
    public uint Argb { get; private set; } = 0xffffffffu;

    /// <summary>Set by slot 6; the age it was set at.</summary>
    public bool Stopping { get; private set; }
    public float StopAge { get; private set; }

    public bool Additive => (Flags & FlagAdditive) != 0;
    public bool HostMaterial => (Flags & FlagHostMaterial) != 0;
    public bool DropsLayers => (Flags & FlagDropLayers) != 0;

    /// <summary>0 no culling, 1 cull back faces (0x8000), 2 cull front faces (0x4000) — <c>1001d20d</c>.</summary>
    public int CullMode => (Flags & FlagCullBack) != 0 ? 1 : (Flags & FlagCullFront) != 0 ? 2 : 0;

    /// <summary>Field 20 in 0..9 puts the shield on an EP03 mech model instead of the host (<c>10111888</c>).</summary>
    public bool OnMechModel => MechModel >= 0 && MechModel <= 9;

    public Shield2Sim(float[] fields)
    {
        Flags = Int(fields, 0);
        Duration = F(fields, 8);
        DisplaceMode = Int(fields, 9);
        UvMode = Int(fields, 10);
        ColourMode = Int(fields, 11);
        Material = Int(fields, 12);
        UScale = F(fields, 13);
        VScale = F(fields, 14);
        USpeed = F(fields, 15);
        VSpeed = F(fields, 16);
        Unused17 = F(fields, 17);
        FlickerRate = F(fields, 18);
        Layers = Int(fields, 19);
        MechModel = Int(fields, 20);
        int index = 21;
        _colour = StockColorCurve.Read(fields, ref index);
        _fade = StockFloatCurve.Read(fields, ref index);
        EndEffectId = Int(fields, index);
        Alpha = _fade.Evaluate(0f);
        Argb = _colour.Evaluate(0f);
    }

    public StockColorCurve Colour => _colour;
    public StockFloatCurve Fade => _fade;

    /// <summary><c>10111071</c>: the first terminate opens the 2 s window.</summary>
    public void Terminate(float age)
    {
        if (Stopping)
            return;
        Stopping = true;
        StopAge = age;
    }

    /// <summary>
    /// One Process body (<c>10111236</c>..<c>101112e5</c>). Returns false once stock would be ready:
    /// the stop window has run out.
    /// </summary>
    public bool Step(float age)
    {
        if (Stopping)
        {
            // 1011124b: the ramp the control computes; the visual never reads it (Docs §9).
            float ramp = (age - StopAge) / StopSeconds;
            if (1f < ramp)
                return false;
        }

        T = Duration != 0f ? age / Duration : 0f;
        Argb = _colour.Evaluate(T);
        Alpha = _fade.Evaluate(T);
        return true;
    }

    /// <summary><c>101112e5</c>: the control terminates itself 2 s before the duration.</summary>
    public bool ShouldTerminate(float age) => 0f < Duration && Duration - StopSeconds < age;

    /// <summary>
    /// The displacement along the normal for a source vertex (<c>1001d569</c>). Mode 1's wave is
    /// <c>sin(2x + 3y + z + 30t)^2</c>; no record uses it.
    /// </summary>
    public float Displacement(float x, float y, float z)
    {
        if (DisplaceMode != 1)
            return Alpha;
        float s = (float)Math.Sin(x + x + 3.0 * y + z + T * 30.0);
        return s * s * Alpha;
    }

    /// <summary>The layer ramp, <c>(i / N) * alpha</c> (<c>1001d496</c>).</summary>
    public float LayerAmount(int layer)
        => Layers > 0 ? (float)layer / Layers * Alpha : 0f;

    /// <summary>The layer's uniform scale, <c>1 + LayerAmount</c> (<c>1001d4ae</c>).</summary>
    public float LayerScale(int layer) => LayerAmount(layer) + 1f;

    /// <summary><c>1001d940</c> (mode 0) and <c>1001d8e7</c> (mode 1); other modes are not ported.</summary>
    public void Uv(float srcU, float srcV, float x, float y, float z, out float u, out float v)
    {
        if (UvMode == 1)
        {
            u = (float)(Math.Atan2(x, z) * (UScale / 6.2831854820251465) + USpeed * T);
            v = y * VScale + VSpeed * T;
            return;
        }

        u = (srcU + USpeed * T) * UScale;
        v = (srcV + VSpeed * T) * VScale;
    }

    /// <summary>
    /// The global flicker with flag 0x800 (<c>1001dba6</c> / <c>1001d9ba</c>), else 1. All three terms
    /// run off <c>d = t * field18 * 10</c>.
    /// </summary>
    public float Flicker()
    {
        if ((Flags & FlagFlicker) == 0)
            return 1f;
        double d = (double)T * FlickerRate * 10.0;
        double a = Math.Cos(d * 1.100000023841858 * 2.5);
        double b = Math.Sin(d * 2.299999952316284) * a;
        return (float)(Math.Sin(d * 1.2000000476837158 * 1.5) * b * 0.10000000149011612 + 0.5);
    }

    /// <summary>
    /// The per-vertex alpha scale for colour modes 1 and 2 (<c>1001da44</c>..<c>1001db33</c>): three
    /// sines over the source position and <c>T = t * field18</c>, mode 2 also times <c>t * 1.5</c>.
    /// </summary>
    public float VertexScale(float x, float y, float z, float flicker)
    {
        double t = (double)T * FlickerRate;
        double e1 = (4.0 * x + 2.5 * y + 3.0 * z + t * 1.100000023841858) * 2.5;
        double n1 = Math.Cos(e1);
        double e2 = (2.5 * x + 3.0 * y + 4.0 * z + t) * 2.299999952316284;
        double n2 = Math.Sin(e2) * n1;
        double e3 = (3.0 * x + 4.0 * y + 1.5 * z + t * 1.2000000476837158) * 1.5;
        double m = Math.Sin(e3) * n2 * flicker;
        if (ColourMode == 2)
            m = (double)T * 1.5 * m;
        return (float)m;
    }

    /// <summary>The curve colour's alpha scaled and clamped to a byte (<c>1001db44</c>).</summary>
    public int ScaleAlpha(float scale)
    {
        int a = (int)((Argb >> 24) * (double)scale);
        return a < 0 ? 0 : a > 255 ? 255 : a;
    }

    static float F(float[] f, int i) => f != null && i >= 0 && i < f.Length ? f[i] : 0f;

    static int Int(float[] f, int i) => f != null && i >= 0 && i < f.Length ? BitConverter.SingleToInt32Bits(f[i]) : 0;
}
