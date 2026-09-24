using System.Globalization;
using UnityEngine.UIElements;

/// <summary>
/// Parsers for stock's inline geometry literals as they appear in the shipped GUI XML:
/// <c>Rect(l,t,r,b)</c> and <c>Point(x,y)</c>. Kept separate from <see cref="AoView"/> so the
/// list and slot views can use them too.
/// </summary>
public static class AoGeometry
{
    /// <summary>
    /// Stock writes a negative component to mean "leave this axis free" — <c>Point(70,-1)</c>
    /// is 70 wide with the height unconstrained. Writing -1 through to a style would be a
    /// constraint, so it has to become an unset instead.
    /// </summary>
    public static StyleLength Length(float value)
    {
        return value < 0f ? new StyleLength(StyleKeyword.Null) : new StyleLength(value);
    }

    public static bool IsUnconstrained(float value) => value < 0f;

    public static bool TryParseRect(string value, out float l, out float t, out float r, out float b)
    {
        l = t = r = b = 0f;
        return TryParseTuple(value, "Rect", 4, out float[] v)
               && Assign(v, out l, out t, out r, out b);
    }

    public static bool TryParsePoint(string value, out float x, out float y)
    {
        x = y = 0f;
        if (!TryParseTuple(value, "Point", 2, out float[] v))
            return false;

        x = v[0];
        y = v[1];
        return true;
    }

    static bool Assign(float[] v, out float l, out float t, out float r, out float b)
    {
        l = v[0];
        t = v[1];
        r = v[2];
        b = v[3];
        return true;
    }

    /// <summary>Accepts "Name(a,b,..)" and a bare comma list; whitespace is ignored.</summary>
    static bool TryParseTuple(string value, string name, int count, out float[] parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string s = value.Trim();
        if (s.StartsWith(name, System.StringComparison.OrdinalIgnoreCase))
        {
            int open = s.IndexOf('(');
            int close = s.LastIndexOf(')');
            if (open < 0 || close <= open)
                return false;

            s = s.Substring(open + 1, close - open - 1);
        }

        string[] parts = s.Split(',');
        if (parts.Length != count)
            return false;

        var result = new float[count];
        for (int i = 0; i < count; i++)
        {
            if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out result[i]))
                return false;
        }

        parsed = result;
        return true;
    }
}
