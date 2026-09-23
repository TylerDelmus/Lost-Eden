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
/// Built on a dynel (the ctor at <c>1010da22</c>, kind 2, the only one that keeps the dynel at +0xd8), the
/// loader (<c>1010cce0</c>..<c>1010cd65</c>) reads the dynel's breed (stat 4) and, with 0x200, its body scale
/// (<c>n3VisualDynel_t::GetBodyScale</c>, stat 360 / 100), which Process reads again every call
/// (<c>1010cf2d</c>). The model is drawn at body scale × scale, and the locator's frame is divided by the
/// body scale before its turn is taken (<c>1010d1f5</c>). With 0x8000 an Atrox (breed 4) multiplies field 28
/// by 1.45 once; with 0x10000 an Atrox takes <see cref="AtroxModelName"/> instead (<c>1010d46a</c>).
///
/// With 0x1000 the model's animation plays (<c>1010d265</c>): anim time += dt; above 0, with a tree total
/// above 0, it wraps by fmod once past the total and goes to <c>VisualMesh_t::SetAnimationTime</c>.
///
/// Not modelled: the vehicle-direction fade (0x2000/0x4000).
///
/// No Unity dependency so the recovered maths can be asserted from plain unit tests.
/// </summary>
public sealed class EffectMeshSim
{
    public const int FlagLocalMode = 2;
    public const int FlagGround = 0x100;
    public const int FlagFaceCamera = 0x400;
    public const int FlagDistanceFade = 0x800;

    public const int FlagBodyScale = 0x200;
    public const int FlagAtroxSize = 0x8000;
    public const int FlagAtroxModel = 0x10000;
    public const int FlagAnimated = 0x1000;

    /// <summary>The vehicle fade.</summary>
    public const int UnmodelledFlags = 0x2000 | 0x4000;

    /// <summary>Stat 4 (breed) of an Atrox.</summary>
    public const int AtroxBreed = 4;

    /// <summary>1016f0c0: 0x8000's size factor for an Atrox.</summary>
    public const double AtroxSize = 1.4500000476837158;

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

    /// <summary>The dynel's breed (stat 4), 0 when not built on a dynel.</summary>
    public int HostBreed { get; }

    /// <summary>Stock +0xd0: the dynel's body scale with 0x200, else 1.</summary>
    public float BodyScale { get; set; } = 1f;

    /// <summary>The visual's scale (<c>1010d40b</c>): body scale × scale.</summary>
    public float DrawScale => (float)((double)BodyScale * Scale);

    /// <summary>Stock +0xbc: the animation's time with 0x1000.</summary>
    public float AnimTime { get; private set; }

    /// <summary>Whether SetAnimationTime has been called (with 0x1000, once the time is past 0).</summary>
    public bool Animating { get; private set; }

    /// <summary>
    /// 1010d265: with 0x1000, anim time += dt; when it and <paramref name="treeTotal"/> are above 0 it wraps to
    /// fmod(time, total) once past the total, and true says SetAnimationTime(anim time) is called.
    /// </summary>
    public bool StepAnimation(float dt, float treeTotal)
    {
        if ((Flags & FlagAnimated) == 0)
            return false;
        AnimTime = (float)((double)AnimTime + dt);
        if (!(0f < AnimTime) || !(0f < treeTotal))
            return false;
        if (treeTotal < AnimTime)
            AnimTime = (float)((double)AnimTime % treeTotal);
        Animating = true;
        return true;
    }

    /// <param name="hostBreed">The dynel's breed when built on one (kind 2), else 0.</param>
    public EffectMeshSim(float[] fields, int hostBreed = 0)
    {
        HostBreed = hostBreed;
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

        // 1010cd41: an Atrox's size, once.
        if ((Flags & FlagAtroxSize) != 0 && hostBreed == AtroxBreed)
            Scale = (float)(Scale * AtroxSize);

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
    /// 1010d46a: with 0x10000 an Atrox host takes models 20, 21 and 27's Atrox versions; any other model gives
    /// null (stock goes ready). Otherwise <see cref="ModelName(int)"/>.
    /// </summary>
    public string Model
    {
        get
        {
            if ((Flags & FlagAtroxModel) == 0 || HostBreed != AtroxBreed)
                return ModelName(ModelIndex);
            return AtroxModelName(ModelIndex);
        }
    }

    /// <summary>The Atrox versions (<c>1016f4c8</c>, <c>1016f4f0</c>, <c>1016f518</c>).</summary>
    public static string AtroxModelName(int index) => index switch
    {
        20 => "hoverbike_a_effect_circle_atrox.abiff",
        21 => "hoverbike_b_effect_circle_atrox.abiff",
        27 => "hoverbike_b_effect_circle_red_atrox.abiff",
        _ => null,
    };

    /// <summary>
    /// Whether the port draws this record as stock does: none of <see cref="UnmodelledFlags"/> and a
    /// rendering effect it knows (not 1 Holo, 2 Space or 7 ZBias).
    ///
    /// Effect 4 used to be carved out for every model but the blast wave, because the port faked its
    /// lighting with a constant that is only right when the emissive is white. It is now drawn through
    /// a lit additive material (<see cref="EffectModels.Part.LitAdditive"/>), matching the render
    /// states stock's effect object sets, so any model is as faithful as the effect-0 path.
    /// </summary>
    public static bool IsModelled(float[] fields)
    {
        int flags = Int(fields, 0);
        int effect = Int(fields, 31);
        return (flags & UnmodelledFlags) == 0 && effect is not (1 or 2 or 7);
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
