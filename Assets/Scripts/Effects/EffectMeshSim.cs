using System;

/// <summary>
/// Stock <c>GfxControlEffectMesh_t</c> (type 3025, 0xbd1; vftable <c>Gamecode 1016f0d4</c>, loader
/// <c>1010caf8</c>, init <c>1010d457</c>, Process <c>1010ce9c</c>): one ABIFF model from a fixed name table,
/// drawn by a DisplaySystem <c>VisualMesh_t</c> that moves, turns, grows and fades.
///
/// Fields: 0 flags, 8 duration, 9 motion (1 = bob), 10 model (<see cref="ModelNames"/>), 16-18 velocity,
/// 19-21 acceleration, 22-24 spin axis (zero = up), 25 angle, 26 spin, 27 spin acceleration (radians),
/// 28 scale, 29 its rate, 30 that rate's rate, 31 rendering effect (<c>VisualMesh_t::SetRenderingEffect</c>),
/// 32.. a <see cref="StockFloatCurve"/> of alpha over age / duration. Fields 11-15 are loaded but unused
/// (init zeroes the offset that 13-15 fill).
///
/// Per call with delta dt (<c>1010cf94</c>..<c>1010d1d8</c>):
/// <list type="bullet">
/// <item>time += dt; alpha = curve(time / duration) when that is &gt;= 0, else 1.</item>
/// <item>Motion 1: offset.y += sin(2 pi age / duration) * velocity.y * dt. Otherwise offset += v dt, then
/// v += a dt.</item>
/// <item>angle += spin dt, spin += spin acceleration dt; scale += rate dt, rate += rate's rate dt.</item>
/// </list>
/// The visual goes to the locator's local-mode position (the ground under it with flag 0x100) plus the
/// offset, turned by the locator's turn composed with (axis, angle), scaled, with transparency alpha.
/// Flag 0x400 instead turns the model's up axis to the camera; flag 0x800 swaps alpha for a camera
/// distance ramp (<see cref="DistanceAlpha"/>).
///
/// Not modelled: the host dynel's body scale (0x200), Atrox model and size variants (0x10000, 0x8000),
/// the vehicle-direction fade (0x2000/0x4000), animated models (0x1000) and the dynel-bound variant.
///
/// No Unity dependency so the recovered maths can be asserted from plain unit tests.
/// </summary>
public sealed class EffectMeshSim
{
    public const int FlagLocalMode = 2;
    public const int FlagGround = 0x100;
    public const int FlagFaceCamera = 0x400;
    public const int FlagDistanceFade = 0x800;

    /// <summary>Body scale, animation, the vehicle fade and the Atrox variants.</summary>
    public const int UnmodelledFlags = 0x200 | 0x1000 | 0x2000 | 0x4000 | 0x8000 | 0x10000;

    /// <summary>
    /// The init's switch (<c>1010d4bb</c>, table <c>0x1010d7f6</c>); MParticle's (<c>1010f4c0</c>) is the
    /// first ten. Stock loads them as RDB meshes (type 1010001) by name.
    /// </summary>
    public static readonly string[] ModelNames =
    {
        "EP03_shoulder_rocket.abiff",
        "EP03_mech_heal_effect.abiff",
        "EP03_blast_wave_effect.abiff",
        "EP03_bullet_casing.abiff",
        "EP03_EMP_blast.abiff",
        "EP03_bomber_debris_01.abiff",
        "EP03_bomber_debris_02.abiff",
        "EP03_bomber_debris_03.abiff",
        "EP03_RUBIKA_asteroidbig.abiff",
        "EP03_RUBIKA_asteroidbig2.abiff",
        "EP03_preorder_mech_statel.abiff",
        "EP03_scout_mech_statel.abiff",
        "EP03_heavy_mech_statel.abiff",
        "EP03_anti_personnel_gun_statel.abiff",
        "EP03_anti_vehicle_gun_statel.abiff",
        "EP03_preorder_mech_upgraded_statel.abiff",
        "EP03_scout_mech_upgraded_statel.abiff",
        "EP03_heavy_mech_upgraded_statel.abiff",
        "EP03_anti_personnel_gun_upgraded_statel.abiff",
        "EP03_anti_vehicle_gun_upgraded_statel.abiff",
        "hoverbike_a_effect_circle.abiff",
        "hoverbike_b_effect_circle.abiff",
        "hoverboard_a_fan.abiff",
        "hoverboard_b_fan.abiff",
        "xan_grid_dependencybox.abiff",
        "penumbra_shoulderpads_atrox.abiff",
        "hoverboard_c_fx01.abiff",
        "hoverbike_b_effect_circle_red.abiff",
    };

    // 1010d0a6.
    const double Pi = 3.141592653589793;

    public readonly int Flags;
    public readonly float Duration;
    public readonly int Motion;
    public readonly int ModelIndex;
    public readonly int RenderingEffect;
    readonly StockFloatCurve _curve;
    float _vx, _vy, _vz;
    readonly float _ax, _ay, _az;
    float _spin;
    readonly float _spinAccel;
    float _scaleRate;
    readonly float _scaleRateRate;
    float _time;

    public float OffsetX { get; private set; }
    public float OffsetY { get; private set; }
    public float OffsetZ { get; private set; }
    public float AxisX { get; }
    public float AxisY { get; }
    public float AxisZ { get; }
    public float Angle { get; private set; }
    public float Scale { get; private set; }

    /// <summary>The curve's alpha this call (1 before the first).</summary>
    public float Alpha { get; private set; } = 1f;

    /// <summary>Stock +0xc8: the vehicle fade, 1 unless flags 0x6000 (not modelled).</summary>
    public float Fade => 1f;

    public EffectMeshSim(float[] fields)
    {
        Flags = Int(fields, 0);
        Duration = F(fields, 8);
        Motion = Int(fields, 9);
        ModelIndex = Int(fields, 10);
        _vx = F(fields, 16);
        _vy = F(fields, 17);
        _vz = F(fields, 18);
        _ax = F(fields, 19);
        _ay = F(fields, 20);
        _az = F(fields, 21);
        Angle = F(fields, 25);
        _spin = F(fields, 26);
        _spinAccel = F(fields, 27);
        Scale = F(fields, 28);
        _scaleRate = F(fields, 29);
        _scaleRateRate = F(fields, 30);
        RenderingEffect = Int(fields, 31);
        int index = 32;
        _curve = StockFloatCurve.Read(fields, ref index);

        // 1010d6e4: a zero axis is up; then set to length 1 (100439aa).
        float x = F(fields, 22), y = F(fields, 23), z = F(fields, 24);
        if (x == 0f && y == 0f && z == 0f)
            y = 1f;
        double len = Math.Sqrt((double)x * x + (double)y * y + (double)z * z);
        AxisX = (float)(x / len);
        AxisY = (float)(y / len);
        AxisZ = (float)(z / len);
    }

    public StockFloatCurve Curve => _curve;

    /// <summary>The model's file name, or null past the table (stock goes ready, <c>1010d7e3</c>).</summary>
    public static string ModelName(int index)
        => index >= 0 && index < ModelNames.Length ? ModelNames[index] : null;

    /// <summary>
    /// Whether the port draws this record as stock does: none of <see cref="UnmodelledFlags"/>, a rendering
    /// effect it knows (not 1 Holo, 2 Space or 7 ZBias), and effect 4, which is lit and taken at full light,
    /// only for the blast wave (model 2), whose emissive white makes that exact.
    /// </summary>
    public static bool IsModelled(float[] fields)
    {
        int flags = Int(fields, 0);
        int effect = Int(fields, 31);
        if ((flags & UnmodelledFlags) != 0 || effect is 1 or 2 or 7)
            return false;
        return effect != 4 || Int(fields, 10) == 2;
    }

    /// <summary>One Process body with the base age already advanced.</summary>
    public void Step(float dt, float age)
    {
        _time += dt;
        float t = _time / Duration;
        Alpha = t >= 0f ? _curve.Evaluate(t) : 1f;

        if (Motion == 1)
        {
            float s = (float)Math.Sin(age / Duration * Pi * 2.0);
            OffsetY = s * _vy * dt + OffsetY;
        }
        else
        {
            OffsetX += _vx * dt;
            OffsetY += _vy * dt;
            OffsetZ += _vz * dt;
            _vx += _ax * dt;
            _vy += _ay * dt;
            _vz += _az * dt;
        }

        Angle = _spin * dt + Angle;
        _spin = _spinAccel * dt + _spin;
        Scale = _scaleRate * dt + Scale;
        _scaleRate = _scaleRateRate * dt + _scaleRate;
    }

    /// <summary>
    /// Flag 0x800 (<c>1010d333</c>..<c>1010d393</c>): with the camera <paramref name="distance"/> from the
    /// model, 0 under 5 m, (d - 5) / 10 from 5 m to 10 m, and the curve's alpha from 10 m. A camera on the
    /// model is 0.
    /// </summary>
    public static float DistanceAlpha(float distance, float curveAlpha)
    {
        if (!(distance > 0f))
            return 0f;
        if (5f > distance)
            return 0f;
        if (distance < 10f)
            return (float)((distance - 5.0) / 10.0);
        return curveAlpha;
    }

    static float F(float[] f, int i) => f != null && i >= 0 && i < f.Length ? f[i] : 0f;

    static int Int(float[] f, int i) => f != null && i >= 0 && i < f.Length ? BitConverter.SingleToInt32Bits(f[i]) : 0;
}
