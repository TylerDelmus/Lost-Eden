using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Shallow AO tweak Object extractor. Skips StateBlob / Expansion bodies.
/// Later objects with the same name replace earlier ones.
/// </summary>
public static class AoTweakObjectParser
{
    static readonly Regex ObjectStart = new Regex(
        @"^\s*Object\s+(\S+)\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.CultureInvariant);

    // Use greedy \S+ for the property name. Non-greedy \S+? only captured the first
    // letter when no [N] array suffix was present (Mesh → "M"), so sky meshes never matched.
    static readonly Regex PropLine = new Regex(
        @"^\s*(Unsigned|Float|Vector|Quaternion|String|Matrix|Sound)\s+(\S+)(?:\s*\[(\d+)\])?\s*:?\s*(.*?)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public sealed class AoObject
    {
        public string Name;
        public readonly Dictionary<string, AoProperty> Properties =
            new Dictionary<string, AoProperty>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class AoProperty
    {
        public string Type;
        public string Raw;
        public int ArrayLength;
        public float[] FloatArray;
        public string StringValue;
        public Vector3? VectorValue;
        public Quaternion? QuaternionValue;
        public float? FloatValue;
    }

    public static Dictionary<string, AoObject> Parse(string flattenedSource)
    {
        var result = new Dictionary<string, AoObject>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(flattenedSource))
            return result;

        MatchCollection starts = ObjectStart.Matches(flattenedSource);
        for (int i = 0; i < starts.Count; i++)
        {
            Match start = starts[i];
            string name = start.Groups[1].Value;
            int braceOpen = flattenedSource.IndexOf('{', start.Index + start.Length);
            if (braceOpen < 0)
                continue;

            int braceClose = FindMatchingBrace(flattenedSource, braceOpen);
            if (braceClose < 0)
                continue;

            string body = flattenedSource.Substring(braceOpen + 1, braceClose - braceOpen - 1);
            var obj = new AoObject { Name = name };
            ParseObjectBody(body, obj);
            result[name] = obj;
        }

        return result;
    }

    static void ParseObjectBody(string body, AoObject obj)
    {
        int i = 0;
        while (i < body.Length)
        {
            SkipWhitespaceAndComments(body, ref i);
            if (i >= body.Length)
                break;

            if (StartsWithToken(body, i, "StateBlob") || StartsWithToken(body, i, "Expansion"))
            {
                SkipNestedBlock(body, ref i);
                continue;
            }

            int lineEnd = body.IndexOf('\n', i);
            if (lineEnd < 0)
                lineEnd = body.Length;

            string line = StripInlineComment(body.Substring(i, lineEnd - i)).Trim();
            i = lineEnd + 1;

            if (line.Length == 0)
                continue;

            Match m = PropLine.Match(line);
            if (!m.Success)
                continue;

            string type = m.Groups[1].Value;
            string propName = m.Groups[2].Value.TrimEnd(':');
            int.TryParse(m.Groups[3].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int arrayLen);
            string raw = m.Groups[4].Value.Trim();

            // Accumulate continued float-array lines that follow without a type prefix.
            if (arrayLen > 0 && (type.Equals("Float", StringComparison.OrdinalIgnoreCase)
                                 || type.Equals("Unsigned", StringComparison.OrdinalIgnoreCase)
                                 || type.Equals("Vector", StringComparison.OrdinalIgnoreCase)))
            {
                raw = AccumulateContinuation(body, ref i, raw);
            }

            var prop = new AoProperty
            {
                Type = type,
                Raw = raw,
                ArrayLength = arrayLen
            };
            TryParseProperty(prop);
            obj.Properties[propName] = prop;
        }
    }

    static string AccumulateContinuation(string body, ref int i, string raw)
    {
        var parts = new List<string> { raw };
        while (i < body.Length)
        {
            int save = i;
            SkipWhitespaceAndComments(body, ref i);
            if (i >= body.Length)
                break;

            if (StartsWithToken(body, i, "StateBlob") || StartsWithToken(body, i, "Expansion"))
            {
                i = save;
                break;
            }

            int lineEnd = body.IndexOf('\n', i);
            if (lineEnd < 0)
                lineEnd = body.Length;

            string line = StripInlineComment(body.Substring(i, lineEnd - i)).Trim();
            if (line.Length == 0)
            {
                i = lineEnd + 1;
                continue;
            }

            if (PropLine.IsMatch(line) || StartsWithToken(line, 0, "StateBlob") || StartsWithToken(line, 0, "Expansion"))
            {
                i = save;
                break;
            }

            // Continuation of numeric list
            if (line.IndexOf('/') >= 0 || char.IsDigit(line[0]) || line[0] == '-' || line[0] == '.')
            {
                parts.Add(line.TrimEnd(','));
                i = lineEnd + 1;
                continue;
            }

            i = save;
            break;
        }

        return string.Join(", ", parts);
    }

    static void TryParseProperty(AoProperty prop)
    {
        if (prop.Type.Equals("String", StringComparison.OrdinalIgnoreCase))
        {
            Match sm = Regex.Match(prop.Raw, "\"([^\"]*)\"");
            if (sm.Success)
                prop.StringValue = sm.Groups[1].Value;
            return;
        }

        if (prop.Type.Equals("Vector", StringComparison.OrdinalIgnoreCase))
        {
            if (TryParseVector(prop.Raw, out Vector3 v))
                prop.VectorValue = v;
            return;
        }

        if (prop.Type.Equals("Quaternion", StringComparison.OrdinalIgnoreCase))
        {
            if (TryParseQuaternion(prop.Raw, out Quaternion q))
                prop.QuaternionValue = q;
            return;
        }

        if (prop.ArrayLength > 0)
        {
            prop.FloatArray = ParseFloatList(prop.Raw);
            return;
        }

        if (TryEvalSimpleFloat(prop.Raw, out float f))
            prop.FloatValue = f;
    }

    public static bool TryParseVector(string raw, out Vector3 value)
    {
        value = default;
        Match m = Regex.Match(raw, @"v\s*\(\s*([^)]+)\)", RegexOptions.IgnoreCase);
        if (!m.Success)
            return false;

        float[] nums = ParseFloatList(m.Groups[1].Value);
        if (nums == null || nums.Length < 3)
            return false;

        value = new Vector3(nums[0], nums[1], nums[2]);
        return true;
    }

    public static bool TryParseQuaternion(string raw, out Quaternion value)
    {
        value = Quaternion.identity;

        Match q = Regex.Match(raw, @"q\s*\(\s*([^)]+)\)", RegexOptions.IgnoreCase);
        if (q.Success)
        {
            float[] nums = ParseFloatList(q.Groups[1].Value);
            if (nums == null || nums.Length < 4)
                return false;
            value = new Quaternion(nums[0], nums[1], nums[2], nums[3]);
            return true;
        }

        // Axis-angle: v(x,y,z), degrees
        Match aa = Regex.Match(
            raw,
            @"v\s*\(\s*([^)]+)\)\s*,\s*([-+0-9.eE]+(?:\s*/\s*[-+0-9.eE]+)?)",
            RegexOptions.IgnoreCase);
        if (!aa.Success)
            return false;

        float[] axis = ParseFloatList(aa.Groups[1].Value);
        if (axis == null || axis.Length < 3)
            return false;

        if (!TryEvalSimpleFloat(aa.Groups[2].Value.Trim(), out float degrees))
            return false;

        Vector3 a = new Vector3(axis[0], axis[1], axis[2]);
        if (a.sqrMagnitude < 1e-8f)
            return false;

        value = Quaternion.AngleAxis(degrees, a.normalized);
        return true;
    }

    public static float[] ParseFloatList(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Array.Empty<float>();

        string cleaned = Regex.Replace(raw, @"\bf\b", "", RegexOptions.IgnoreCase);
        string[] tokens = cleaned.Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        var list = new List<float>(tokens.Length);
        for (int i = 0; i < tokens.Length; i++)
        {
            string token = tokens[i].Trim();
            if (token.Length == 0)
                continue;
            if (TryEvalSimpleFloat(token, out float v))
                list.Add(v);
        }

        return list.ToArray();
    }

    public static bool TryEvalSimpleFloat(string expr, out float value)
    {
        value = 0f;
        if (string.IsNullOrWhiteSpace(expr))
            return false;

        expr = expr.Trim().TrimEnd('u', 'U', 'f', 'F');
        // Reject identifiers / cross-refs
        if (Regex.IsMatch(expr, @"[A-Za-z_]"))
            return false;

        if (expr.Contains("/"))
        {
            string[] parts = expr.Split('/');
            if (parts.Length == 2
                && float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float a)
                && float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float b)
                && Mathf.Abs(b) > 1e-12f)
            {
                value = a / b;
                return true;
            }

            return false;
        }

        return float.TryParse(expr, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    public static float SampleArray(float[] values, float t01)
    {
        if (values == null || values.Length == 0)
            return 0f;
        if (values.Length == 1)
            return values[0];

        float t = Mathf.Clamp01(t01) * (values.Length - 1);
        int i0 = Mathf.FloorToInt(t);
        int i1 = Mathf.Min(i0 + 1, values.Length - 1);
        return Mathf.Lerp(values[i0], values[i1], t - i0);
    }

    static int FindMatchingBrace(string text, int openIndex)
    {
        int depth = 0;
        for (int i = openIndex; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '{')
                depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                    return i;
            }
        }

        return -1;
    }

    static void SkipNestedBlock(string body, ref int i)
    {
        int brace = body.IndexOf('{', i);
        if (brace < 0)
        {
            i = body.Length;
            return;
        }

        int close = FindMatchingBrace(body, brace);
        i = close < 0 ? body.Length : close + 1;
    }

    static void SkipWhitespaceAndComments(string body, ref int i)
    {
        while (i < body.Length)
        {
            char c = body[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c == '#' || (c == '/' && i + 1 < body.Length && body[i + 1] == '/'))
            {
                while (i < body.Length && body[i] != '\n')
                    i++;
                continue;
            }

            break;
        }
    }

    static string StripInlineComment(string line)
    {
        int hash = line.IndexOf('#');
        if (hash >= 0)
            line = line.Substring(0, hash);
        return line;
    }

    static bool StartsWithToken(string text, int index, string token)
    {
        if (index + token.Length > text.Length)
            return false;
        if (string.Compare(text, index, token, 0, token.Length, StringComparison.OrdinalIgnoreCase) != 0)
            return false;
        int after = index + token.Length;
        return after >= text.Length || !char.IsLetterOrDigit(text[after]);
    }
}
