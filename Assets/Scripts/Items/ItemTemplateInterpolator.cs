using System;
using System.Collections.Generic;
using AODB.Common.Enums;
using AODB.Common.RDBObjects;
using AODB.Common.Structs;

/// <summary>
/// Linearly interpolates an ItemObject between low/high templates at a target QL.
/// </summary>
public static class ItemTemplateInterpolator
{
    public static ItemObject Interpolate(ItemObject low, ItemObject high, int quality)
    {
        if (low == null)
            throw new ArgumentNullException(nameof(low));
        if (high == null)
            throw new ArgumentNullException(nameof(high));

        int lowQl = GetQl(low);
        int highQl = GetQl(high);

        if (ReferenceEquals(low, high) || lowQl == highQl)
        {
            ItemObject clone = CloneItem(low);
            SetQl(clone, quality);
            return clone;
        }

        float t = (quality - lowQl) / (float)(highQl - lowQl);
        if (t < 0f) t = 0f;
        else if (t > 1f) t = 1f;

        ItemBase nearer = t < 0.5f ? low : high;
        var result = new ItemObject
        {
            DynelType = nearer.DynelType,
            Name = nearer.Name,
            Description = nearer.Description,
            Stats = LerpStats(low.Stats, high.Stats, t),
            UnkSkills0x06K0x1B = LerpIntStats(low.UnkSkills0x06K0x1B, high.UnkSkills0x06K0x1B, t),
            SkillChecks = LerpSkillChecks(low.SkillChecks, high.SkillChecks, t),
            Requirements = LerpRequirements(low.Requirements, high.Requirements, t, nearer == low),
            Modifiers = LerpModifiers(low.Modifiers, high.Modifiers, t, nearer == low),
        };

        SetQl(result, quality);
        return result;
    }

    static int GetQl(ItemBase template)
    {
        if (template.Stats != null && template.Stats.TryGetValue(StatId.level, out uint ql))
            return (int)ql;
        return 1;
    }

    static void SetQl(ItemBase template, int quality)
    {
        template.Stats ??= new Dictionary<StatId, uint>();
        template.Stats[StatId.level] = (uint)quality;
    }

    static ItemObject CloneItem(ItemObject source)
    {
        return new ItemObject
        {
            DynelType = source.DynelType,
            Name = source.Name,
            Description = source.Description,
            Stats = CloneStats(source.Stats),
            UnkSkills0x06K0x1B = CloneIntStats(source.UnkSkills0x06K0x1B),
            SkillChecks = CloneSkillChecks(source.SkillChecks),
            Requirements = CloneRequirements(source.Requirements),
            Modifiers = CloneModifiers(source.Modifiers),
        };
    }

    static uint LerpUInt(uint a, uint b, float t)
    {
        return (uint)Math.Round(a + (b - (double)a) * t, MidpointRounding.AwayFromZero);
    }

    static object LerpObject(object low, object high, float t, bool preferLow)
    {
        if (TryToDouble(low, out double a) && TryToDouble(high, out double b))
        {
            double lerped = a + (b - a) * t;
            double rounded = Math.Round(lerped, MidpointRounding.AwayFromZero);

            if (low is int || high is int)
                return (int)rounded;
            if (low is uint || high is uint)
                return (uint)rounded;
            if (low is long || high is long)
                return (long)rounded;
            if (low is float || high is float)
                return (float)lerped;
            if (low is double || high is double)
                return lerped;
            if (low is short || high is short)
                return (short)rounded;
            if (low is byte || high is byte)
                return (byte)rounded;

            return Convert.ChangeType(rounded, low?.GetType() ?? typeof(int));
        }

        return preferLow ? low : high;
    }

    static bool TryToDouble(object value, out double result)
    {
        switch (value)
        {
            case null:
                result = 0;
                return false;
            case IConvertible convertible when value is not string and not bool:
                try
                {
                    result = convertible.ToDouble(null);
                    return true;
                }
                catch
                {
                    result = 0;
                    return false;
                }
            default:
                result = 0;
                return false;
        }
    }

    static Dictionary<StatId, uint> LerpStats(
        Dictionary<StatId, uint> low,
        Dictionary<StatId, uint> high,
        float t)
    {
        var result = new Dictionary<StatId, uint>();
        if (low == null && high == null)
            return result;

        var keys = new HashSet<StatId>();
        if (low != null)
            foreach (StatId key in low.Keys)
                keys.Add(key);
        if (high != null)
            foreach (StatId key in high.Keys)
                keys.Add(key);

        foreach (StatId key in keys)
        {
            uint a = 0;
            uint b = 0;
            low?.TryGetValue(key, out a);
            high?.TryGetValue(key, out b);
            result[key] = LerpUInt(a, b, t);
        }

        return result;
    }

    static Dictionary<int, uint> LerpIntStats(
        Dictionary<int, uint> low,
        Dictionary<int, uint> high,
        float t)
    {
        var result = new Dictionary<int, uint>();
        if (low == null && high == null)
            return result;

        var keys = new HashSet<int>();
        if (low != null)
            foreach (int key in low.Keys)
                keys.Add(key);
        if (high != null)
            foreach (int key in high.Keys)
                keys.Add(key);

        foreach (int key in keys)
        {
            uint a = 0;
            uint b = 0;
            low?.TryGetValue(key, out a);
            high?.TryGetValue(key, out b);
            result[key] = LerpUInt(a, b, t);
        }

        return result;
    }

    static Dictionary<SkillCheck, Dictionary<StatId, uint>> LerpSkillChecks(
        Dictionary<SkillCheck, Dictionary<StatId, uint>> low,
        Dictionary<SkillCheck, Dictionary<StatId, uint>> high,
        float t)
    {
        var result = new Dictionary<SkillCheck, Dictionary<StatId, uint>>();
        if (low == null && high == null)
            return result;

        var keys = new HashSet<SkillCheck>();
        if (low != null)
            foreach (SkillCheck key in low.Keys)
                keys.Add(key);
        if (high != null)
            foreach (SkillCheck key in high.Keys)
                keys.Add(key);

        foreach (SkillCheck key in keys)
        {
            Dictionary<StatId, uint> lowStats = null;
            Dictionary<StatId, uint> highStats = null;
            low?.TryGetValue(key, out lowStats);
            high?.TryGetValue(key, out highStats);
            result[key] = LerpStats(lowStats, highStats, t);
        }

        return result;
    }

    static Dictionary<ActionType, Requirement> LerpRequirements(
        Dictionary<ActionType, Requirement> low,
        Dictionary<ActionType, Requirement> high,
        float t,
        bool preferLow)
    {
        var result = new Dictionary<ActionType, Requirement>();
        Dictionary<ActionType, Requirement> structure = preferLow
            ? low ?? high
            : high ?? low;
        if (structure == null)
            return result;

        foreach (KeyValuePair<ActionType, Requirement> pair in structure)
        {
            Requirement lowReq = null;
            Requirement highReq = null;
            low?.TryGetValue(pair.Key, out lowReq);
            high?.TryGetValue(pair.Key, out highReq);

            Requirement near = preferLow ? lowReq ?? highReq : highReq ?? lowReq;
            if (near == null)
                continue;

            result[pair.Key] = new Requirement
            {
                Criterion = LerpCriterionMap(
                    lowReq?.Criterion,
                    highReq?.Criterion,
                    t,
                    preferLow),
            };
        }

        return result;
    }

    static Dictionary<ActionType, List<RequirementCriterion>> LerpCriterionMap(
        Dictionary<ActionType, List<RequirementCriterion>> low,
        Dictionary<ActionType, List<RequirementCriterion>> high,
        float t,
        bool preferLow)
    {
        var result = new Dictionary<ActionType, List<RequirementCriterion>>();
        Dictionary<ActionType, List<RequirementCriterion>> structure = preferLow
            ? low ?? high
            : high ?? low;
        if (structure == null)
            return result;

        foreach (KeyValuePair<ActionType, List<RequirementCriterion>> pair in structure)
        {
            List<RequirementCriterion> lowList = null;
            List<RequirementCriterion> highList = null;
            low?.TryGetValue(pair.Key, out lowList);
            high?.TryGetValue(pair.Key, out highList);

            List<RequirementCriterion> near = preferLow ? lowList ?? highList : highList ?? lowList;
            if (near == null)
                continue;

            var lerped = new List<RequirementCriterion>(near.Count);
            for (int i = 0; i < near.Count; i++)
            {
                RequirementCriterion a = lowList != null && i < lowList.Count ? lowList[i] : near[i];
                RequirementCriterion b = highList != null && i < highList.Count ? highList[i] : a;

                lerped.Add(new RequirementCriterion
                {
                    Stat = preferLow ? a.Stat : b.Stat,
                    Operator = preferLow ? a.Operator : b.Operator,
                    Value = a.Stat == b.Stat && a.Operator == b.Operator
                        ? LerpUInt(a.Value, b.Value, t)
                        : (preferLow ? a.Value : b.Value),
                });
            }

            result[pair.Key] = lerped;
        }

        return result;
    }

    static Dictionary<EventType, Modifier> LerpModifiers(
        Dictionary<EventType, Modifier> low,
        Dictionary<EventType, Modifier> high,
        float t,
        bool preferLow)
    {
        var result = new Dictionary<EventType, Modifier>();
        Dictionary<EventType, Modifier> structure = preferLow
            ? low ?? high
            : high ?? low;
        if (structure == null)
            return result;

        foreach (KeyValuePair<EventType, Modifier> pair in structure)
        {
            Modifier lowMod = null;
            Modifier highMod = null;
            low?.TryGetValue(pair.Key, out lowMod);
            high?.TryGetValue(pair.Key, out highMod);

            Modifier near = preferLow ? lowMod ?? highMod : highMod ?? lowMod;
            if (near == null)
                continue;

            result[pair.Key] = new Modifier
            {
                Modifiers = LerpFunctionMap(
                    lowMod?.Modifiers,
                    highMod?.Modifiers,
                    t,
                    preferLow),
            };
        }

        return result;
    }

    static Dictionary<FunctionType, List<Dictionary<FunctionOperator, object>>> LerpFunctionMap(
        Dictionary<FunctionType, List<Dictionary<FunctionOperator, object>>> low,
        Dictionary<FunctionType, List<Dictionary<FunctionOperator, object>>> high,
        float t,
        bool preferLow)
    {
        var result = new Dictionary<FunctionType, List<Dictionary<FunctionOperator, object>>>();
        Dictionary<FunctionType, List<Dictionary<FunctionOperator, object>>> structure = preferLow
            ? low ?? high
            : high ?? low;
        if (structure == null)
            return result;

        foreach (KeyValuePair<FunctionType, List<Dictionary<FunctionOperator, object>>> pair in structure)
        {
            List<Dictionary<FunctionOperator, object>> lowList = null;
            List<Dictionary<FunctionOperator, object>> highList = null;
            low?.TryGetValue(pair.Key, out lowList);
            high?.TryGetValue(pair.Key, out highList);

            List<Dictionary<FunctionOperator, object>> near =
                preferLow ? lowList ?? highList : highList ?? lowList;
            if (near == null)
                continue;

            var lerped = new List<Dictionary<FunctionOperator, object>>(near.Count);
            for (int i = 0; i < near.Count; i++)
            {
                Dictionary<FunctionOperator, object> a =
                    lowList != null && i < lowList.Count ? lowList[i] : near[i];
                Dictionary<FunctionOperator, object> b =
                    highList != null && i < highList.Count ? highList[i] : a;
                lerped.Add(LerpOperatorMap(a, b, t, preferLow));
            }

            result[pair.Key] = lerped;
        }

        return result;
    }

    static Dictionary<FunctionOperator, object> LerpOperatorMap(
        Dictionary<FunctionOperator, object> low,
        Dictionary<FunctionOperator, object> high,
        float t,
        bool preferLow)
    {
        var result = new Dictionary<FunctionOperator, object>();
        Dictionary<FunctionOperator, object> structure = preferLow
            ? low ?? high
            : high ?? low;
        if (structure == null)
            return result;

        foreach (KeyValuePair<FunctionOperator, object> pair in structure)
        {
            object a = null;
            object b = null;
            bool hasLow = low != null && low.TryGetValue(pair.Key, out a);
            bool hasHigh = high != null && high.TryGetValue(pair.Key, out b);

            if (hasLow && hasHigh)
                result[pair.Key] = LerpObject(a, b, t, preferLow);
            else
                result[pair.Key] = preferLow
                    ? (hasLow ? a : b)
                    : (hasHigh ? b : a);
        }

        return result;
    }

    static Dictionary<StatId, uint> CloneStats(Dictionary<StatId, uint> source)
    {
        return source == null
            ? new Dictionary<StatId, uint>()
            : new Dictionary<StatId, uint>(source);
    }

    static Dictionary<int, uint> CloneIntStats(Dictionary<int, uint> source)
    {
        return source == null
            ? new Dictionary<int, uint>()
            : new Dictionary<int, uint>(source);
    }

    static Dictionary<SkillCheck, Dictionary<StatId, uint>> CloneSkillChecks(
        Dictionary<SkillCheck, Dictionary<StatId, uint>> source)
    {
        var result = new Dictionary<SkillCheck, Dictionary<StatId, uint>>();
        if (source == null)
            return result;

        foreach (KeyValuePair<SkillCheck, Dictionary<StatId, uint>> pair in source)
            result[pair.Key] = CloneStats(pair.Value);

        return result;
    }

    static Dictionary<ActionType, Requirement> CloneRequirements(
        Dictionary<ActionType, Requirement> source)
    {
        var result = new Dictionary<ActionType, Requirement>();
        if (source == null)
            return result;

        foreach (KeyValuePair<ActionType, Requirement> pair in source)
        {
            var criterion = new Dictionary<ActionType, List<RequirementCriterion>>();
            if (pair.Value?.Criterion != null)
            {
                foreach (KeyValuePair<ActionType, List<RequirementCriterion>> crit in pair.Value.Criterion)
                {
                    var list = new List<RequirementCriterion>();
                    if (crit.Value != null)
                    {
                        foreach (RequirementCriterion c in crit.Value)
                        {
                            list.Add(new RequirementCriterion
                            {
                                Stat = c.Stat,
                                Value = c.Value,
                                Operator = c.Operator,
                            });
                        }
                    }

                    criterion[crit.Key] = list;
                }
            }

            result[pair.Key] = new Requirement { Criterion = criterion };
        }

        return result;
    }

    static Dictionary<EventType, Modifier> CloneModifiers(Dictionary<EventType, Modifier> source)
    {
        var result = new Dictionary<EventType, Modifier>();
        if (source == null)
            return result;

        foreach (KeyValuePair<EventType, Modifier> pair in source)
        {
            var functions = new Dictionary<FunctionType, List<Dictionary<FunctionOperator, object>>>();
            if (pair.Value?.Modifiers != null)
            {
                foreach (KeyValuePair<FunctionType, List<Dictionary<FunctionOperator, object>>> fn in pair.Value.Modifiers)
                {
                    var list = new List<Dictionary<FunctionOperator, object>>();
                    if (fn.Value != null)
                    {
                        foreach (Dictionary<FunctionOperator, object> ops in fn.Value)
                            list.Add(ops == null
                                ? new Dictionary<FunctionOperator, object>()
                                : new Dictionary<FunctionOperator, object>(ops));
                    }

                    functions[fn.Key] = list;
                }
            }

            result[pair.Key] = new Modifier { Modifiers = functions };
        }

        return result;
    }
}
