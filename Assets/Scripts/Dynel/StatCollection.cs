using System;
using System.Collections.Generic;
using AOSharp.Common.GameData;
using SmokeLounge.AOtomation.Messaging.GameData;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;

public class StatCollection
{
    readonly Dictionary<Stat, int> _values = new();

    public event Action<Stat, int, int, bool> StatChanged;

    public bool TryGetValue(Stat stat, out int value) => _values.TryGetValue(stat, out value);

    public int Get(Stat stat) => _values.TryGetValue(stat, out int value) ? value : 0;

    public void Set(Stat stat, int value)
    {
        bool isInitialSet = !_values.ContainsKey(stat);
        int previousValue = isInitialSet ? 0 : _values[stat];
        if (!isInitialSet && previousValue == value)
            return;

        _values[stat] = value;
        StatChanged?.Invoke(stat, previousValue, value, isInitialSet);
    }

    public void Apply(StatMessage msg)
    {
        if (msg.Stats == null)
            return;

        foreach (GameTuple<Stat, uint> entry in msg.Stats)
            Set(entry.Value1, (int)entry.Value2);
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
}
