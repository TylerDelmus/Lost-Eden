using System;

public enum AttractorPlace
{
    Head = 0,
    RightHand = 1,
    LeftHand = 2,
    RightShoulder = 3,
    LeftShoulder = 4,
    Back = 5,
    Hip = 6,
    RightThigh = 7,
    LeftThigh = 8,
    RightCrus = 9,
    LeftCrus = 10,
    RightArm = 11,
    LeftArm = 12,
    RightForearm = 13,
    LeftForearm = 14,
}

public static class AttractorPlaceUtil
{
    /// <summary>
    /// Maps CatMesh names like "Attractor01_head" / "Attractor15_leftforearm" to <see cref="AttractorPlace"/>.
    /// Prefers the NN index in the name (AttractorNN → place NN-1), then known suffixes.
    /// </summary>
    public static bool TryParse(string catMeshName, out AttractorPlace place)
    {
        place = default;
        if (string.IsNullOrWhiteSpace(catMeshName))
            return false;

        string name = catMeshName.Trim();

        // Attractor01_head → 1 → Head(0)
        if (name.StartsWith("Attractor", StringComparison.OrdinalIgnoreCase)
            && name.Length > "Attractor".Length
            && char.IsDigit(name["Attractor".Length]))
        {
            int i = "Attractor".Length;
            int number = 0;
            while (i < name.Length && char.IsDigit(name[i]))
            {
                number = number * 10 + (name[i] - '0');
                i++;
            }

            if (number >= 1 && number <= 15)
            {
                place = (AttractorPlace)(number - 1);
                return Enum.IsDefined(typeof(AttractorPlace), place);
            }
        }

        int underscore = name.LastIndexOf('_');
        string suffix = underscore >= 0 && underscore < name.Length - 1
            ? name[(underscore + 1)..]
            : name;

        return TryParseSuffix(suffix, out place);
    }

    static bool TryParseSuffix(string suffix, out AttractorPlace place)
    {
        place = default;
        if (string.IsNullOrEmpty(suffix))
            return false;

        switch (suffix.Trim().ToLowerInvariant())
        {
            case "head": place = AttractorPlace.Head; return true;
            case "righthand": place = AttractorPlace.RightHand; return true;
            case "lefthand": place = AttractorPlace.LeftHand; return true;
            case "rightshoulder": place = AttractorPlace.RightShoulder; return true;
            case "leftshoulder": place = AttractorPlace.LeftShoulder; return true;
            case "back": place = AttractorPlace.Back; return true;
            case "hip":
            case "body": place = AttractorPlace.Hip; return true;
            case "rightthigh": place = AttractorPlace.RightThigh; return true;
            case "leftthigh": place = AttractorPlace.LeftThigh; return true;
            case "rightcrus": place = AttractorPlace.RightCrus; return true;
            case "leftcrus": place = AttractorPlace.LeftCrus; return true;
            case "rightarm": place = AttractorPlace.RightArm; return true;
            case "leftarm": place = AttractorPlace.LeftArm; return true;
            case "rightforearm": place = AttractorPlace.RightForearm; return true;
            case "leftforearm": place = AttractorPlace.LeftForearm; return true;
            default: return false;
        }
    }
}
