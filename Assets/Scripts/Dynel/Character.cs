using System;
using AOSharp.Common.GameData;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using UnityEngine;

[RequireComponent(typeof(CharacterMotor))]
[RequireComponent(typeof(VisualDynel))]
public class Character : Dynel
{
    const float LocomotionAnimBlendSeconds = CatAnimPlayer.DefaultBlendSeconds;

    internal Character FightingTarget { get; set; }
    CharacterMotor _motor;
    VisualDynel _visual;
    string _locomotionLogicalName;
    bool _appearanceStale;

    public CharacterMotor Motor => _motor;
    public VisualDynel Visual => _visual;
    public Action CombatStarted;
    public Action CombatEnded;

    void Awake()
    {
        _motor = GetComponent<CharacterMotor>();
        _visual = GetComponent<VisualDynel>();
    }

    void OnEnable()
    {
        Stats.StatChanged += OnStatChanged;
    }

    void OnDisable()
    {
        Stats.StatChanged -= OnStatChanged;
    }

    void Update()
    {
        if (_appearanceStale)
        {
            _appearanceStale = false;
            UpdateAppearance();
        }

        UpdateLocomotionAnim();
    }

    void OnStatChanged(Stat stat, int previousValue, int value, bool isInitialSet)
    {
        if (stat == Stat.MonsterData
            || stat == Stat.Breed
            || stat == Stat.Sex
            || stat == Stat.Fatness
            || stat == Stat.Race)
        {
            MarkAppearanceStale();
            return;
        }

        if (stat == Stat.HeadMesh || stat == Stat.VisualFlags)
        {
            _visual.ApplyAttachedMeshes();
            return;
        }

        if (stat == Stat.Scale)
        {
            _visual.ApplyScale();
            return;
        }

        if (stat == Stat.AnimSet)
        {
            if (_visual.TryGetAnimPlayer(out CatAnimPlayer player))
                player.SetAnimSet(value);
        }

        if (stat == Stat.RunSpeed || stat == Stat.Health || stat == Stat.MaxHealth)
            RefreshMovementSpeed();

        if (stat == Stat.AnimSpeed)
            UpdateLocomotionPlaybackRate();
    }

    public override void Apply(SimpleCharFullUpdateMessage msg)
    {
        base.Apply(msg);
        _motor.Warp(transform.position, transform.rotation);
        if (msg.MovementStatus.HasValue)
            _motor.ApplyMovementStatus(msg.MovementStatus.Value);
        RefreshMovementSpeed();
        _visual.StoreTextures(msg.Textures);
        _visual.StoreMeshes(msg.Meshes);
        MarkAppearanceStale();
    }

    public void Apply(AppearanceUpdateMessage msg)
    {
        if (msg == null)
            return;
        Stats.Set(Stat.VisualFlags, msg.VisualFlags);
        _visual.StoreTextures(msg.Textures);
        _visual.StoreMeshes(msg.Meshes);
        _visual.ApplyBodySlotTextures();
        _visual.ApplyAttachedMeshes();
    }

    public void Apply(CharDCMoveMessage msg)
    {
        _motor.Warp(msg.Position.ToUnity(), msg.Heading.ToUnity());
        _motor.ApplyAction(msg.MoveType);
    }

    public void Apply(FollowTargetMessage msg)
    {
        if (msg.Info is not FollowTargetMessage.PathInfo pathInfo)
            return;
        if (pathInfo.Waypoints == null || pathInfo.Waypoints.Length == 0)
        {
            _motor.ClearPath();
            return;
        }
        var waypoints = new UnityEngine.Vector3[pathInfo.Waypoints.Length];
        for (int i = 0; i < pathInfo.Waypoints.Length; i++)
            waypoints[i] = pathInfo.Waypoints[i].ToUnity();
        _motor.SetPath(waypoints);
    }

    public bool Play(string logicalName, float blendSeconds = LocomotionAnimBlendSeconds)
    {
        bool played = _visual.Play(logicalName, blendSeconds);
        if (played)
            _locomotionLogicalName = logicalName?.Trim().ToLowerInvariant();
        return played;
    }

    public bool TryGetAttractor(AttractorPlace place, out Attractor attractor)
        => _visual.TryGetAttractor(place, out attractor);

    public void MarkAppearanceStale() => _appearanceStale = true;

    public void UpdateAppearance()
    {
        int previousCatMeshId = _visual.LoadedCatMeshId;
        _visual.UpdateAppearance();
        if (_visual.LoadedCatMeshId != previousCatMeshId)
        {
            _locomotionLogicalName = null;
            UpdateLocomotionAnim();
        }
    }

    void RefreshMovementSpeed()
    {
        if (_motor == null)
            return;

        int runSpeed = Stats.Get(Stat.RunSpeed, StatDetail.Full);
        int currentHealth = Stats.Get(Stat.Health, StatDetail.Full);
        int maxHealth = Stats.Get(Stat.MaxHealth, StatDetail.Full);
        _motor.UpdateRunLimitsFromStats(runSpeed, currentHealth, maxHealth);
    }

    void UpdateLocomotionAnim()
    {
        if (_visual.VisualRoot == null || _motor == null)
            return;

        string desired = _motor.GetLocomotionLogicalName();
        if (!string.Equals(_locomotionLogicalName, desired, StringComparison.Ordinal))
        {
            if (_visual.TryGetAnimPlayer(out CatAnimPlayer player)
                && player.Play(desired, LocomotionAnimBlendSeconds))
            {
                _locomotionLogicalName = desired;
            }
        }

        UpdateLocomotionPlaybackRate();
    }

    void UpdateLocomotionPlaybackRate()
    {
        if (!_visual.TryGetAnimPlayer(out CatAnimPlayer player))
            return;

        if (!string.Equals(_locomotionLogicalName, "idle", StringComparison.Ordinal))
        {
            int animSpeed = Stats.Get(Stat.AnimSpeed, StatDetail.Full);
            if (animSpeed <= 0)
                animSpeed = 100;
            player.PlaybackSpeed = CharacterMotor.ComputeRunPlaybackRate(_motor.GetLocomotionMaxSpeed(), animSpeed);
        }
        else
        {
            player.PlaybackSpeed = 1f;
        }
    }
}
