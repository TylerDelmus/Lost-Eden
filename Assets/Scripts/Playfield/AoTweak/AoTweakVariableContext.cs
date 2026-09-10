using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;
using static AoTweakObjectParser;

/// <summary>
/// Resolves AO tweak float/quaternion expressions across the flattened object graph
/// (GAME.ThickCloudsIntensity, This.TFACTOR, Object.Prop, [ROT], array subscripts, + - * / %).
/// Not a full tweak VM — enough for environment/sky evaluation at a frozen snapshot.
/// </summary>
public sealed class AoTweakVariableContext
{
    static readonly Regex RefPattern = new Regex(
        @"^([A-Za-z_][A-Za-z0-9_]*)\.([A-Za-z_][A-Za-z0-9_]*)(?:\[(.+)\])?\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    static readonly Regex AxisAnglePattern = new Regex(
        @"^v\s*\(\s*([^)]+)\)\s*,\s*(.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static readonly Regex QuatLiteralPattern = new Regex(
        @"^q\s*\(\s*([^)]+)\)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    readonly Dictionary<string, AoObject> _objects;
    readonly AoObject _game;
    readonly Dictionary<string, float> _resolved = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _inProgress = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, Quaternion> _resolvedQuats =
        new Dictionary<string, Quaternion>(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _inProgressQuats = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
    /// Resolve a quaternion property (literals, axis-angle, This/GAME/Object refs, [ROT] composition).
    /// Animated counters that cannot freeze evaluate as 0° so static tilts still apply.
    /// </summary>
    public bool TryResolveQuaternion(AoObject owner, AoObject self, string propName, out Quaternion value)
    {
        value = Quaternion.identity;
        if (owner == null || string.IsNullOrWhiteSpace(propName))
            return false;

        string key = owner.Name + "." + propName;
        if (_resolvedQuats.TryGetValue(key, out value))
            return true;

        if (!_inProgressQuats.Add(key))
            return false;

        bool ok = false;
        try
        {
            if (!owner.Properties.TryGetValue(propName, out AoProperty prop))
                return false;

            // Prefer expression eval when Raw needs the graph (refs / [ROT] / non-literal angles).
            if (!string.IsNullOrWhiteSpace(prop.Raw)
                && NeedsQuaternionExpression(prop.Raw))
            {
                ok = TryEvaluateQuaternion(self ?? owner, prop.Raw, out value);
            }
            else if (prop.QuaternionValue.HasValue)
            {
                value = prop.QuaternionValue.Value;
                ok = true;
            }
            else if (!string.IsNullOrWhiteSpace(prop.Raw))
            {
                ok = TryEvaluateQuaternion(self ?? owner, prop.Raw, out value);
            }
        }
        finally
        {
            _inProgressQuats.Remove(key);
        }

        if (ok)
            _resolvedQuats[key] = value;
        return ok;
    }

    public bool TryEvaluateQuaternion(AoObject self, string expr, out Quaternion value)
    {
        value = Quaternion.identity;
        if (string.IsNullOrWhiteSpace(expr))
            return false;

        expr = StripAoSuffixes(expr.Trim());
        if (expr.Length == 0)
            return false;

        // A [ROT] B [ROT] C — left-associative; A [ROT] B ≡ B * A (apply A then B).
        if (SplitAtRot(expr, out string lhs, out string rhs))
        {
            if (!TryEvaluateQuaternion(self, lhs, out Quaternion a)
                || !TryEvaluateQuaternion(self, rhs, out Quaternion b))
                return false;

            value = b * a;
            return true;
        }

        Match qLit = QuatLiteralPattern.Match(expr);
        if (qLit.Success)
        {
            float[] nums = ParseFloatList(qLit.Groups[1].Value);
            if (nums == null || nums.Length < 4)
                return false;
            value = new Quaternion(nums[0], nums[1], nums[2], nums[3]);
            return true;
        }

        Match aa = AxisAnglePattern.Match(expr);
        if (aa.Success)
        {
            float[] axis = ParseFloatList(aa.Groups[1].Value);
            if (axis == null || axis.Length < 3)
                return false;

            Vector3 a = new Vector3(axis[0], axis[1], axis[2]);
            if (a.sqrMagnitude < 1e-8f)
                return false;

            // Frozen snapshot: unresolved/cyclic angles (e.g. Counter accumulators) → 0°.
            float degrees = 0f;
            TryEvaluateExpression(self, aa.Groups[2].Value.Trim(), out degrees);

            value = Quaternion.AngleAxis(degrees, a.normalized);
            return true;
        }

        Match refMatch = RefPattern.Match(expr);
        if (refMatch.Success)
        {
            if (!TryResolveRefOwner(self, refMatch.Groups[1].Value, out AoObject owner))
                return false;

            // Quaternion props do not use array subscripts.
            if (refMatch.Groups[3].Success)
                return false;

            return TryResolveQuaternion(owner, self, refMatch.Groups[2].Value, out value);
        }

        return false;
    }

    static bool NeedsQuaternionExpression(string raw)
    {
        if (raw.IndexOf("[ROT]", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        if (raw.IndexOf("<|", StringComparison.Ordinal) >= 0)
            return true;
        if (raw.IndexOf('.') >= 0)
            return true;

        Match aa = AxisAnglePattern.Match(raw.Trim());
        if (aa.Success)
            return !TryEvalSimpleFloat(aa.Groups[2].Value.Trim(), out _);

        return false;
    }

    bool TryResolveRefOwner(AoObject self, string ownerName, out AoObject owner)
    {
        owner = null;
        if (ownerName.Equals("GAME", StringComparison.OrdinalIgnoreCase))
        {
            owner = _game;
            return owner != null;
        }

        if (ownerName.Equals("This", StringComparison.OrdinalIgnoreCase))
        {
            owner = self;
            return owner != null;
        }

        return _objects.TryGetValue(ownerName, out owner) && owner != null;
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
        // Same precedence for * / %; rightmost split → left-associative (a * b % c == (a*b)%c).
        if (SplitAtDepthZeroAny(expr, out string lhs, out string rhs, out char op, '*', '/', '%'))
        {
            if (!TryEvalMul(self, lhs, out float a) || !TryEvalUnary(self, rhs, out float b))
                return false;

            if (op == '*')
                return Set(out value, a * b);
            if (op == '/')
                return Mathf.Abs(b) > 1e-12f && Set(out value, a / b);
            // %
            return Mathf.Abs(b) > 1e-12f && Set(out value, a % b);
        }

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
            if (!TryResolveRefOwner(self, refMatch.Groups[1].Value, out AoObject owner))
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
        // Side-effect / bitwise suffixes we ignore for frozen eval.
        int tilde = expr.IndexOf('~');
        if (tilde >= 0)
            expr = expr.Substring(0, tilde).Trim();

        int trigger = expr.IndexOf("<|", StringComparison.Ordinal);
        if (trigger >= 0)
            expr = expr.Substring(0, trigger).Trim();

        return expr.TrimEnd('u', 'U', 'f', 'F');
    }

    static bool SplitAtRot(string expr, out string lhs, out string rhs)
    {
        lhs = null;
        rhs = null;
        const string op = "[ROT]";
        int depth = 0;
        for (int i = expr.Length - op.Length; i >= 0; i--)
        {
            char c = expr[i];
            if (c == ')')
                depth++;
            else if (c == '(')
                depth--;
            else if (depth == 0
                     && string.Compare(expr, i, op, 0, op.Length, StringComparison.OrdinalIgnoreCase) == 0)
            {
                lhs = expr.Substring(0, i).Trim();
                rhs = expr.Substring(i + op.Length).Trim();
                return lhs.Length > 0 && rhs.Length > 0;
            }
        }

        return false;
    }

    static bool SplitAtDepthZero(string expr, char op, out string lhs, out string rhs)
    {
        return SplitAtDepthZeroAny(expr, out lhs, out rhs, out _, op);
    }

    static bool SplitAtDepthZeroAny(string expr, out string lhs, out string rhs, out char foundOp, params char[] ops)
    {
        lhs = null;
        rhs = null;
        foundOp = '\0';
        int depth = 0;
        for (int i = expr.Length - 1; i >= 0; i--)
        {
            char c = expr[i];
            if (c == ')')
                depth++;
            else if (c == '(')
                depth--;
            else if (depth == 0)
            {
                for (int o = 0; o < ops.Length; o++)
                {
                    if (c != ops[o])
                        continue;
                    if (ops[o] == '-' && i == 0)
                        continue;

                    lhs = expr.Substring(0, i).Trim();
                    rhs = expr.Substring(i + 1).Trim();
                    foundOp = ops[o];
                    return lhs.Length > 0 && rhs.Length > 0;
                }
            }
        }

        return false;
    }
}
