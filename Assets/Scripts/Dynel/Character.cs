using System;
using AOSharp.Common.GameData;
using SmokeLounge.AOtomation.Messaging.GameData;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using UnityEngine;
using MovementAction = AOSharp.Common.GameData.MovementAction;
using MovementState = AOSharp.Common.GameData.MovementState;

[RequireComponent(typeof(CharacterMotor))]
[RequireComponent(typeof(VisualDynel))]
public class Character : Dynel
{
    const string MovementConfigResourcePath = "MovementConfig";

    enum SitTransitionPhase
    {
        None,
        Entering,
        Seated,
        Exiting,
    }

    enum JumpAnimPhase
    {
        None,
        Airborne,
        Landing,
    }

    [SerializeField] MovementConfig _movementConfig;

    internal Character FightingTarget { get; set; }
    CharacterMotor _motor;
    VisualDynel _visual;
    string _locomotionLogicalName;
    bool _appearanceStale;
    MovementState _lastMotorState;
    SitTransitionPhase _sitPhase = SitTransitionPhase.None;
    JumpAnimPhase _jumpPhase = JumpAnimPhase.None;
    string _jumpLandLogicalName;

    public CharacterMotor Motor => _motor;
    public VisualDynel Visual => _visual;
    public Action CombatStarted;
    public Action CombatEnded;

    MovementConfig Config
    {
        get
        {
            if (_movementConfig == null)
                _movementConfig = Resources.Load<MovementConfig>(MovementConfigResourcePath);
            return _movementConfig;
        }
    }

    float LocomotionAnimBlendSeconds =>
        Config != null ? Config.LocomotionAnimBlendSeconds : CatAnimPlayer.DefaultBlendSeconds;

    void Awake()
    {
        _motor = GetComponent<CharacterMotor>();
        _visual = GetComponent<VisualDynel>();
        _lastMotorState = _motor.State;
        if (_movementConfig == null && _motor != null && _motor.Config != null)
            _movementConfig = _motor.Config;
    }

    void OnEnable()
    {
        Stats.StatChanged += OnStatChanged;
        if (_motor == null)
            _motor = GetComponent<CharacterMotor>();
        _motor.JumpStarted += OnJumpStarted;
        _motor.JumpLanded += OnJumpLanded;
    }

    void OnDisable()
    {
        Stats.StatChanged -= OnStatChanged;
        if (_motor != null)
        {
            _motor.JumpStarted -= OnJumpStarted;
            _motor.JumpLanded -= OnJumpLanded;
        }
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

    void LateUpdate()
    {
        if (_visual == null || !_visual.HasRenderOffset())
            return;

        MovementConfig config = Config;
        if (config == null)
            return;

        _visual.SmoothRenderOffsetTowardIdentity(
            config.RemoteVisualPositionSharpness,
            config.RemoteVisualYawSharpness,
            Time.deltaTime);
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

        if (stat == Stat.RunSpeed || stat == Stat.Health || stat == Stat.MaxHealth
            || stat == Stat.Strength || stat == Stat.Agility || stat == Stat.GmLevel)
            RefreshMovementSpeed();

        if (stat == Stat.AnimSpeed)
            UpdateLocomotionPlaybackRate();
    }

    public override void Apply(SimpleCharFullUpdateMessage msg)
    {
        base.Apply(msg);
        _motor.Warp(transform.position, transform.rotation);
        _visual.ClearRenderOffset();
        if (msg.MovementStatus.HasValue)
            _motor.ApplyMovementStatus(msg.MovementStatus.Value);
        SyncSitStateFromMotor(spawnedAlreadySeated: true);
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
        MarkAppearanceStale();
    }

    public void Apply(CharDCMoveMessage msg)
    {
        bool hadPose = _visual.TryGetRenderPose(out UnityEngine.Vector3 renderPos, out UnityEngine.Quaternion renderRot);

        // Snap motor pose without clearing speed — remote CharDCMoves arrive while still moving.
        _motor.Warp(msg.Position.ToUnity(), msg.Heading.ToUnity(), resetVelocity: false);
        _motor.ApplyAction(msg.MoveType);
        HandleMotorStateChange();

        if (!hadPose)
            return;

        MovementConfig config = Config;
        float teleportThreshold = config != null ? config.RemoteVisualTeleportThreshold : 10f;
        UnityEngine.Vector3 planarDelta = renderPos - transform.position;
        planarDelta.y = 0f;
        if (planarDelta.sqrMagnitude > teleportThreshold * teleportThreshold)
        {
            _visual.ClearRenderOffset();
            return;
        }

        // Keep the mesh where it was; LateUpdate decays the offset toward identity.
        _visual.SetRenderWorldPose(renderPos, renderRot);
    }

    public void Apply(CharacterActionMessage msg)
    {
        if (msg == null)
            return;

        switch (msg.Action)
        {
            case CharacterActionType.StandUp:
                _motor.ApplyAction(MovementAction.LeaveSit);
                HandleMotorStateChange();
                break;
        }
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

    public bool Play(string logicalName, float blendSeconds = -1f)
    {
        if (blendSeconds < 0f)
            blendSeconds = LocomotionAnimBlendSeconds;
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
        // Character owns locomotion; don't let CatMeshLoader default to stand idle
        // (that races with SyncSitStateFromMotor and sticks seated chars in idle).
        _visual.RequestUpdateAppearance(playIdle: false);
        _locomotionLogicalName = null;
    }

    void RefreshMovementSpeed()
    {
        if (_motor == null)
            return;

        int runSpeed = Stats.Get(Stat.RunSpeed, StatDetail.Full);
        int currentHealth = Stats.Get(Stat.Health, StatDetail.Full);
        int maxHealth = Stats.Get(Stat.MaxHealth, StatDetail.Full);
        _motor.UpdateRunLimitsFromStats(runSpeed, currentHealth, maxHealth);

        int strength = Stats.Get(Stat.Strength, StatDetail.Full);
        int agility = Stats.Get(Stat.Agility, StatDetail.Full);
        int gmLevel = Stats.Get(Stat.GmLevel, StatDetail.Full);
        _motor.UpdateJumpStatsFromStats(strength, agility, gmLevel);
    }

    void UpdateLocomotionAnim()
    {
        if (_visual.VisualRoot == null || _motor == null)
            return;

        if (!_visual.TryGetAnimPlayer(out CatAnimPlayer player))
            return;

        HandleMotorStateChange();

        if (_sitPhase == SitTransitionPhase.Entering || _jumpPhase == JumpAnimPhase.Airborne)
        {
            UpdateLocomotionPlaybackRate();
            return;
        }

        string desired = _motor.GetLocomotionLogicalName();

        // Land is an overlay — keep base locomotion updating underneath.
        if (_jumpPhase == JumpAnimPhase.Landing)
        {
            // Standing idle-land only: cut out when the player starts moving.
            // Back/strafe also use land-idle overlaid on directional loco — don't cancel those.
            if (_jumpLandLogicalName == "jump-land-idle"
                && (_locomotionLogicalName == "idle" || _locomotionLogicalName == "idle-sit")
                && desired != "idle"
                && desired != "idle-sit")
            {
                FinishJumpLand(desired, cancelOverlay: true);
                UpdateLocomotionPlaybackRate();
                return;
            }

            if (!string.Equals(_locomotionLogicalName, desired, StringComparison.Ordinal)
                || !string.Equals(player.CurrentLogicalName, desired, StringComparison.Ordinal))
            {
                if (player.Play(desired, LocomotionAnimBlendSeconds))
                    _locomotionLogicalName = desired;
            }

            UpdateLocomotionPlaybackRate();
            return;
        }

        if (_sitPhase == SitTransitionPhase.Exiting)
        {
            if (desired == "idle")
            {
                UpdateLocomotionPlaybackRate();
                return;
            }

            CancelStandUpTransition();
        }

        // Prefer the player's actual clip over our cache — appearance rebuild can desync them.
        string playing = player.CurrentLogicalName;
        if (!string.Equals(_locomotionLogicalName, desired, StringComparison.Ordinal)
            || !string.Equals(playing, desired, StringComparison.Ordinal))
        {
            if (player.Play(desired, LocomotionAnimBlendSeconds))
                _locomotionLogicalName = desired;
        }

        UpdateLocomotionPlaybackRate();
    }

    void HandleMotorStateChange()
    {
        MovementState state = _motor.State;
        if (state == _lastMotorState)
            return;

        if (state == MovementState.Sit)
        {
            CancelJumpTransition();
            BeginSitDown();
        }
        else if (_lastMotorState == MovementState.Sit)
        {
            BeginStandUp();
        }

        _lastMotorState = state;
    }

    void OnJumpStarted()
    {
        if (_sitPhase != SitTransitionPhase.None)
            return;

        CancelStandUpTransition();
        if (_visual.TryGetAnimPlayer(out CatAnimPlayer existing))
            existing.CancelOverlay();

        _jumpPhase = JumpAnimPhase.Airborne;
        _jumpLandLogicalName = null;
        string takeoffLogical = _motor.GetJumpTakeoffLogicalName();
        _locomotionLogicalName = takeoffLogical;
        if (!_visual.PlayOnce(takeoffLogical, LocomotionAnimBlendSeconds, null))
        {
            // Moving takeoff missing on some creatures — fall back to stand/bare jump.
            if (takeoffLogical == "jump-forward"
                && _visual.PlayOnce("jump-stand", LocomotionAnimBlendSeconds, null))
            {
                _locomotionLogicalName = "jump-stand";
                return;
            }

            _locomotionLogicalName = null;
        }
    }

    void OnJumpLanded()
    {
        if (_sitPhase != SitTransitionPhase.None)
        {
            _jumpPhase = JumpAnimPhase.None;
            _jumpLandLogicalName = null;
            return;
        }

        // Base locomotion continues; land plays as an overlay on top.
        string loco = _motor.GetLocomotionLogicalName();
        _visual.Play(loco, LocomotionAnimBlendSeconds);
        _locomotionLogicalName = loco;

        string landLogical = _motor.GetJumpLandLogicalName();
        _jumpPhase = JumpAnimPhase.Landing;
        _jumpLandLogicalName = landLogical;
        if (!_visual.PlayOverlayOnce(landLogical, LocomotionAnimBlendSeconds, OnJumpLandComplete))
            OnJumpLandComplete();
    }

    void OnJumpLandComplete()
    {
        // Overlay is already fading out in the player — just leave the landing phase.
        FinishJumpLand(_motor.GetLocomotionLogicalName(), cancelOverlay: false);
    }

    void FinishJumpLand(string desired, bool cancelOverlay)
    {
        _jumpPhase = JumpAnimPhase.None;
        _jumpLandLogicalName = null;

        if (!_visual.TryGetAnimPlayer(out CatAnimPlayer player))
            return;

        if (cancelOverlay)
            player.CancelOverlay();

        if (string.IsNullOrEmpty(desired))
            desired = _motor.GetIdleLogicalName();

        if (!string.Equals(_locomotionLogicalName, desired, StringComparison.Ordinal)
            || !string.Equals(player.CurrentLogicalName, desired, StringComparison.Ordinal))
        {
            if (player.Play(desired, LocomotionAnimBlendSeconds))
                _locomotionLogicalName = desired;
        }
        else
        {
            _locomotionLogicalName = desired;
        }
    }

    void CancelJumpTransition()
    {
        if (_jumpPhase == JumpAnimPhase.None)
            return;

        _jumpPhase = JumpAnimPhase.None;
        _jumpLandLogicalName = null;
        _locomotionLogicalName = null;
        if (_visual.TryGetAnimPlayer(out CatAnimPlayer player))
        {
            player.CancelOneShot();
            player.CancelOverlay();
        }
    }

    void SyncSitStateFromMotor(bool spawnedAlreadySeated)
    {
        _lastMotorState = _motor.State;
        if (_motor.State != MovementState.Sit)
        {
            _sitPhase = SitTransitionPhase.None;
            return;
        }

        CancelJumpTransition();
        if (spawnedAlreadySeated)
        {
            _sitPhase = SitTransitionPhase.Seated;
            // Don't claim idle-sit until Play succeeds — visual often isn't ready yet on SCFU.
            _locomotionLogicalName = null;
            if (_visual.TryGetAnimPlayer(out CatAnimPlayer player)
                && player.Play("idle-sit", LocomotionAnimBlendSeconds))
            {
                _locomotionLogicalName = "idle-sit";
            }
        }
    }

    void BeginSitDown()
    {
        CancelJumpTransition();
        _sitPhase = SitTransitionPhase.Entering;
        _locomotionLogicalName = "sit-start";
        if (!_visual.PlayOnce("sit-start", LocomotionAnimBlendSeconds, OnSitDownComplete))
            OnSitDownComplete();
    }

    void OnSitDownComplete()
    {
        _sitPhase = SitTransitionPhase.Seated;
        _locomotionLogicalName = "idle-sit";
        _visual.Play("idle-sit", LocomotionAnimBlendSeconds);
    }

    void BeginStandUp()
    {
        _sitPhase = SitTransitionPhase.Exiting;
        _locomotionLogicalName = "sit-stop";
        if (!_visual.PlayOnce("sit-stop", LocomotionAnimBlendSeconds, OnStandUpComplete))
            OnStandUpComplete();
    }

    void OnStandUpComplete()
    {
        _sitPhase = SitTransitionPhase.None;
        _locomotionLogicalName = _motor.GetIdleLogicalName();
        _visual.Play(_locomotionLogicalName, LocomotionAnimBlendSeconds);
    }

    void CancelStandUpTransition()
    {
        _sitPhase = SitTransitionPhase.None;
        if (_visual.TryGetAnimPlayer(out CatAnimPlayer player))
            player.CancelOneShot();
    }

    static bool IsIdlePlaybackLogicalName(string logicalName)
    {
        if (string.IsNullOrEmpty(logicalName))
            return false;

        return logicalName is "idle" or "idle-sit" or "sit-start" or "sit-stop"
            or "jump-stand" or "jump-forward"
            or "jump-land-idle" or "jump-land-walk" or "jump-land-run";
    }

    void UpdateLocomotionPlaybackRate()
    {
        if (!_visual.TryGetAnimPlayer(out CatAnimPlayer player))
            return;

        if (!IsIdlePlaybackLogicalName(_locomotionLogicalName))
        {
            int animSpeed = Stats.Get(Stat.AnimSpeed, StatDetail.Full);
            float calibration = AnimCalibration.GetFactor(player.CurrentAnimId, player.AnimSet);
            player.PlaybackSpeed = _motor.ComputeLocomotionPlaybackRate(
                _motor.DesiredSpeed,
                _motor.GetLocomotionBaseVelocity(),
                animSpeed,
                calibration);
        }
        else
        {
            player.PlaybackSpeed = 1f;
        }
    }
}
