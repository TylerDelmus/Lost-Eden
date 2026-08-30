using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;
using static AoTweakObjectParser;

/// <summary>
/// Resolves AO tweak float expressions across the flattened object graph
/// (GAME.ThickCloudsIntensity, This.TFACTOR, array subscripts, + - * /).
/// Not a full tweak VM — enough for environment/sky evaluation.
/// </summary>
public sealed class AoTweakVariableContext
{
    static readonly Regex RefPattern = new Regex(
        @"^(GAME|This)\.([A-Za-z_][A-Za-z0-9_]*)(?:\[(.+)\])?\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    readonly Dictionary<string, AoObject> _objects;
    readonly AoObject _game;
    readonly Dictionary<string, float> _resolved = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _inProgress = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public AoTweakVariableContext(Dictionary<string, AoObject> objects)
    {
        _objects = objects ?? new Dictionary<string, AoObject>(StringComparer.OrdinalIgnoreCase);
        _objects.TryGetValue("GAME", out _game);
    }

    /// <summary>Resolve every float property on GAME into <paramref name="target"/>.</summary>
    public void ExportGameVariables(Dictionary<string, float> target)
    {
        if (target == null || _game == null)
            return;

        foreach (KeyValuePair<string, AoProperty> pair in _game.Properties)
        {
            if (TryResolveProperty(_game, _game, pair.Key, out float value))
                target[pair.Key] = value;
        }
    }

    public bool TryGetGameFloat(string name, out float value)
    {
        if (_game == null)
        {
            value = 0f;
            return false;
        }

        return TryResolveProperty(_game, _game, name, out value);
    }

    public bool TryResolveProperty(AoObject owner, AoObject self, string propName, out float value)
    {
        value = 0f;
        if (owner == null || string.IsNullOrWhiteSpace(propName))
            return false;

        string key = owner.Name + "." + propName;
        if (_resolved.TryGetValue(key, out value))
            return true;

        if (!_inProgress.Add(key))
            return false;

        bool ok = false;
        try
        {
            if (!owner.Properties.TryGetValue(propName, out AoProperty prop))
                return false;

            if (prop.FloatValue.HasValue)
            {
                value = prop.FloatValue.Value;
                ok = true;
            }
            else if (prop.FloatArray != null && prop.FloatArray.Length > 0
                     && (string.IsNullOrWhiteSpace(prop.Raw)
                         || (!prop.Raw.Contains("This.", StringComparison.OrdinalIgnoreCase)
                             && !prop.Raw.Contains("GAME.", StringComparison.OrdinalIgnoreCase)
                             && prop.Raw.IndexOf('[') < 0)))
            {
                value = prop.FloatArray[0];
                ok = true;
            }
            else if (!string.IsNullOrWhiteSpace(prop.Raw))
            {
                ok = TryEvaluateExpression(self ?? owner, prop.Raw, out value);
            }
        }
        finally
        {
            _inProgress.Remove(key);
        }

        if (ok)
            _resolved[key] = value;
        return ok;
    }

    public bool TryEvaluateExpression(AoObject self, string expr, out float value)
    {
        value = 0f;
        if (string.IsNullOrWhiteSpace(expr))
            return false;

        expr = StripAoSuffixes(expr.Trim());
        if (expr.Length == 0)
            return false;

        return TryEvalAdd(self, expr, out value);
    }

    /// <summary>
    /// Sky opacity from TFACTOR / Intensity tweak properties.
    /// </summary>
    public float ResolveSkyIntensity(AoObject obj)
    {
        if (obj == null)
            return 1f;

        if (TryResolveProperty(obj, obj, "TFACTOR", out float tf))
        {
            float fromTf = IntensityFromTextureFactor(tf);
            if (fromTf >= 0f)
                return fromTf;
        }

        if (TryResolveProperty(obj, obj, "Intensity", out float intensity))
            return Mathf.Clamp01(intensity);

        return 1f;
    }

    public static float IntensityFromTextureFactor(float tf)
    {
        if (float.IsNaN(tf) || float.IsInfinity(tf))
            return -1f;

        if (tf >= 0f && tf <= 1f)
            return tf;

        uint bits = (uint)tf;
        float alpha = ((bits >> 24) & 0xFF) / 255f;
        if (alpha > 0f)
            return Mathf.Clamp01(alpha);

        if (tf > 1f)
            return 1f;

        return -1f;
    }

    bool TryEvalAdd(AoObject self, string expr, out float value)
    {
        value = 0f;
        if (SplitAtDepthZero(expr, '+', out string lhs, out string rhs))
            return TryEvalAdd(self, lhs, out float a) && TryEvalAdd(self, rhs, out float b) && Set(out value, a + b);

        if (SplitAtDepthZero(expr, '-', out lhs, out rhs))
            return TryEvalAdd(self, lhs, out float a) && TryEvalMul(self, rhs, out float b) && Set(out value, a - b);

        return TryEvalMul(self, expr, out value);
    }

    bool TryEvalMul(AoObject self, string expr, out float value)
    {
        value = 0f;
        if (SplitAtDepthZero(expr, '*', out string lhs, out string rhs))
            return TryEvalMul(self, lhs, out float a) && TryEvalMul(self, rhs, out float b) && Set(out value, a * b);

        if (SplitAtDepthZero(expr, '/', out lhs, out rhs))
            return TryEvalMul(self, lhs, out float a) && TryEvalUnary(self, rhs, out float b)
                   && Mathf.Abs(b) > 1e-12f && Set(out value, a / b);

        return TryEvalUnary(self, expr, out value);
    }

    bool TryEvalUnary(AoObject self, string expr, out float value)
    {
        value = 0f;
        expr = expr.Trim();
        if (expr.StartsWith("+", StringComparison.Ordinal))
            return TryEvalUnary(self, expr.Substring(1), out value);

        if (expr.StartsWith("-", StringComparison.Ordinal))
            return TryEvalUnary(self, expr.Substring(1), out value) && Set(out value, -value);

        return TryEvalPrimary(self, expr, out value);
    }

    bool TryEvalPrimary(AoObject self, string expr, out float value)
    {
        value = 0f;
        expr = expr.Trim();
        if (expr.Length == 0)
            return false;

        if (expr.StartsWith("(", StringComparison.Ordinal) && expr.EndsWith(")", StringComparison.Ordinal))
            return TryEvaluateExpression(self, expr.Substring(1, expr.Length - 2), out value);

        Match refMatch = RefPattern.Match(expr);
        if (refMatch.Success)
        {
            AoObject owner = refMatch.Groups[1].Value.Equals("GAME", StringComparison.OrdinalIgnoreCase)
                ? _game
                : self;
            if (owner == null)
                return false;

            string propName = refMatch.Groups[2].Value;
            if (refMatch.Groups[3].Success)
            {
                if (!TryEvaluateExpression(self, refMatch.Groups[3].Value.Trim(), out float index))
                    return false;

                if (!owner.Properties.TryGetValue(propName, out AoProperty prop) || prop.FloatArray == null
                    || prop.FloatArray.Length == 0)
                    return false;

                value = SampleArray(prop.FloatArray, index);
                return true;
            }

            return TryResolveProperty(owner, self, propName, out value);
        }

        if (expr.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && uint.TryParse(expr.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint hex))
        {
            value = hex;
            return true;
        }

        if (float.TryParse(expr, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return true;

        return false;
    }

    static bool Set(out float dst, float value)
    {
        dst = value;
        return true;
    }

    static string StripAoSuffixes(string expr)
    {
        // Drop AO-only operators we do not evaluate yet (~, [ROT], etc.).
        int tilde = expr.IndexOf('~');
        if (tilde >= 0)
            expr = expr.Substring(0, tilde).Trim();

        return expr.TrimEnd('u', 'U', 'f', 'F');
    }

    static bool SplitAtDepthZero(string expr, char op, out string lhs, out string rhs)
    {
        lhs = null;
        rhs = null;
        int depth = 0;
        for (int i = expr.Length - 1; i >= 0; i--)
        {
            char c = expr[i];
            if (c == ')')
                depth++;
            else if (c == '(')
                depth--;
            else if (depth == 0 && c == op)
            {
                if (op == '-' && i == 0)
                    continue;

                lhs = expr.Substring(0, i).Trim();
                rhs = expr.Substring(i + 1).Trim();
                return lhs.Length > 0 && rhs.Length > 0;
            }
        }

        return false;
    }
}
