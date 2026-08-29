using System;
using System.Collections.Generic;
using AODB.Common.RDBObjects;
using UnityEngine;

public sealed class CatAnimResolver
{
    static readonly Dictionary<int, string> DummyAnimSetVariants = new Dictionary<int, string>
    {
        { 0, "stand" },
        { 1, "blade" },
        { 2, "unarmed" },
        { 3, "rifle" },
        { 4, "smallarms" },
        { 5, "2h" },
    };

    readonly ResourceDatabase _database;
    Dictionary<int, string> _animNames;
    static readonly Dictionary<(int monsterDataId, int animSet, string action), int> ResolveCache =
        new Dictionary<(int monsterDataId, int animSet, string action), int>();
    static readonly object ResolveCacheGate = new object();

    public CatAnimResolver(ResourceDatabase database)
    {
        _database = database;
    }

    public bool TryResolve(int monsterDataId, int animSet, string logicalName, out int animId, out string resolvedName)
    {
        animId = 0;
        resolvedName = null;

        if (string.IsNullOrWhiteSpace(logicalName))
            return false;

        string action = NormalizeAction(logicalName);
        if (action == null)
            return false;

        var cacheKey = (monsterDataId, animSet, action);
        lock (ResolveCacheGate)
        {
            if (ResolveCache.TryGetValue(cacheKey, out int cachedId) && cachedId > 0)
            {
                animId = cachedId;
                resolvedName = GetAnimName(cachedId);
                return true;
            }
        }

        if (!MonsterDataResolver.TryGetAnimIds(_database, monsterDataId, animSet, out List<int> candidates))
            return false;

        EnsureAnimNames();

        string preferredVariant = DummyAnimSetVariants.TryGetValue(animSet, out string variant)
            ? variant
            : DummyAnimSetVariants[0];

        if (TryResolveSitSemantic(action, candidates, preferredVariant, out animId, out resolvedName)
            || TryResolveJumpSemantic(action, candidates, preferredVariant, out animId, out resolvedName))
        {
            lock (ResolveCacheGate)
                ResolveCache[cacheKey] = animId;
            return true;
        }

        if (!TryFindBest(candidates, action, preferredVariant, out int bestId, out string bestName, out int bestScore)
            || bestScore < 0)
        {
            string fallback = action switch
            {
                "run" => "walk",
                "run-back" => "walk-back",
                "walk-left" or "walk-right" => "walk",
                _ => null,
            };

            if (fallback == null
                || !TryFindBest(candidates, fallback, preferredVariant, out bestId, out bestName, out bestScore)
                || bestScore < 0)
            {
                return false;
            }
        }

        animId = bestId;
        resolvedName = bestName;
        lock (ResolveCacheGate)
            ResolveCache[cacheKey] = animId;
        return true;
    }

    bool TryFindBest(
        List<int> candidates,
        string action,
        string preferredVariant,
        out int bestId,
        out string bestName,
        out int bestScore)
    {
        bestId = 0;
        bestName = null;
        bestScore = int.MinValue;

        for (int i = 0; i < candidates.Count; i++)
        {
            int id = candidates[i];
            if (id <= 0)
                continue;

            string name = GetAnimName(id);
            if (string.IsNullOrEmpty(name))
                continue;

            int score = ScoreCandidate(name, action, preferredVariant);
            if (score <= bestScore)
                continue;

            bestScore = score;
            bestId = id;
            bestName = name;
        }

        return bestId > 0;
    }

    bool TryResolveSitSemantic(
        string semantic,
        List<int> candidates,
        string preferredVariant,
        out int animId,
        out string resolvedName)
    {
        animId = 0;
        resolvedName = null;

        if (!TryGetSitAliases(semantic, out string[] aliases))
            return false;

        for (int i = 0; i < aliases.Length; i++)
        {
            if (!TryFindBest(candidates, aliases[i], preferredVariant, out int id, out string name, out int score)
                || score < 0)
            {
                continue;
            }

            animId = id;
            resolvedName = name;
            return true;
        }

        return false;
    }

    bool TryResolveJumpSemantic(
        string semantic,
        List<int> candidates,
        string preferredVariant,
        out int animId,
        out string resolvedName)
    {
        animId = 0;
        resolvedName = null;

        if (!TryGetJumpAliases(semantic, out string[] aliases))
            return false;

        for (int i = 0; i < aliases.Length; i++)
        {
            if (!TryFindBest(candidates, aliases[i], preferredVariant, out int id, out string name, out int score)
                || score < 0)
            {
                continue;
            }

            animId = id;
            resolvedName = name;
            return true;
        }

        return false;
    }

    static bool TryGetSitAliases(string semantic, out string[] aliases)
    {
        switch (semantic)
        {
            case "idle-sit":
                aliases = new[] { "idle-ground", "idle-sit", "sit_idle" };
                return true;
            case "sit-start":
                aliases = new[] { "ground-start", "sit-start", "sit_down" };
                return true;
            case "sit-stop":
                aliases = new[] { "ground-stop", "sit-stop", "stand_up" };
                return true;
            default:
                aliases = null;
                return false;
        }
    }

    static bool TryGetJumpAliases(string semantic, out string[] aliases)
    {
        switch (semantic)
        {
            case "jump-stand":
                // Humanoids use jump-stand; many creatures only have bare jump_01.
                aliases = new[] { "jump-stand", "jump" };
                return true;
            case "jump-forward":
                aliases = new[] { "jump-forward" };
                return true;
            case "jump-land-idle":
            case "jump-land-walk":
            case "jump-land-run":
                aliases = new[] { semantic };
                return true;
            default:
                aliases = null;
                return false;
        }
    }

    static string NormalizeAction(string logicalName)
    {
        string trimmed = logicalName.Trim().ToLowerInvariant();
        return trimmed switch
        {
            "idle" => "idle",
            "run" => "run",
            "run-back" => "run-back",
            "walk-left" => "walk-left",
            "walk-right" => "walk-right",
            "walk" => "walk",
            "walk-back" => "walk-back",
            "idle-sit" => "idle-sit",
            "sit-start" => "sit-start",
            "sit-stop" => "sit-stop",
            "jump-stand" => "jump-stand",
            "jump-forward" => "jump-forward",
            "jump-land-idle" => "jump-land-idle",
            "jump-land-walk" => "jump-land-walk",
            "jump-land-run" => "jump-land-run",
            _ => null,
        };
    }

    static int ScoreCandidate(string rawName, string action, string preferredVariant)
    {
        string name = NormalizeName(rawName);
        if (string.IsNullOrEmpty(name))
            return int.MinValue;

        if (!name.Contains(action))
            return int.MinValue;

        // Reject directional / social / misc mismatches when looking for base forward locomotion.
        if (action == "run" || action == "walk")
        {
            if (name.Contains(action + "-back") || name.Contains(action + "_back"))
                return -50;
            if (name.Contains(action + "-left") || name.Contains(action + "_left"))
                return -50;
            if (name.Contains(action + "-right") || name.Contains(action + "_right"))
                return -50;
            if (name.Contains(action + "-2h") || name.Contains(action + "_2h"))
            {
                if (preferredVariant != "2h")
                    return -40;
            }
        }

        if (action == "jump" || action == "jump-stand" || action == "jump-forward")
        {
            if (action != "jump-forward"
                && (name.Contains("jump-forward") || name.Contains("jump_forward")))
            {
                return int.MinValue;
            }

            if (action == "jump-forward"
                && !name.Contains("jump-forward")
                && !name.Contains("jump_forward"))
            {
                return int.MinValue;
            }

            if (name.Contains("jump-land") || name.Contains("jump_land"))
                return int.MinValue;
            if (name.Contains("attack")
                || name.Contains("roundkick")
                || name.Contains("jumpslam")
                || name.Contains("jumpbite")
                || name.Contains("hoverboard"))
            {
                return int.MinValue;
            }
        }

        if (action.StartsWith("jump-land", StringComparison.Ordinal))
        {
            if (name.Contains("-2h") || name.Contains("_2h"))
            {
                if (preferredVariant != "2h")
                    return -40;
            }
        }

        if (action == "run-back"
            && !name.Contains("run-back")
            && !name.Contains("run_back"))
        {
            return int.MinValue;
        }

        if (action == "walk-back"
            && !name.Contains("walk-back")
            && !name.Contains("walk_back")
            && !name.Contains("walk-backwards")
            && !name.Contains("walk_backwards"))
        {
            return int.MinValue;
        }

        if (action == "walk-left"
            && !name.Contains("walk-left")
            && !name.Contains("walk_left"))
        {
            return int.MinValue;
        }

        if (action == "walk-right"
            && !name.Contains("walk-right")
            && !name.Contains("walk_right"))
        {
            return int.MinValue;
        }

        if (name.Contains("social-") || name.Contains("social_"))
            return -100;

        int score = 10;

        string actionVariant = action + "-" + preferredVariant;
        string actionVariantAlt = action + "_" + preferredVariant;
        if (name.Contains(actionVariant) || name.Contains(actionVariantAlt))
            score += 100;
        else if (name.Contains(preferredVariant))
            score += 40;

        // Prefer classic idle-stand / idle-unarmed style over bare idle tokens.
        if (action == "idle")
        {
            if (name.Contains("idle-stand") || name.Contains("idle_stand"))
                score += preferredVariant == "stand" ? 20 : 5;
            if (name.Contains("idle-") || name.Contains("idle_"))
                score += 10;
        }

        // Prefer exact action token boundaries: "_run_" / "_walk_" / ending with the action.
        if ((action == "run" || action == "walk")
            && (name.Contains("_" + action + "_")
                || name.Contains("_" + action + ".")
                || name.EndsWith("_" + action, StringComparison.Ordinal)))
        {
            score += 30;
        }

        // Slight preference for shorter / simpler names (fewer modifiers).
        int hyphenCount = 0;
        for (int i = 0; i < name.Length; i++)
        {
            if (name[i] == '-' || name[i] == '_')
                hyphenCount++;
        }

        score -= Math.Min(hyphenCount, 8);

        return score;
    }

    string GetAnimName(int animId)
    {
        if (_animNames != null && _animNames.TryGetValue(animId, out string named) && !string.IsNullOrEmpty(named))
            return named;

        try
        {
            CATAnim catAnim = _database.Get<CATAnim>(ResourceTypeId.Anim, animId);
            if (catAnim == null || string.IsNullOrEmpty(catAnim.Name))
                return null;

            return NormalizeName(catAnim.Name);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"CatAnimResolver: Failed to load CATAnim {animId} ({ex.Message}).");
            return null;
        }
    }

    void EnsureAnimNames()
    {
        if (_animNames != null)
            return;

        _animNames = new Dictionary<int, string>();
        if (_database?.Rdb == null)
            return;

        try
        {
            InfoObject info = _database.Get<InfoObject>(1);
            if (info?.Types == null)
                return;

            if (!info.Types.TryGetValue(ResourceTypeId.Anim, out Dictionary<int, string> names) || names == null)
                return;

            foreach (KeyValuePair<int, string> pair in names)
            {
                if (string.IsNullOrEmpty(pair.Value))
                    continue;

                _animNames[pair.Key] = NormalizeName(pair.Value);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"CatAnimResolver: Failed to load anim names ({ex.Message}).");
        }
    }

    static string NormalizeName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        string trimmed = name.Trim().Trim('\0').ToLowerInvariant();
        if (trimmed.EndsWith(".ani", StringComparison.Ordinal))
            trimmed = trimmed.Substring(0, trimmed.Length - 4);

        return trimmed.Trim();
    }
}
