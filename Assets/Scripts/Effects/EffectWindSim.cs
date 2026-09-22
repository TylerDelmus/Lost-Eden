using System;

/// <summary>
/// The client's wind: the weather controller's wind object (Gamecode, 0x58 bytes; ctor <c>100bfece</c>,
/// strength <c>100bff68</c>, update <c>100bfc80</c>, read <c>100bfbf0</c>) and the effect handler's copy of it
/// (<c>_EffectHandler_t::ComputeWind</c> <c>100cdee7</c>: GetWind = the raw vector, GetSmoothWind = a
/// 0.96 / 0.04 running blend). One <see cref="Step"/> is one stock frame.
///
/// State: W the base wind (+0, starts (1, 0, 0), <c>100b90e0</c>), A the output (+0xc), B and C two gusts
/// (+0x18, +0x24), D (+0x30, never set after the reset, so zero), the strength s (+0x44) and the gust
/// weight g (+0x3c).
///
/// Per frame, from the weather update (<c>100beacd</c>):
/// <list type="bullet">
/// <item>Strength(speed), speed = the weather state's wind speed (+0x1c): s = clamp(speed, 0, 33), at least
/// 0.001; W keeps its direction at length s (W.x = s if W is zero); g = 1 - (1 - s / 33)³.</item>
/// <item>Update(p = 0.9, k = 0.5): with m = 0.5 g + 0.5 and each kick drawn as (rand() % 100 - 50) / 450
/// / 0.016 (y: / 800), 9 frames in 10 (rand() % 100 &lt; 100 p) C += (kx, 0, kz) k m; again 9 in 10,
/// B += (kx, ky, kz) k m. Then A = W + B, B /= 1.02, C /= 1.02, and while C != D, W += (C - D) / 1000.</item>
/// </list>
/// The handler then copies A (raw) and blends smooth = raw * 0.04 + smooth * 0.96. The update is skipped
/// when the display preference byte +3 (<c>EnvironmentPreferences_t</c>) is off; the port always runs it.
///
/// No Unity dependency so the recovered maths can be asserted from plain unit tests.
/// </summary>
public sealed class EffectWindSim
{
    // 100c0101 (floor), 100bff6b (cap); the gust chance and scale are the weather controller's +0x50 / +0x4c
    // (ctor 100be280 / 100be275), passed at 100beecf.
    public const float MinStrength = 0.001f;
    public const float MaxStrength = 33f;
    public const float GustChance = 0.9f;
    public const float GustScale = 0.5f;

    float _wx = 1f, _wy, _wz;
    float _ax, _ay, _az;
    float _bx, _by, _bz;
    float _cx, _cy, _cz;
    float _strength;
    float _gust;
    float _sx, _sy, _sz;
    readonly Func<int> _rand;

    /// <summary>The handler's raw wind (GetWind, +0x24).</summary>
    public float X { get; private set; }
    public float Y { get; private set; }
    public float Z { get; private set; }

    /// <summary>The handler's smoothed wind (GetSmoothWind, +0x30).</summary>
    public float SmoothX => _sx;
    public float SmoothY => _sy;
    public float SmoothZ => _sz;

    public float Strength => _strength;
    public float Gust => _gust;

    /// <param name="rand">Stock <c>rand()</c>: 0..0x7fff.</param>
    public EffectWindSim(Func<int> rand)
    {
        _rand = rand ?? throw new ArgumentNullException(nameof(rand));
    }

    /// <summary>One stock frame with the weather's wind speed.</summary>
    public void Step(float weatherSpeed)
    {
        SetStrength(weatherSpeed);
        Update(GustChance, GustScale);

        // ComputeWind (100cdee7).
        X = _ax;
        Y = _ay;
        Z = _az;
        _sx = (float)(X * 0.03999999910593033) + (float)(_sx * 0.9599999785423279);
        _sy = (float)(Y * 0.03999999910593033) + (float)(_sy * 0.9599999785423279);
        _sz = (float)(Z * 0.03999999910593033) + (float)(_sz * 0.9599999785423279);
    }

    /// <summary><c>100bff68</c>, less the wind sound.</summary>
    void SetStrength(float speed)
    {
        float s = speed;
        if (s > MaxStrength)
            s = MaxStrength;
        else if (!(s >= 0f))
            s = 0f;
        if (!(MinStrength <= s))
            s = MinStrength;
        _strength = s;

        if (_wx == 0f && _wy == 0f && _wz == 0f)
        {
            _wx = s;
        }
        else
        {
            double len = Math.Sqrt((double)_wx * _wx + (double)_wy * _wy + (double)_wz * _wz);
            _wx = (float)(_wx / len * s);
            _wy = (float)(_wy / len * s);
            _wz = (float)(_wz / len * s);
        }

        float t = (float)(1.0 - s / 33.0);
        _gust = (float)(1.0 - t * (double)t * t);
    }

    /// <summary><c>100bfc80</c>.</summary>
    void Update(float chance, float k)
    {
        double percent = chance * 100.0;
        float m = (float)(_gust * 0.5 + 0.5);
        if (_rand() % 100 < percent)
        {
            float kx = Kick(450.0), kz = Kick(450.0);
            _cx += kx * k * m;
            _cz += kz * k * m;
        }
        if (_rand() % 100 < percent)
        {
            float kx = Kick(450.0), ky = Kick(800.0), kz = Kick(450.0);
            _bx += kx * k * m;
            _by += ky * k * m;
            _bz += kz * k * m;
        }

        _ax = _wx + _bx;
        _ay = _wy + _by;
        _az = _wz + _bz;
        _bx = (float)(_bx / 1.0199999809265137);
        _by = (float)(_by / 1.0199999809265137);
        _bz = (float)(_bz / 1.0199999809265137);
        _cx = (float)(_cx / 1.0199999809265137);
        _cy = (float)(_cy / 1.0199999809265137);
        _cz = (float)(_cz / 1.0199999809265137);
        if (_cx != 0f || _cy != 0f || _cz != 0f)
        {
            _wx = (float)(_cx / 1000.0) + _wx;
            _wy = (float)(_cy / 1000.0) + _wy;
            _wz = (float)(_cz / 1000.0) + _wz;
        }
    }

    float Kick(double divisor) => (float)((_rand() % 100 - 50) / divisor / 0.01600000075995922);
}
