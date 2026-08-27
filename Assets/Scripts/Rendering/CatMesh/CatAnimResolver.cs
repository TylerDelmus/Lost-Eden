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

        if (!MonsterDataResolver.TryGetAnimIds(_database, monsterDataId, animSet, out List<int> candidates))
            return false;

        EnsureAnimNames();

        string preferredVariant = DummyAnimSetVariants.TryGetValue(animSet, out string variant)
            ? variant
            : DummyAnimSetVariants[0];

        int bestId = 0;
        string bestName = null;
        int bestScore = int.MinValue;

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

        if (bestId <= 0 || bestScore < 0)
            return false;

        animId = bestId;
        resolvedName = bestName;
        return true;
    }

    static string NormalizeAction(string logicalName)
    {
        string trimmed = logicalName.Trim().ToLowerInvariant();
        if (trimmed == "idle" || trimmed == "run")
            return trimmed;

        return null;
    }

    static int ScoreCandidate(string rawName, string action, string preferredVariant)
    {
        string name = NormalizeName(rawName);
        if (string.IsNullOrEmpty(name))
            return int.MinValue;

        if (!name.Contains(action))
            return int.MinValue;

        // Reject directional / social / misc mismatches when looking for base locomotion.
        if (action == "run")
        {
            if (name.Contains("run-back") || name.Contains("run_back"))
                return -50;
            if (name.Contains("run-2h") || name.Contains("run_2h"))
            {
                if (preferredVariant != "2h")
                    return -40;
            }
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

        // Prefer exact action token boundaries: "_run_" / "_run." / ending with run variants.
        if (action == "run" && (name.Contains("_run_") || name.Contains("_run.") || name.EndsWith("_run", StringComparison.Ordinal)))
            score += 30;

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
