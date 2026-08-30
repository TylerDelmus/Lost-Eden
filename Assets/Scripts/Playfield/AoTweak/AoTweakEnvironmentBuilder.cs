using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using static AoTweakObjectParser;

/// <summary>
/// Builds a <see cref="PlayfieldEnvironmentTweak"/> from flattened AO tweak objects.
/// Evaluates only the frozen day-time subset (no live expression VM).
/// </summary>
public static class AoTweakEnvironmentBuilder
{
    static readonly HashSet<string> SkipObjectNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "GAME", "SunLight", "LiquidTweaks", "BackgroundSort", "DebugMonitor",
        "PlayfieldData", "RKPP", "RKWP", "Atmosphere", "AtmosphereMesh",
        "DotStars", "DotStarsMesh", "ParticleCluster",
        "ProgressiveLava", "ProgressiveLavaAnim1", "ProgressiveLavaAnim2", "ProgressiveLavaAnim3",
        "RubiKa_SingleClouds"
    };

    public static PlayfieldEnvironmentTweak Build(Dictionary<string, AoObject> objects)
    {
        var tweak = new PlayfieldEnvironmentTweak();
        if (objects == null || objects.Count == 0)
            return tweak;

        var vars = new AoTweakVariableContext(objects);
        vars.ExportGameVariables(tweak.GameVariables);

        float dayTime = 0f;
        float dayTimeFactor = 0.5f;
        if (objects.TryGetValue("GAME", out AoObject game))
        {
            if (vars.TryResolveProperty(game, game, "CurrentDayTime", out float cdt))
                dayTime = cdt;
            else if (vars.TryResolveProperty(game, game, "GameDayTime", out float gdt))
                dayTime = gdt;

            if (vars.TryResolveProperty(game, game, "DayTimeFactor", out float dtf) && dtf >= 0f && dtf <= 1.5f)
                dayTimeFactor = Mathf.Clamp01(dtf);
            else if (dayTime > 0f)
                dayTimeFactor = Mathf.Clamp01(dayTime / 6480f);

            if (TryGetQuaternion(game, "Sun1Rotation", out Quaternion sunRot))
            {
                tweak.SunRotation = sunRot;
                tweak.HasLighting = true;
            }
        }

        tweak.DayTimeFactor = dayTimeFactor;

        if (objects.TryGetValue("SunLight", out AoObject sunLight))
        {
            Color ground = SampleRgb(sunLight, "GroundLightR", "GroundLightG", "GroundLightB", dayTimeFactor);
            // AO clamps *2 <| 1.0 for ground light
            ground.r = Mathf.Min(ground.r * 2f, 1f);
            ground.g = Mathf.Min(ground.g * 2f, 1f);
            ground.b = Mathf.Min(ground.b * 2f, 1f);
            tweak.GroundLightColor = ground;

            if (sunLight.Properties.TryGetValue("AmbientLight", out AoProperty ambientArr)
                && ambientArr.FloatArray != null && ambientArr.FloatArray.Length > 0)
            {
                float a = SampleArray(ambientArr.FloatArray, dayTimeFactor);
                tweak.AmbientLightColor = new Color(a, a, a, 1f);
            }

            tweak.HasLighting = true;
        }

        if (objects.TryGetValue("Atmosphere", out AoObject atmosphere))
        {
            Color bottom = SampleRgb(atmosphere, "ColorBottomR", "ColorBottomG", "ColorBottomB", dayTimeFactor);
            Color middle = SampleRgb(atmosphere, "ColorMiddleR", "ColorMiddleG", "ColorMiddleB", dayTimeFactor);
            tweak.FogTint = Color.Lerp(bottom, middle, 0.35f);
            if (TryGetFloat(atmosphere, "AddFogI", out float fogI))
                tweak.FogDensityHint = Mathf.Clamp(fogI, 0.001f, 0.2f);
            else if (vars.TryResolveProperty(atmosphere, atmosphere, "AddFogI", out float addFogI))
                tweak.FogDensityHint = Mathf.Clamp(addFogI, 0.001f, 0.2f);
            tweak.HasFog = true;
        }

        foreach (KeyValuePair<string, AoObject> pair in objects)
        {
            if (SkipObjectNames.Contains(pair.Key))
                continue;
            if (pair.Key.StartsWith("Spaceship_", StringComparison.OrdinalIgnoreCase))
                continue;
            if (pair.Key.StartsWith("BattleStation_", StringComparison.OrdinalIgnoreCase))
                continue;

            AoObject obj = pair.Value;
            if (!TryGetString(obj, "Mesh", out string meshName) || string.IsNullOrWhiteSpace(meshName))
                continue;

            if (!IsCameraLocked(obj))
                continue;

            // Include disabled AO objects so LE overlays can re-enable them.
            bool enabled = IsEnabled(obj);

            Vector3 pos = TryGetVector(obj, "Position", out Vector3 p) ? p : Vector3.zero;
            Quaternion rot = TryGetQuaternion(obj, "Rotation", out Quaternion r) ? r : Quaternion.identity;

            var placement = new AoSkyMeshPlacement
            {
                ObjectName = pair.Key,
                MeshName = meshName.Trim(),
                PositionOffset = pos,
                LocalRotation = FlipYz(rot),
                Scale = TryGetFloat(obj, "Scale", out float scale) ? Mathf.Max(0.01f, scale) : 1f,
                Intensity = vars.ResolveSkyIntensity(obj),
                Enabled = enabled
            };

            // Prefer non-_Close variants when both exist; still allow unique _Close-only
            if (pair.Key.EndsWith("_Close", StringComparison.OrdinalIgnoreCase))
                continue;

            tweak.SkyMeshes.Add(placement);
        }

        return tweak;
    }

    /// <summary>
    /// Remap a quaternion written in AO tweak space (Y/Z swapped) into Unity.
    /// </summary>
    static Quaternion FlipYz(Quaternion q) => new Quaternion(q.x, q.z, q.y, q.w);

    static bool IsCameraLocked(AoObject obj)
    {
        if (!obj.Properties.TryGetValue("PositionType", out AoProperty prop) || prop.Raw == null)
            return false;
        return prop.Raw.IndexOf("LockToCamera", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static bool IsEnabled(AoObject obj)
    {
        if (!obj.Properties.TryGetValue("Use", out AoProperty use) || use.Raw == null)
            return true;
        return use.Raw.IndexOf("e_No", StringComparison.OrdinalIgnoreCase) < 0;
    }

    static Color SampleRgb(AoObject obj, string rName, string gName, string bName, float t)
    {
        float r = 0f, g = 0f, b = 0f;
        if (obj.Properties.TryGetValue(rName, out AoProperty rp) && rp.FloatArray != null)
            r = SampleArray(rp.FloatArray, t);
        if (obj.Properties.TryGetValue(gName, out AoProperty gp) && gp.FloatArray != null)
            g = SampleArray(gp.FloatArray, t);
        if (obj.Properties.TryGetValue(bName, out AoProperty bp) && bp.FloatArray != null)
            b = SampleArray(bp.FloatArray, t);
        return new Color(Mathf.Clamp01(r), Mathf.Clamp01(g), Mathf.Clamp01(b), 1f);
    }

    static bool TryGetFloat(AoObject obj, string name, out float value)
    {
        value = 0f;
        if (!obj.Properties.TryGetValue(name, out AoProperty prop))
            return false;
        if (prop.FloatValue.HasValue)
        {
            value = prop.FloatValue.Value;
            return true;
        }

        // Evaluate simple CurrentDayTime / 6480 style when both operands are known literals on same object
        if (!string.IsNullOrEmpty(prop.Raw) && prop.Raw.IndexOf('/') >= 0 && prop.Raw.IndexOf("This.", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            Match m = Regex.Match(prop.Raw, @"This\.(\w+)\s*/\s*([0-9.]+)", RegexOptions.IgnoreCase);
            if (m.Success
                && TryGetFloat(obj, m.Groups[1].Value, out float lhs)
                && float.TryParse(m.Groups[2].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float rhs)
                && Mathf.Abs(rhs) > 1e-12f)
            {
                value = lhs / rhs;
                return true;
            }
        }

        return TryEvalSimpleFloat(prop.Raw, out value);
    }

    static bool TryGetString(AoObject obj, string name, out string value)
    {
        value = null;
        if (!obj.Properties.TryGetValue(name, out AoProperty prop))
            return false;
        value = prop.StringValue;
        return !string.IsNullOrEmpty(value);
    }

    static bool TryGetVector(AoObject obj, string name, out Vector3 value)
    {
        value = default;
        if (!obj.Properties.TryGetValue(name, out AoProperty prop) || !prop.VectorValue.HasValue)
            return false;
        value = prop.VectorValue.Value;
        return true;
    }

    static bool TryGetQuaternion(AoObject obj, string name, out Quaternion value)
    {
        value = Quaternion.identity;
        if (!obj.Properties.TryGetValue(name, out AoProperty prop) || !prop.QuaternionValue.HasValue)
            return false;
        value = prop.QuaternionValue.Value;
        return true;
    }
}
