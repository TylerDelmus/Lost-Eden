using System;
using System.Collections.Generic;
using AOSharp.Common.GameData;
using SmokeLounge.AOtomation.Messaging.GameData;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using UnityEngine;

public enum StatDetail
{
    Base,
    Bonus,
    Full
}

[Serializable]
internal class StatValue
{
    [SerializeField]
    private int _base;
    internal int Base
    {
        get => _base;
        set
        {
            _base = value;
            Full = _base + _bonus;
        }
    }

    [SerializeField]
    private int _bonus;
    internal int Bonus
    {
        get => _bonus;
        set
        {
            _bonus = value;
            Full = _base + _bonus;
        }
    }

    internal int Full { get; private set; }

    public StatValue()
    {
    }

    internal StatValue(int @base, int bonus)
    {
        Base = @base;
        Bonus = bonus;
    }
}

public class StatCollection
{
    readonly Dictionary<Stat, StatValue> _values = new();

    public event Action<Stat, int, int, bool> StatChanged;

    public bool TryGetValue(Stat stat, out int value, StatDetail detail = StatDetail.Full)
    {
        if (!_values.TryGetValue(stat, out StatValue statValue))
        {
            value = 0;
            return false;
        }

        value = GetDetail(statValue, detail);
        return true;
    }

    public int Get(Stat stat, StatDetail detail = StatDetail.Full)
        => _values.TryGetValue(stat, out StatValue statValue) ? GetDetail(statValue, detail) : 0;

    public IEnumerable<(Stat Stat, int Base, int Bonus, int Full)> GetEntries()
    {
        foreach (KeyValuePair<Stat, StatValue> pair in _values)
            yield return (pair.Key, pair.Value.Base, pair.Value.Bonus, pair.Value.Full);
    }

    public void Set(Stat stat, int value, StatDetail detail = StatDetail.Base)
    {
        bool isInitialSet = !_values.TryGetValue(stat, out StatValue existing);
        if (isInitialSet)
            existing = new StatValue();

        int previousFull = existing.Full;

        if (detail == StatDetail.Bonus)
            existing.Bonus = value;
        else
            existing.Base = value;

        if (!isInitialSet && previousFull == existing.Full)
            return;

        _values[stat] = existing;
        StatChanged?.Invoke(stat, previousFull, existing.Full, isInitialSet);
    }

    public void Apply(StatMessage msg)
    {
        if (msg.Stats == null)
            return;

        foreach (GameTuple<Stat, uint> entry in msg.Stats)
            Set(entry.Value1, (int)entry.Value2);
    }

    public void Apply(FullCharacterMessage msg)
    {
        if (msg.Stats1 != null)
        {
            foreach (GameTuple<int, int> entry in msg.Stats1)
                Set((Stat)entry.Value1, entry.Value2);
        }

        if (msg.Stats2 != null)
        {
            foreach (GameTuple<int, int> entry in msg.Stats2)
                Set((Stat)entry.Value1, entry.Value2);
        }
    }

    public void Apply(SimpleCharFullUpdateMessage msg)
    {
        Set(Stat.Level, msg.Level);
        Set(Stat.Health, msg.Health);
        Set(Stat.AccumulatedDamage, msg.HealthDamage);
        Set(Stat.MonsterData, (int)msg.MonsterData);
        Set(Stat.Scale, msg.MonsterScale);
        Set(Stat.VisualFlags, msg.VisualFlags);
        Set(Stat.TitleLevel, msg.VisibleTitle);
        Set(Stat.RunSpeed, msg.RunSpeedBase);
        Set(Stat.AccountFlags, msg.AccountFlags);
        Set(Stat.Expansion, msg.Expansions);

        if (msg.Flags.HasFlag(SimpleCharFullUpdateFlags.IsNpc))
            Set(Stat.NPCFlags, (int)msg.CharacterFlags);
        else
            Set(Stat.MoreFlags, (int)msg.CharacterFlags);

        if (msg.HeadMesh.HasValue)
            Set(Stat.HeadMesh, msg.HeadMesh.Value);

        if (msg.PlayfieldId.HasValue)
            Set(Stat.LastConcretePlayfieldInstance, msg.PlayfieldId.Value);

        if (msg.FightingTarget.HasValue)
        {
            Set(Stat.SelectedTargetType, (int)msg.FightingTarget.Value.Type);
            Set(Stat.SelectedTarget, msg.FightingTarget.Value.Instance);
        }

        if (msg.Flags2.HasFlag(ScfuFlags2.HasOwner) && msg.Owner.HasValue)
            Set(Stat.OwnerInstance, msg.Owner.Value.Instance);

        ApplyAppearance(msg.Appearance);
        ApplyCharacterInfo(msg.CharacterInfo, msg.Flags);
    }

    void ApplyAppearance(Appearance appearance)
    {
        if (appearance == null)
            return;

        Set(Stat.Breed, (int)appearance.Breed);
        Set(Stat.Fatness, (int)appearance.Fatness);
        Set(Stat.Sex, (int)appearance.Gender);
        Set(Stat.Race, (int)appearance.Race);
        Set(Stat.Side, (int)appearance.Side);
    }

    void ApplyCharacterInfo(SimpleCharInfo info, SimpleCharFullUpdateFlags flags)
    {
        if (info == null)
            return;

        switch (info)
        {
            case SimpleCharInfo.PlayerInfo player:
                Set(Stat.CurrentNano, (int)player.CurrentNano);
                Set(Stat.Team, player.Team);
                Set(Stat.Strength, player.StrengthBase);
                Set(Stat.Agility, player.AgilityBase);
                Set(Stat.Stamina, player.StaminaBase);
                Set(Stat.Intelligence, player.IntelligenceBase);
                Set(Stat.Sense, player.SenseBase);
                Set(Stat.Psychic, player.PsychicBase);
                Set(Stat.ClanInstance, player.OrgId);
                break;

            case SimpleCharInfo.NPCInfo npc:
                if (flags.HasFlag(SimpleCharFullUpdateFlags.HasSmallNpcFamily))
                    Set(Stat.NPCFamily, npc.Family);
                break;
        }
    }

    static int GetDetail(StatValue statValue, StatDetail detail) => detail switch
    {
        StatDetail.Base => statValue.Base,
        StatDetail.Bonus => statValue.Bonus,
        _ => statValue.Full,
    };
}
