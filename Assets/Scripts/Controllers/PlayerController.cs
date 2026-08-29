using System;
using Reflex.Attributes;
using SmokeLounge.AOtomation.Messaging.GameData;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using UnityEngine;
using MovementAction = AOSharp.Common.GameData.MovementAction;
using MovementState = AOSharp.Common.GameData.MovementState;

[RequireComponent(typeof(InputController))]
[DefaultExecutionOrder(-100)]
public class PlayerController : MonoBehaviour
{
    static readonly (MovementFlags Flag, MovementAction Start, MovementAction Stop)[] FlagActions =
    {
        (MovementFlags.Forward, MovementAction.ForwardStart, MovementAction.ForwardStop),
        (MovementFlags.Backward, MovementAction.BackwardStart, MovementAction.BackwardStop),
        (MovementFlags.StrafeLeft, MovementAction.StrafeLeftStart, MovementAction.StrafeLeftStop),
        (MovementFlags.StrafeRight, MovementAction.StrafeRightStart, MovementAction.StrafeRightStop),
        (MovementFlags.TurnLeft, MovementAction.TurnLeftStart, MovementAction.TurnLeftStop),
        (MovementFlags.TurnRight, MovementAction.TurnRightStart, MovementAction.TurnRightStop),
        (MovementFlags.Jump, MovementAction.JumpStart, MovementAction.JumpStop),
    };

    const MovementFlags NetworkFlags =
        MovementFlags.Forward | MovementFlags.Backward |
        MovementFlags.StrafeLeft | MovementFlags.StrafeRight |
        MovementFlags.TurnLeft | MovementFlags.TurnRight |
        MovementFlags.Jump;

    [SerializeField]
    private InputController _inputController;

    [SerializeField]
    internal CameraController CameraController;

    [SerializeField]
    internal TargetingController TargetingController;

    [Inject] NetworkClient _networkClient;

    private Quaternion _lastSentRotation;
    private MovementFlags _lastSentFlags;

    public Action<Collider> OnInteraction;


    internal bool IsLocalPlayer(Character character) => _localPlayer != null && _localPlayer == character;

    internal bool TryGetLocalPlayer(out Character localPlayer) => (localPlayer = _localPlayer) != null;

    private Character _localPlayer;

    private void Start()
    {
        _inputController.InteractPressed += OnInteractPress;
        _inputController.SelfTargetPressed += OnSelfTargetPress;
        _inputController.TabPressed += OnTabPress;
        _inputController.HotbarPressed += OnHotbarPress;
        _inputController.CharacterPressed += OnCharacterPress;
        _inputController.SitPressed += OnSitPress;
        _inputController.CancelPressed += OnCancelPress;
    }

    private void OnCharacterPress()
    {
        //UserInterface.Instance.ToggleCharacterPanel();
    }

    private void OnInteractPress()
    {
        if (_localPlayer == null)
            return;
    }

    private void Update()
    {
        if (_localPlayer == null)
            return;

        CameraController.SetInputs(_inputController.ActorInput);
        TargetingController.Tick(_inputController.ActorInput);

        var cameraYaw = Quaternion.AngleAxis(CameraController.GetViewAngles().y, Vector3.up);
        var flags = _inputController.ActorInput.ToMovementFlags();
        _localPlayer.Motor.SetInputs(flags, cameraYaw);

        // Network heading is character facing, not camera look (they diverge without mouse-turn).
        var facing = Quaternion.AngleAxis(_localPlayer.transform.eulerAngles.y, Vector3.up);
        SyncMovementToServer(flags, facing);
    }

    void SyncMovementToServer(MovementFlags flags, Quaternion rotation)
    {
        if (_networkClient == null || !_networkClient.InPlay)
            return;

        if (_localPlayer.Motor.State == MovementState.Sit)
            return;

        MovementFlags networkFlags = flags & NetworkFlags;
        MovementFlags changed = networkFlags ^ _lastSentFlags;

        // Stops first, then starts — e.g. Forward+StrafeLeft becomes two CharDCMoves.
        if (changed != MovementFlags.None)
        {
            for (int i = 0; i < FlagActions.Length; i++)
            {
                var (flag, _, stop) = FlagActions[i];
                if ((changed & flag) == 0)
                    continue;
                if ((networkFlags & flag) == 0)
                    SendMove(stop, rotation);
            }

            for (int i = 0; i < FlagActions.Length; i++)
            {
                var (flag, start, _) = FlagActions[i];
                if ((changed & flag) == 0)
                    continue;
                if ((networkFlags & flag) != 0)
                    SendMove(start, rotation);
            }

            _lastSentFlags = networkFlags;
        }

        bool mouseTurn = (flags & MovementFlags.MouseTurn) != 0;
        if (mouseTurn && rotation != _lastSentRotation)
        {
            SendMove(MovementAction.Update, rotation);
            _lastSentRotation = rotation;
        }
        else if (!mouseTurn)
        {
            _lastSentRotation = rotation;
        }
    }

    void SendMove(MovementAction action, Quaternion rotation)
    {
        _networkClient.Send(new CharDCMoveMessage
        {
            MoveType = action,
            Position = _localPlayer.Position.ToAo(),
            Heading = rotation.ToAo(),
        });
    }

    void SendCharacterAction(CharacterActionType action)
    {
        _networkClient.Send(new CharacterActionMessage { Action = action });
    }

    void OnSitPress()
    {
        if (_localPlayer == null || _networkClient == null || !_networkClient.InPlay)
            return;

        var facing = Quaternion.AngleAxis(_localPlayer.transform.eulerAngles.y, Vector3.up);
        var motor = _localPlayer.Motor;

        if (motor.State == MovementState.Sit)
        {
            motor.ApplyAction(MovementAction.LeaveSit);
            SendCharacterAction(CharacterActionType.StandUp);
            return;
        }

        motor.ApplyAction(MovementAction.SwitchToSit);
        SendMove(MovementAction.SwitchToSit, facing);
        _lastSentFlags = MovementFlags.None;
    }

    internal void SetLocalPlayer(Character player)
    {
        _localPlayer = player;
        _lastSentFlags = MovementFlags.None;
        _lastSentRotation = player != null ? player.transform.rotation : Quaternion.identity;
        // _localPlayer.Nameplate.Hide();
        CameraController.SetTarget(_localPlayer);
        TargetingController.Initialize(_localPlayer);
        TargetingController.TargetChanged += OnTargetChanged;
    }

    private void OnTargetChanged(Dynel dynel, TargetingController.TargetType type)
    {
        if (dynel == null)
            return;

        //if (type == TargetingController.TargetType.Interact && dynel is Character character)
        //    _localPlayer.StartAttackCmd(character);
    }

    private void OnSelfTargetPress()
    {
        if (_localPlayer == null)
            return;

        TargetingController.SelectSelf();
    }

    private void OnTabPress()
    {
        if (_localPlayer == null)
            return;

        TargetingController.SelectNextClosest();
    }

    private void OnCancelPress()
    {
        if (_localPlayer == null)
            return;

        //if (_localPlayer.FightingTarget != null)
        //    _localPlayer.StopAttackCmd();

        TargetingController.ClearTarget();
    }

    private void OnHotbarPress(int slot)
    {
        if (_localPlayer == null)
            return;

        Debug.Log($"[Hotbar] Slot {slot} pressed");

        //DEV
        //if (slot == 1)
        //    _localPlayer.CastSpellCmd(1001);
        //DEV
    }

    private void OnDestroy()
    {
        _inputController.InteractPressed -= OnInteractPress;
        _inputController.SelfTargetPressed -= OnSelfTargetPress;
        _inputController.TabPressed -= OnTabPress;
        _inputController.HotbarPressed -= OnHotbarPress;
        _inputController.SitPressed -= OnSitPress;
        _inputController.CancelPressed -= OnCancelPress;
    }
}
