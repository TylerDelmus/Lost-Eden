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

    internal Character FightingTarget { get; private set; }
    CharacterMotor _motor;
    VisualDynel _visual;
    string _locomotionLogicalName;
    string _strafeOverlayLogicalName;
    readonly EquippedWeaponHands _hands = new EquippedWeaponHands();
    readonly AnimHolder _animHolder = new AnimHolder();
    bool _fightStance;
    int _locomotionKind;
    int _strafeOverlayKind;
    bool _appearanceStale;
    MovementState _lastMotorState;
    SitTransitionPhase _sitPhase = SitTransitionPhase.None;
    JumpAnimPhase _jumpPhase = JumpAnimPhase.None;
    string _jumpLandLogicalName;

    public CharacterMotor Motor => _motor;
    public VisualDynel Visual => _visual;
    public Action CombatStarted;
    public Action CombatEnded;

    public bool CanAttack(Character target)
    {
        if (target == null || target == this)
            return false;

        // AO Side: Neutral=0, Clan=1, Omni=2, Monster=3
        const int clan = 1;
        const int omni = 2;
        const int monster = 3;

        int targetSide = target.Stats.Get(Stat.Side);
        if (targetSide == monster)
            return true;

        int mySide = Stats.Get(Stat.Side);
        return (mySide == clan && targetSide == omni) || (mySide == omni && targetSide == clan);
    }

    internal void SetFightingTarget(Character target)
    {
        if (FightingTarget == target)
            return;

        bool wasFighting = FightingTarget != null;
        FightingTarget = target;

        if (target != null)
            CombatStarted?.Invoke();
        else if (wasFighting)
            CombatEnded?.Invoke();

        RefreshAnimHolder();
    }

    public bool IsFighting => _fightStance || FightingTarget != null;

    public void RefreshEquippedHands(PlayerInventory inventory)
    {
        if (inventory == null)
        {
            _hands.Clear();
            RefreshAnimHolder();
            _locomotionKind = 0;
            return;
        }

        inventory.TryGetByPlacement(AnimKindIds.EquipRight, out InventoryItem right);
        inventory.TryGetByPlacement(AnimKindIds.EquipLeft, out InventoryItem left);
        inventory.TryGetByPlacement(AnimKindIds.EquipUtil, out InventoryItem util);
        _hands.Set(right?.Item, left?.Item, util?.Item);
        RefreshAnimHolder();
        _locomotionKind = 0;
    }

    void RefreshAnimHolder()
    {
        int animSet = _hands.StanceAnimSet;
        if (animSet < 0)
            _animHolder.ApplyUnarmedDefaults(IsFighting);
        else
            _animHolder.ApplyStance(animSet, IsFighting);
    }

    void RefreshMoveSlots()
        => _animHolder.ApplyMoveSlots(_hands.StanceAnimSet);

    public void ApplyFightEnter(Character target)
    {
        bool already = IsFighting;
        _fightStance = true;
        if (target != null)
            SetFightingTarget(target);
        else
            RefreshAnimHolder();

        if (!already)
            PlayFightEnter();
    }

    public void ApplyFightLeave()
    {
        if (!IsFighting)
            return;

        _fightStance = false;
        SetFightingTarget(null);
        PlayFightLeave();
    }

    public void PlayAttackerSwingAnim(int weaponSlot)
    {
        if (TryGetAnimPlayer(out CatAnimPlayer player)
            && player.HasLoopKeyPlaying(AnimKindIds.MapAttack))
            return;

        int kind = ResolveSwingKind(weaponSlot);
        if (kind == 0)
            kind = FallbackSwingKind();

        if (kind == 0)
            return;

        Item weapon = _hands.ItemInSlot(weaponSlot);
        float speed = ResolveSwingSpeed(weapon, kind);
        if (!_visual.PlayKindOnce(
            kind,
            0f,
            null,
            overlay: true,
            speed,
            AnimKindIds.MapAttack))
        {
            PlaySwingFallback();
        }
    }

    int ResolveSwingKind(int weaponSlot)
    {
        if (Stats.Get(Stat.MonsterData) != 0 && !_visual.HasWeaponAttractorBones())
            return AnimKindIds.UnarmedRSwing;

        WeaponAnimListMap map = _hands.MapForSlot(weaponSlot) ?? _hands.StanceMap;
        if (map == null || map.IsEmpty)
            return 0;

        return map.PickRandomKindId(AnimKindIds.MapAttack);
    }

    int FallbackSwingKind()
    {
        if (Stats.Get(Stat.MonsterData) != 0 && !_visual.HasWeaponAttractorBones())
            return AnimKindIds.UnarmedRSwing;
        return 0;
    }

    float ResolveSwingSpeed(Item weapon, int kind)
    {
        if (weapon == null || !weapon.TryGetStat(AnimKindIds.ItemAttackDelay, out int delay) || delay <= 0)
            return 1f;

        if (!_visual.TryResolveKind(kind, out int animId) || !_visual.TryGetAnimPlayer(out CatAnimPlayer player))
            return 1f;

        CatAnimRuntimeClip clip = player.EnsureClip(animId);
        if (clip == null || clip.NoteTimeMs <= 0f)
            return 1f;

        return UnityEngine.Mathf.Clamp(clip.NoteTimeMs / (delay * 10f), 1f, 2f);
    }

    void PlaySwingFallback()
    {
        if (_visual.HasCurrentAnim)
            return;

        _visual.PlayKindOnce(AnimKindIds.Wave, LocomotionAnimBlendSeconds, null, overlay: true);
    }

    void PlayFightEnter()
    {
        int startKind = ResolveFightStartKind(out string startName);
        if (startKind != 0)
            _visual.PlayKindOnce(startKind, LocomotionAnimBlendSeconds, null, overlay: true);
        else if (!string.IsNullOrEmpty(startName) && _visual.TryResolveKindName(startName, out int startId)
            && _visual.TryGetAnimPlayer(out CatAnimPlayer player))
        {
            player.PlayKindOnce(0, startId, LocomotionAnimBlendSeconds, null, overlay: true);
        }

        int idleKind = _animHolder.Idle;
        float delay = 0f;
        if (startKind != AnimKindIds.UnarmedStart
            && startKind != 0
            && _visual.TryResolveKind(startKind, out int startAnimId)
            && _visual.TryGetAnimPlayer(out CatAnimPlayer startPlayer))
        {
            CatAnimRuntimeClip startClip = startPlayer.EnsureClip(startAnimId);
            if (startClip != null)
                delay = UnityEngine.Mathf.Max(0f, startClip.GetOneShotDuration() - 0.3f);
        }

        _visual.PlayKindDelayed(idleKind, delay, LocomotionAnimBlendSeconds);
        _locomotionKind = 0;
    }

    void PlayFightLeave()
    {
        int stopKind = _hands.StanceItem == null ? AnimKindIds.UnarmedStop : 0;
        if (stopKind != 0)
            _visual.PlayKindOnce(stopKind, LocomotionAnimBlendSeconds, null, overlay: true);
        else
        {
            string stopName = AnimKindCatalog.StopNameForAnimSet(_hands.StanceAnimSet);
            if (!string.IsNullOrEmpty(stopName)
                && _visual.TryResolveKindName(stopName, out int stopId)
                && _visual.TryGetAnimPlayer(out CatAnimPlayer player))
            {
                player.PlayKindOnce(0, stopId, LocomotionAnimBlendSeconds, null, overlay: true);
            }
        }

        _locomotionKind = 0;
    }

    int ResolveFightStartKind(out string startName)
    {
        startName = null;
        if (_hands.StanceItem == null)
            return AnimKindIds.UnarmedStart;

        int animSet = _hands.StanceAnimSet;
        if (animSet == 3)
        {
            WeaponAnimListMap map = _hands.StanceMap;
            int draw = map != null ? map.PickRandomKindId(AnimKindIds.MapDraw) : 0;
            return draw != 0 ? draw : AnimKindIds.RifleStart;
        }

        startName = AnimKindCatalog.StartNameForAnimSet(animSet);
        return 0;
    }

    bool TryGetAnimPlayer(out CatAnimPlayer player)
        => _visual.TryGetAnimPlayer(out player);

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

        switch ((int)msg.Action)
        {
            case AnimKindIds.FightEnterAction:
                ApplyFightEnter(null);
                break;
            case AnimKindIds.FightLeaveAction:
                ApplyFightLeave();
                break;
            case AnimKindIds.AttackSwingAction:
                // TODO: play muzzle FX. Do not play the swing overlay or re-pick idle (AttackInfo owns that).
                break;
            default:
                if (msg.Action == CharacterActionType.StandUp)
                {
                    _motor.ApplyAction(MovementAction.LeaveSit);
                    HandleMotorStateChange();
                }
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

    public override bool TryGetIndicatorPosition(out UnityEngine.Vector3 worldPos)
    {
        if (_visual != null && _visual.TryGetIndicatorPosition(transform, out worldPos))
            return true;

        return base.TryGetIndicatorPosition(out worldPos);
    }

    public void MarkAppearanceStale() => _appearanceStale = true;

    public void UpdateAppearance()
    {
        // Character owns locomotion; don't let CatMeshLoader default to stand idle
        // (that races with SyncSitStateFromMotor and sticks seated chars in idle).
        _visual.RequestUpdateAppearance(playIdle: false);
        _locomotionLogicalName = null;
        _strafeOverlayLogicalName = null;
        _locomotionKind = 0;
        _strafeOverlayKind = 0;
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

        if (_motor.SuppressLocomotionPlay)
        {
            UpdateLocomotionPlaybackRate();
            return;
        }

        RefreshMoveSlots();
        if (!_motor.TryGetLocomotionKind(_animHolder, out int desiredKind)
            && _sitPhase == SitTransitionPhase.None)
        {
            UpdateLocomotionPlaybackRate();
            return;
        }

        // Shot is overlay-only. idle-rifle stays on the base channel; do not re-pick or re-Play it.
        if (player.HasLoopKeyPlaying(AnimKindIds.MapAttack) && desiredKind == _animHolder.Idle)
        {
            UpdateLocomotionPlaybackRate();
            return;
        }

        // Land is an overlay — keep base locomotion updating underneath.
        if (_jumpPhase == JumpAnimPhase.Landing)
        {
            if (_jumpLandLogicalName == "jump-land-idle"
                && _locomotionKind == _animHolder.Idle
                && desiredKind != 0
                && desiredKind != _animHolder.Idle)
            {
                FinishJumpLandKind(desiredKind, cancelOverlay: true);
                UpdateLocomotionPlaybackRate();
                return;
            }

            PlayLocomotionKind(player, desiredKind);
            SyncStrafeOverlay(player);
            UpdateLocomotionPlaybackRate();
            return;
        }

        if (_sitPhase == SitTransitionPhase.Exiting)
        {
            if (desiredKind == _animHolder.Idle)
            {
                UpdateLocomotionPlaybackRate();
                return;
            }

            CancelStandUpTransition();
        }

        PlayLocomotionKind(player, desiredKind);
        SyncStrafeOverlay(player);
        UpdateLocomotionPlaybackRate();
    }

    void PlayLocomotionKind(CatAnimPlayer player, int kind)
    {
        if (kind == 0)
            return;

        if (player == null && !_visual.TryGetAnimPlayer(out player))
            return;

        if (kind == _locomotionKind && player.CurrentKindId == kind)
            return;

        if (_visual.PlayKind(kind, LocomotionAnimBlendSeconds))
        {
            _locomotionKind = kind;
            _locomotionLogicalName = $"kind:{kind}";
        }
    }

    void SyncStrafeOverlay(CatAnimPlayer player)
    {
        int strafeKind = _sitPhase == SitTransitionPhase.None
            && _jumpPhase != JumpAnimPhase.Airborne
            ? _motor.GetStrafeOverlayKind()
            : 0;

        if (_strafeOverlayKind == strafeKind)
            return;

        if (strafeKind == 0)
        {
            player.CancelStrafe(LocomotionAnimBlendSeconds);
            _strafeOverlayKind = 0;
            _strafeOverlayLogicalName = null;
            return;
        }

        if (!_visual.TryResolveKind(strafeKind, out int animId)
            || !player.PlayStrafeKind(strafeKind, animId, LocomotionAnimBlendSeconds))
            return;

        _strafeOverlayKind = strafeKind;
        _strafeOverlayLogicalName = $"kind:{strafeKind}";
    }

    void ClearStrafeOverlay(CatAnimPlayer player, float blendSeconds = 0f)
    {
        _strafeOverlayLogicalName = null;
        _strafeOverlayKind = 0;
        player?.CancelStrafe(blendSeconds);
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
        {
            existing.CancelOverlay();
            ClearStrafeOverlay(existing);
        }

        _jumpPhase = JumpAnimPhase.Airborne;
        _jumpLandLogicalName = null;
        int takeoffKind = _motor.GetJumpTakeoffKind();
        _locomotionKind = takeoffKind;
        _locomotionLogicalName = $"kind:{takeoffKind}";
        if (!_visual.PlayKindOnce(takeoffKind, LocomotionAnimBlendSeconds, null, overlay: false))
        {
            if (takeoffKind == AnimKindIds.JumpForward
                && _visual.PlayKindOnce(AnimKindIds.JumpStand, LocomotionAnimBlendSeconds, null, overlay: false))
            {
                _locomotionKind = AnimKindIds.JumpStand;
                _locomotionLogicalName = $"kind:{AnimKindIds.JumpStand}";
                return;
            }

            _locomotionLogicalName = null;
            _locomotionKind = 0;
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

        RefreshAnimHolder();
        if (_motor.TryGetLocomotionKind(_animHolder, out int locoKind)
            && _visual.TryGetAnimPlayer(out CatAnimPlayer basePlayer))
            PlayLocomotionKind(basePlayer, locoKind);

        if (_visual.TryGetAnimPlayer(out CatAnimPlayer landPlayer))
            SyncStrafeOverlay(landPlayer);

        int landKind = _motor.GetJumpLandKind();
        _jumpPhase = JumpAnimPhase.Landing;
        _jumpLandLogicalName = landKind == 0 ? "jump-land-idle" : $"kind:{landKind}";
        if (landKind != 0
            && _visual.PlayKindOnce(landKind, LocomotionAnimBlendSeconds, OnJumpLandComplete, overlay: true))
            return;

        OnJumpLandComplete();
    }

    void OnJumpLandComplete()
    {
        // Overlay is already removed. Play again so arbitration writes a fresh mask.
        RefreshAnimHolder();
        _motor.TryGetLocomotionKind(_animHolder, out int kind);
        FinishJumpLandKind(kind, cancelOverlay: false);
    }

    void FinishJumpLandKind(int desiredKind, bool cancelOverlay)
    {
        _jumpPhase = JumpAnimPhase.None;
        _jumpLandLogicalName = null;

        if (!_visual.TryGetAnimPlayer(out CatAnimPlayer player))
            return;

        if (cancelOverlay)
            player.CancelOverlay();

        if (desiredKind == 0)
            desiredKind = _animHolder.Idle;

        _locomotionKind = 0;
        PlayLocomotionKind(player, desiredKind);
        SyncStrafeOverlay(player);
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
            ClearStrafeOverlay(player);
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
        if (_visual.TryGetAnimPlayer(out CatAnimPlayer player))
            ClearStrafeOverlay(player);
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
        RefreshAnimHolder();
        _locomotionKind = 0;
        if (_visual.TryGetAnimPlayer(out CatAnimPlayer standPlayer))
            PlayLocomotionKind(standPlayer, _animHolder.Idle);
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

        if (_motor.IsMoving && !IsIdlePlaybackLogicalName(_locomotionLogicalName))
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
