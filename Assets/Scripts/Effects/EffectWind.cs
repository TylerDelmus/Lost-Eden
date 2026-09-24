using UnityEngine;

/// <summary>
/// The wind the effects see: stock's effect handler is a singleton whose <c>GetWind</c> /
/// <c>GetSmoothWind</c> any control can read, so this is a static too. It replays <see cref="EffectWindSim"/>
/// one stock frame per <see cref="EffectFrameRate.StockProcessSeconds"/>, once per Unity frame however
/// many handlers tick.
///
/// <see cref="WeatherSpeed"/> is the weather state's wind speed (+0x1c of the blended weather). The port
/// has no weather yet, so it stays 0: stock's own no-weather state, where only the gusts blow.
/// </summary>
public static class EffectWind
{
    static readonly EffectWindSim Sim = new EffectWindSim(() => Random.Range(0, 0x8000));
    static float _carry;
    static int _lastFrame = -1;

    /// <summary>The weather's wind speed, 0-33. 0 until a weather system sets it.</summary>
    public static float WeatherSpeed { get; set; }

    /// <summary>Stock <c>_EffectHandler_t::GetWind</c>.</summary>
    public static Vector3 Raw => new Vector3(Sim.X, Sim.Y, Sim.Z);

    /// <summary>Stock <c>_EffectHandler_t::GetSmoothWind</c>.</summary>
    public static Vector3 Smooth => new Vector3(Sim.SmoothX, Sim.SmoothY, Sim.SmoothZ);

    public static void Advance(float dt)
    {
        if (Time.frameCount == _lastFrame)
            return;
        _lastFrame = Time.frameCount;

        int steps = EffectFrameRate.TakeFixedSteps(ref _carry, dt, EffectFrameRate.StockProcessSeconds, 4);
        for (int i = 0; i < steps; i++)
            Sim.Step(WeatherSpeed);
    }
}
