using System;
using System.Collections.Generic;
using AODB.Common.Enums;
using AODB.Common.RDBObjects;
using AOSharp.Common.GameData;
using UnityEngine;

/// <summary>
/// Resolves naked SkinTexture resource IDs from breed/gender/race + body placement.
/// </summary>
public sealed class SkinTextureResolver
{
    readonly ResourceDatabase _database;
    Dictionary<string, int> _skinNameToId;

    public SkinTextureResolver(ResourceDatabase database)
    {
        _database = database;
    }

    public bool SupportsBreed(Breed breed) =>
        breed is Breed.Solitus or Breed.Opifex or Breed.Nanomage or Breed.Atrox;

    public bool TryResolveNakedId(
        BodyPart part,
        Breed breed,
        Gender gender,
        int race,
        out int skinTextureId)
    {
        skinTextureId = 0;
        if (!SupportsBreed(breed))
            return false;

        if (!TryBuildNakedName(part, breed, gender, race, out string name))
            return false;

        EnsureNameCache();
        return _skinNameToId != null
            && _skinNameToId.TryGetValue(name, out skinTextureId)
            && skinTextureId > 0;
    }

    public static string BodyPartName(BodyPart part) => part switch
    {
        BodyPart.Hands => "hands",
        BodyPart.Body => "body",
        BodyPart.Feet => "feet",
        BodyPart.Arms => "arms",
        BodyPart.Legs => "legs",
        _ => null
    };

    public static bool TryParseBodyPartName(string name, out BodyPart part)
    {
        part = default;
        if (string.IsNullOrEmpty(name))
            return false;

        switch (name.Trim().ToLowerInvariant())
        {
            case "hands": part = BodyPart.Hands; return true;
            case "body": part = BodyPart.Body; return true;
            case "feet": part = BodyPart.Feet; return true;
            case "arms": part = BodyPart.Arms; return true;
            case "legs": part = BodyPart.Legs; return true;
            default: return false;
        }
    }

    public static bool TryBuildNakedName(
        BodyPart part,
        Breed breed,
        Gender gender,
        int race,
        out string name)
    {
        name = null;
        string placement = BodyPartName(part);
        if (placement == null)
            return false;

        if (breed == Breed.Atrox)
        {
            name = $"{placement}_athroxmale_naked.png";
            return true;
        }

        string breedName = breed switch
        {
            Breed.Solitus => "solitus",
            Breed.Opifex => "opifex",
            Breed.Nanomage => "nanomage",
            _ => null
        };
        if (breedName == null)
            return false;

        string genderName = gender switch
        {
            Gender.Male => "male",
            Gender.Female => "female",
            Gender.Uni => "male",
            _ => null
        };
        if (genderName == null)
            return false;

        if (breed == Breed.Solitus)
        {
            if (!TryRaceToken(race, out string raceToken))
                return false;

            name = $"{placement}_{breedName}{genderName}_{raceToken}_naked.png";
            return true;
        }

        // Opifex / Nanomage: no race token in RDB names.
        name = $"{placement}_{breedName}{genderName}_naked.png";
        return true;
    }

    public static bool TryRaceToken(int race, out string token)
    {
        token = race switch
        {
            1 => "caucation",
            2 => "african",
            3 => "asian",
            _ => null
        };
        return token != null;
    }

    void EnsureNameCache()
    {
        if (_skinNameToId != null)
            return;

        _skinNameToId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (_database?.Rdb == null)
            return;

        try
        {
            InfoObject info = _database.Get<InfoObject>(1);
            if (info?.Types == null)
                return;

            if (!info.Types.TryGetValue(ResourceTypeId.SkinTexture, out Dictionary<int, string> names)
                || names == null)
                return;

            foreach (KeyValuePair<int, string> pair in names)
            {
                string normalized = NormalizeName(pair.Value);
                if (string.IsNullOrEmpty(normalized))
                    continue;

                if (!_skinNameToId.ContainsKey(normalized))
                    _skinNameToId[normalized] = pair.Key;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"SkinTextureResolver: Failed to load SkinTexture names ({ex.Message}).");
            _skinNameToId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }
    }

    static string NormalizeName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        return name.Trim().Trim('\0').ToLowerInvariant();
    }
}
