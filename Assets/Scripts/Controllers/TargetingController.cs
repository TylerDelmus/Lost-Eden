using System;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(-150)]
public class TargetingController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera _camera;

    [Header("Targeting")]
    [SerializeField] private float _maxTargetDistance = 50f;

    public Character LocalPlayer { get; private set; }

    public Dynel HoverTarget { get; private set; }
    public Dynel CurrentTarget { get; private set; }

    public enum TargetType
    {
        None,
        Select,
        Interact
    }

    public TargetType CurrentTargetType { get; private set; }

    public event Action<Dynel, TargetType> TargetChanged;
    public event Action<Dynel> HoverChanged;

    private bool _isInCombat;
    private Dynel _previousFightingTarget;
    private Dynel _previousTarget;

    public void Initialize(Character localPlayer)
    {
        LocalPlayer = localPlayer;
        LocalPlayer.CombatStarted += OnCombatStarted;
        LocalPlayer.CombatEnded += OnCombatEnded;
    }

    private void OnDestroy()
    {
        if (LocalPlayer != null)
        {
            LocalPlayer.CombatStarted -= OnCombatStarted;
            LocalPlayer.CombatEnded -= OnCombatEnded;
        }
    }

    public void Tick(ActorInput input)
    {
        UpdateHover();

        if (input.LeftClickReleasedAtOrigin)
            TryClickTarget(TargetType.Select);

        if (input.RightClickReleasedAtOrigin)
            TryClickTarget(TargetType.Interact);
    }

    // --------------------------------------------------
    // Hover
    // --------------------------------------------------

    private void UpdateHover()
    {
        Dynel newHover = RaycastDynel();

        if (newHover == HoverTarget)
            return;

        HoverTarget = newHover;

        if (HoverTarget == null)
        {
            CursorController.Instance.SetCursor(CursorState.Default);
        }
        else
        {
            //if (newHover is Character hoverChar && LocalPlayer.CanAttack(hoverChar))
            //    CursorController.Instance.SetCursor(CursorState.Combat);
            //else if (HoverTarget is PickupItem)
            //    CursorController.Instance.SetCursor(CursorState.Pickup);
        }

        HoverChanged?.Invoke(HoverTarget);
    }

    // --------------------------------------------------
    // Target Selection
    // --------------------------------------------------

    private void TryClickTarget(TargetType targetType)
    {
        Debug.Log($"TryClickTarget: {HoverTarget}");
        if (HoverTarget == null)
            return;

        if (!CanTarget(HoverTarget))
            return;

        SetTarget(HoverTarget, targetType);
    }

    public void SelectTarget(Dynel target)
    {
        SetTarget(target, TargetType.Select);
    }

    private void SetTarget(Dynel target, TargetType targetType)
    {
        if (CurrentTarget == target && CurrentTargetType == targetType)
            return;

        //if (CurrentTarget is Character oldChar)
        //    oldChar.SetNameplateTargeted(false);

        CurrentTarget = target;
        CurrentTargetType = targetType;
        //LocalPlayer.SetTargetCmd(target);

        //if (target is Character newChar)
        //    newChar.SetNameplateTargeted(true);

        Debug.Log($"Target {targetType} set to: {target}");

        TargetChanged?.Invoke(target, targetType);
    }

    // --------------------------------------------------
    // Combat State Events
    // --------------------------------------------------

    private void OnCombatStarted()
    {
        _isInCombat = true;
        _previousFightingTarget = LocalPlayer.FightingTarget;
        
        // Nameplate colors are now handled by the NameplateState system in Character class
    }

    private void OnCombatEnded()
    {
        _isInCombat = false;
        _previousFightingTarget = null;
        
        // Nameplate colors are now handled by the NameplateState system in Character class
    }


    // --------------------------------------------------
    // Raycasting & Validation
    // --------------------------------------------------

    private Dynel RaycastDynel()
    {
        if (_camera == null)
            return null;

        Ray ray = _camera.ScreenPointToRay(Mouse.current.position.ReadValue());

        if (!Physics.Raycast(ray, out RaycastHit hit, _maxTargetDistance, GameLayers.DynelMask, QueryTriggerInteraction.Ignore))
            return null;

        return hit.collider.GetComponentInParent<Dynel>();
    }

    private bool CanTarget(Dynel dynel)
    {
        if (dynel == null)
            return false;

        return true;
    }

    public void ClearTarget()
    {
        if (CurrentTarget == null)
            return;

        //if (CurrentTarget is Character oldChar)
        //    oldChar.SetNameplateTargeted(false);

        CurrentTarget = null;
        CurrentTargetType = TargetType.None;
        //LocalPlayer.SetTargetCmd(null);

        TargetChanged?.Invoke(null, TargetType.None);
    }

    // --------------------------------------------------
    // Keyboard Targeting
    // --------------------------------------------------

    public void SelectSelf()
    {
        if (LocalPlayer == null)
            return;

        if (CurrentTarget == LocalPlayer)
        {
            var revert = _previousTarget;
            _previousTarget = LocalPlayer;
            if (revert != null && revert != LocalPlayer)
                SetTarget(revert, TargetType.Select);
            else
                ClearTarget();
        }
        else
        {
            _previousTarget = CurrentTarget;
            SetTarget(LocalPlayer, TargetType.Select);
        }
    }

    public void SelectNextClosest()
    {
        // if (LocalPlayer == null)
        //     return;

        // var reference = CurrentTarget != null ? CurrentTarget : LocalPlayer;

        // var candidates = LocalPlayer
        //     .GetDynelsInRadius<Character>(_maxTargetDistance)
        //     .Where(c => c != LocalPlayer)
        //     .OrderBy(c => c.DistanceFrom(reference))
        //     .ToList();

        // if (candidates.Count == 0)
        //     return;

        // if (CurrentTarget is Character currentChar && candidates.Contains(currentChar))
        // {
        //     int index = candidates.IndexOf(currentChar);
        //     SetTarget(candidates[(index + 1) % candidates.Count], TargetType.Select);
        // }
        // else
        // {
        //     SetTarget(candidates[0], TargetType.Select);
        // }
    }
}