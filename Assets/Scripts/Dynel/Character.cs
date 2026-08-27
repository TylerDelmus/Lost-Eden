using AOSharp.Common.GameData;
using Reflex.Attributes;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using UnityEngine;

[RequireComponent(typeof(CharacterMotor))]
public class Character : Dynel
{
    const float LocomotionAnimBlendSeconds = CatAnimPlayer.DefaultBlendSeconds;

    [Inject] CatMeshLoader _catMeshLoader;

    CharacterMotor _motor;
    GameObject _visualRoot;
    int _loadedMonsterDataId;
    string _locomotionLogicalName;

    void Awake()
    {
        _motor = GetComponent<CharacterMotor>();
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
        UpdateLocomotionAnim();
    }

    void OnStatChanged(Stat stat, int previousValue, int value, bool isInitialSet)
    {
        if (stat == Stat.MonsterData)
        {
            ApplyMonsterVisual(value);
            return;
        }

        if (stat == Stat.AnimSet)
        {
            if (TryGetAnimPlayer(out CatAnimPlayer player))
                player.SetAnimSet(value);
        }
    }

    public override void Apply(SimpleCharFullUpdateMessage msg)
    {
        base.Apply(msg);
        _motor.Warp(transform.position, transform.rotation);
        ApplyMonsterVisual(Stats.Get(Stat.MonsterData));
    }

    public void Apply(CharDCMoveMessage msg)
    {
        _motor.Warp(
            new UnityEngine.Vector3(msg.Position.X, msg.Position.Y, msg.Position.Z),
            new UnityEngine.Quaternion(msg.Heading.X, msg.Heading.Y, msg.Heading.Z, msg.Heading.W));
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
        {
            var p = pathInfo.Waypoints[i];
            waypoints[i] = new UnityEngine.Vector3(p.X, p.Y, p.Z);
        }

        _motor.SetPath(waypoints);
    }

    public bool Play(string logicalName, float blendSeconds = LocomotionAnimBlendSeconds)
    {
        if (!TryGetAnimPlayer(out CatAnimPlayer player))
            return false;

        bool played = player.Play(logicalName, blendSeconds);
        if (played)
            _locomotionLogicalName = logicalName?.Trim().ToLowerInvariant();

        return played;
    }

    void ApplyMonsterVisual(int monsterDataId)
    {
        int animSet = Stats.Get(Stat.AnimSet);
        _catMeshLoader?.ApplyMonsterVisual(
            transform,
            monsterDataId,
            animSet,
            ref _visualRoot,
            ref _loadedMonsterDataId);

        _locomotionLogicalName = null;
        UpdateLocomotionAnim();
    }

    void UpdateLocomotionAnim()
    {
        if (_visualRoot == null || _motor == null)
            return;

        string desired = _motor.IsMoving ? "run" : "idle";
        if (string.Equals(_locomotionLogicalName, desired, System.StringComparison.Ordinal))
            return;

        if (!TryGetAnimPlayer(out CatAnimPlayer player))
            return;

        if (player.Play(desired, LocomotionAnimBlendSeconds))
            _locomotionLogicalName = desired;
    }

    bool TryGetAnimPlayer(out CatAnimPlayer player)
    {
        player = null;
        if (_visualRoot == null)
            return false;

        if (_visualRoot.TryGetComponent(out CatMeshVisualHolder holder) && holder.Player != null)
        {
            player = holder.Player;
            return true;
        }

        return _visualRoot.TryGetComponent(out player);
    }
}
