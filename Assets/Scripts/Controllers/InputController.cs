using Reflex.Attributes;
using System;
using UnityEngine;
using UnityEngine.InputSystem;

public readonly struct ActorInput
{
    private readonly ActorInputRaw _raw;
    internal readonly bool IsSet => _raw != null;

    internal ActorInput(ActorInputRaw actorInputRaw)
    {
        _raw = actorInputRaw;
    }

    internal readonly Vector3 MoveInput => new Vector3(_raw.MoveAxisRaw.x, 0, _raw.MoveAxisRaw.y);
    internal readonly Vector3 LookInput => new Vector3(_raw.LookAxis.x, _raw.LookAxis.y, 0);
    internal readonly bool LeftClickHeld => _raw.IsLeftClickHeld;
    internal readonly bool LeftClickPressed => _raw.IsLeftClickPressedThisFrame;
    internal readonly bool LeftClickReleasedAtOrigin => _raw.IsLeftClickReleasedAtOrigin;
    internal readonly bool RightClickHeld => _raw.IsRightClickHeld;
    internal readonly bool RightClickPressed => _raw.IsRightClickPressedThisFrame;
    internal readonly bool RightClickReleasedAtOrigin => _raw.IsRightClickReleasedAtOrigin;
    internal readonly bool IsJumping => _raw.Jumped;
    public readonly bool IsMoving => IsSet && MoveInput != Vector3.zero;
    public readonly bool IsStrafing => IsSet && Mathf.Abs(MoveInput.x) > 0f;
    public readonly bool IsMovingForward => IsSet && Mathf.Abs(MoveInput.z) > 0f;
    internal readonly float ZoomDelta => _raw.ZoomDelta;
    internal readonly float StrafeInput => _raw.StrafeAxisRaw;

    internal readonly MovementFlags ToMovementFlags()
    {
        var flags = MovementFlags.None;
        Vector3 moveInput = MoveInput;

        if (moveInput.z > 0)
            flags |= MovementFlags.Forward;
        else if (moveInput.z < 0)
            flags |= MovementFlags.Backward;

        if (RightClickHeld)
        {
            if (moveInput.x < 0)
                flags |= MovementFlags.StrafeLeft;
            else if (moveInput.x > 0)
                flags |= MovementFlags.StrafeRight;

            flags |= MovementFlags.MouseTurn;
        }
        else
        {
            if (moveInput.x < 0)
                flags |= MovementFlags.TurnLeft;
            else if (moveInput.x > 0)
                flags |= MovementFlags.TurnRight;
        }

        // Q/E dedicated strafe — always strafes regardless of right-click
        if (StrafeInput < 0)
            flags |= MovementFlags.StrafeLeft;
        else if (StrafeInput > 0)
            flags |= MovementFlags.StrafeRight;

        if (IsJumping)
            flags |= MovementFlags.Jump;

        return flags;
    }
}

internal class ActorInputRaw
{
    internal Vector2 LookAxisRaw = Vector2.zero;
    internal Vector2 LookAxis = Vector2.zero;
    internal Vector2 MoveAxisRaw = Vector2.zero;
    internal bool IsLeftClickHeld;
    internal bool IsLeftClickPressedThisFrame;
    internal bool IsLeftClickReleasedAtOrigin;
    internal Vector2 LeftClickOrigin;
    internal bool IsRightClickHeld;
    internal bool IsRightClickPressedThisFrame;
    internal bool IsRightClickReleasedAtOrigin;
    internal Vector2 RightClickOrigin;
    internal bool InteractPressed;
    internal bool Jumped;
    internal float ZoomDelta;
    internal float StrafeAxisRaw;
}

[DefaultExecutionOrder(-200)]
internal class InputController : MonoBehaviour
{
    [SerializeField]
    private InputActionAsset _inputAction;

    [SerializeField]
    private Vector2 _lookSensitivity = new Vector2(2, 2);

    internal ActorInput ActorInput => new ActorInput(_actorInputRaw);
    private ActorInputRaw _actorInputRaw;

    private InputAction _moveAction;
    private InputAction _lookAction;
    private InputAction _jumpAction;
    private InputAction _interactAction;
    private InputAction _leftClickAction;
    private InputAction _rightClickAction;
    private InputAction _selfTargetAction;
    private InputAction _tabAction;
    private InputAction _hotbarAction;
    private InputAction _zoomAction;
    private InputAction _cancelAction;
    private InputAction _strafeAction;
    private InputAction _characterAction;

    public Action CharacterPressed;
    public Action InteractPressed;
    public Action SelfTargetPressed;
    public Action TabPressed;
    public Action<int> HotbarPressed;
    public Action CancelPressed;

    [Inject]
    private IUINotifyService _uiNotifyService;

    private void Awake()
    {
        _actorInputRaw = new ActorInputRaw();

        _moveAction = InputSystem.actions.FindAction("Move");
        _lookAction = InputSystem.actions.FindAction("Look");
        _jumpAction = InputSystem.actions.FindAction("Jump");
        _interactAction = InputSystem.actions.FindAction("Interact");
        _leftClickAction = InputSystem.actions.FindAction("LeftClick");
        _rightClickAction = InputSystem.actions.FindAction("RightClick");
        _selfTargetAction = InputSystem.actions.FindAction("SelfTarget");
        _tabAction = InputSystem.actions.FindAction("Tab");
        _hotbarAction = InputSystem.actions.FindAction("HotbarAction");
        _zoomAction = InputSystem.actions.FindAction("Zoom");
        _cancelAction = InputSystem.actions.FindAction("Cancel");
        _strafeAction = InputSystem.actions.FindAction("Strafe");
        _characterAction = InputSystem.actions.FindAction("Character");
       
        _characterAction.performed += OnCharacterPerformed;

        _jumpAction.performed += OnJumpPerformed;
        _jumpAction.canceled += OnJumpCanceled;
        _interactAction.performed += OnInteractPerformed;

        _leftClickAction.started += OnLeftClickStarted;
        _leftClickAction.performed += OnLeftClickPerformed;
        _leftClickAction.canceled += OnLeftClickCanceled;

        _rightClickAction.started += OnRightClickStarted;
        _rightClickAction.performed += OnRightClickPerformed;
        _rightClickAction.canceled += OnRightClickCanceled;

        _selfTargetAction.performed += OnSelfTargetPerformed;
        _tabAction.performed += OnTabPerformed;
        _hotbarAction.performed += OnHotbarPerformed;
        _cancelAction.performed += OnCancelPerformed;
    }
    private void OnCharacterPerformed(InputAction.CallbackContext ctx)
    {
        CharacterPressed?.Invoke();
    }

    private void OnJumpPerformed(InputAction.CallbackContext ctx)
    {
        _actorInputRaw.Jumped = true;
    }

    private void OnJumpCanceled(InputAction.CallbackContext ctx)
    {
        _actorInputRaw.Jumped = false;
    }

    private void OnInteractPerformed(InputAction.CallbackContext ctx)
    {
        InteractPressed?.Invoke();
    }

    private void OnSelfTargetPerformed(InputAction.CallbackContext ctx)
    {
        SelfTargetPressed?.Invoke();
    }

    private void OnTabPerformed(InputAction.CallbackContext ctx)
    {
        TabPressed?.Invoke();
    }

    private void OnCancelPerformed(InputAction.CallbackContext ctx)
    {
        CancelPressed?.Invoke();
    }

    private void OnHotbarPerformed(InputAction.CallbackContext ctx)
    {
        string keyName = ctx.control.name;
        int slot;

        if (keyName == "0")
            slot = 10;
        else if (int.TryParse(keyName, out int parsed) && parsed >= 1 && parsed <= 9)
            slot = parsed;
        else
            return;

        HotbarPressed?.Invoke(slot);
    }

    private void OnEnable()
    {
        _inputAction.FindActionMap("Player").Enable();
        _inputAction.FindActionMap("UI").Disable();
    }

    private void OnDisable()
    {
        _inputAction.FindActionMap("Player").Disable();
        _inputAction.FindActionMap("UI").Disable();
    }

    private void Update()
    {
        UpdateInput(_actorInputRaw);
    }

    private void LateUpdate()
    {
        _actorInputRaw.IsLeftClickPressedThisFrame = false;
        _actorInputRaw.IsRightClickPressedThisFrame = false;
        _actorInputRaw.IsLeftClickReleasedAtOrigin = false;
        _actorInputRaw.IsRightClickReleasedAtOrigin = false;
    }

    private void UpdateInput(ActorInputRaw actorInput)
    {
        actorInput.MoveAxisRaw = _moveAction.ReadValue<Vector2>();
        actorInput.LookAxisRaw = 0.1f * 0.5f * _lookAction.ReadValue<Vector2>(); // legacy scaling compensation
        actorInput.LookAxis = _lookSensitivity * actorInput.LookAxisRaw;
        actorInput.ZoomDelta = _zoomAction.ReadValue<Vector2>().y;
        actorInput.StrafeAxisRaw = _strafeAction.ReadValue<float>();
    }

    private void OnLeftClickStarted(InputAction.CallbackContext ctx)
    {
        _actorInputRaw.IsLeftClickPressedThisFrame = true;
        _actorInputRaw.IsLeftClickHeld = true;
        _actorInputRaw.LeftClickOrigin = Mouse.current.position.ReadValue();
        HideCursor();
    }

    private void OnLeftClickPerformed(InputAction.CallbackContext ctx)
    {
        _actorInputRaw.IsLeftClickHeld = true;
    }

    private void OnLeftClickCanceled(InputAction.CallbackContext ctx)
    {
        _actorInputRaw.IsLeftClickHeld = false;
        _actorInputRaw.IsLeftClickReleasedAtOrigin = Mouse.current.position.ReadValue() == _actorInputRaw.LeftClickOrigin;
        ShowCursor();
    }

    private void OnRightClickStarted(InputAction.CallbackContext ctx)
    {
        _actorInputRaw.IsRightClickPressedThisFrame = true;
        _actorInputRaw.IsRightClickHeld = true;
        _actorInputRaw.RightClickOrigin = Mouse.current.position.ReadValue();
        HideCursor();
    }

    private void OnRightClickPerformed(InputAction.CallbackContext ctx)
    {
        _actorInputRaw.IsRightClickHeld = true;
    }

    private void OnRightClickCanceled(InputAction.CallbackContext ctx)
    {
        _actorInputRaw.IsRightClickHeld = false;
        _actorInputRaw.IsRightClickReleasedAtOrigin = Mouse.current.position.ReadValue() == _actorInputRaw.RightClickOrigin;
        ShowCursor();
    }

    private Vector2 _savedCursorPosition;

    private void HideCursor()
    {
        if (!_uiNotifyService.IsInteractingWithUI && Cursor.visible)
        {
            _savedCursorPosition = Mouse.current.position.ReadValue();
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Confined;
            _uiNotifyService.NotifyGameDragStart();
        }
    }

    private void ShowCursor()
    {
        if (!Cursor.visible)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Mouse.current.WarpCursorPosition(_savedCursorPosition);
            _uiNotifyService.NotifyGameDragEnd();
        }
    }

    public void SwitchToPlayer()
    {
        _inputAction.FindActionMap("Player").Enable();
        _inputAction.FindActionMap("UI").Disable();
    }

    public void SwitchToUI()
    {
        _inputAction.FindActionMap("UI").Enable();
        _inputAction.FindActionMap("Player").Disable();
    }

    private void OnDestroy()
    {
        _jumpAction.performed -= OnJumpPerformed;
        _jumpAction.canceled -= OnJumpCanceled;
        _interactAction.performed -= OnInteractPerformed;

        _leftClickAction.started -= OnLeftClickStarted;
        _leftClickAction.performed -= OnLeftClickPerformed;
        _leftClickAction.canceled -= OnLeftClickCanceled;

        _rightClickAction.started -= OnRightClickStarted;
        _rightClickAction.performed -= OnRightClickPerformed;
        _rightClickAction.canceled -= OnRightClickCanceled;

        _selfTargetAction.performed -= OnSelfTargetPerformed;
        _tabAction.performed -= OnTabPerformed;
        _hotbarAction.performed -= OnHotbarPerformed;
        _cancelAction.performed -= OnCancelPerformed;

        _characterAction.performed -= OnCharacterPerformed;
    }
}