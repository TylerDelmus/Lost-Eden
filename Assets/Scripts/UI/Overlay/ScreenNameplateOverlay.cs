using System.Collections.Generic;
using AOSharp.Common.GameData;
using UnityEngine;
using UnityEngine.UIElements;

internal class ScreenNameplateOverlay : ObjectPoolOverlay<ScreenNameplateView>
{
    readonly VisualTreeAsset _nameplateAsset;
    readonly Dictionary<Identity, ScreenNameplateView> _activeNameplates = new();
    readonly Dictionary<Identity, ScreenNameplateView> _hiddenNameplates = new();
    readonly List<Identity> _toRemove = new();
    readonly List<Identity> _toHide = new();
    readonly List<Identity> _toShow = new();
    readonly float _maxNameplateDistanceSq;

    readonly PlayerController _playerController;

    public ScreenNameplateOverlay(
        VisualTreeAsset nameplateAsset,
        VisualElement root,
        PlayerController playerController,
        float maxDistance = 20f) : base(root, defaultCapacity: 64)
    {
        _nameplateAsset = nameplateAsset;
        _playerController = playerController;
        _maxNameplateDistanceSq = maxDistance * maxDistance;
        MaxElements = 256;
    }

    internal override ScreenNameplateView CreateView()
    {
        return new ScreenNameplateView(_nameplateAsset);
    }

    public void ShowNameplate(Dynel dynel, string displayName, NameplateState state, System.Action onSelected = null)
    {
        if (dynel == null || string.IsNullOrEmpty(displayName))
            return;

        Identity id = dynel.Identity;

        if (_activeNameplates.TryGetValue(id, out ScreenNameplateView existingView)
            || _hiddenNameplates.TryGetValue(id, out existingView))
        {
            existingView.Dynel = dynel;
            existingView.UpdateContent(displayName);
            existingView.ApplyState(state);
            ApplyLevelIfNeeded(existingView, dynel, state);
            RefreshHealthIfNeeded(existingView, dynel, state);

            if (onSelected != null)
                existingView.SetSelectedCallback(onSelected);

            return;
        }

        ScreenNameplateView nameplateView = Pool.Get();
        nameplateView.Init(dynel, Offset);
        nameplateView.UpdateContent(displayName);
        nameplateView.ApplyState(state);
        ApplyLevelIfNeeded(nameplateView, dynel, state);
        RefreshHealthIfNeeded(nameplateView, dynel, state);

        if (onSelected != null)
            nameplateView.SetSelectedCallback(onSelected);

        _activeNameplates[id] = nameplateView;

        if (_playerController != null && _playerController.TryGetLocalPlayer(out Character local) && dynel == local)
            nameplateView.Root.style.display = DisplayStyle.None;
    }

    static void ApplyLevelIfNeeded(ScreenNameplateView view, Dynel dynel, NameplateState state)
    {
        if ((state & NameplateState.HasLevel) != 0)
            view.SetLevel(dynel.Stats.Get(Stat.Level));
    }

    public static void RefreshHealthIfNeeded(ScreenNameplateView view, Dynel dynel, NameplateState state)
    {
        if ((state & NameplateState.HealthVisible) == 0 || dynel is not Character)
            return;

        int health = dynel.Stats.Get(Stat.Health);
        int max = dynel.Stats.Get(Stat.MaxHealth);
        if (max <= 0)
            max = UnityEngine.Mathf.Max(health, 1);

        view.SetHealth(health, max);
    }

    public void HideNameplate(Dynel dynel)
    {
        if (dynel == null)
            return;

        HideNameplate(dynel.Identity);
    }

    public void HideNameplate(Identity identity)
    {
        ScreenNameplateView view = null;
        bool found = false;

        if (_activeNameplates.TryGetValue(identity, out view))
        {
            _activeNameplates.Remove(identity);
            found = true;
        }
        else if (_hiddenNameplates.TryGetValue(identity, out view))
        {
            _hiddenNameplates.Remove(identity);
            found = true;
        }

        if (found && view != null)
            Pool.Release(view);
    }

    public void ClearAll()
    {
        foreach (var kvp in _activeNameplates)
            Pool.Release(kvp.Value);
        _activeNameplates.Clear();

        foreach (var kvp in _hiddenNameplates)
            Pool.Release(kvp.Value);
        _hiddenNameplates.Clear();
    }

    public bool TryGetNameplate(Identity identity, out ScreenNameplateView nameplateView)
    {
        if (_activeNameplates.TryGetValue(identity, out nameplateView))
            return true;

        return _hiddenNameplates.TryGetValue(identity, out nameplateView);
    }

    public bool TryGetNameplate(Dynel dynel, out ScreenNameplateView nameplateView)
    {
        nameplateView = null;
        return dynel != null && TryGetNameplate(dynel.Identity, out nameplateView);
    }

    public void Tick(Camera camera)
    {
        _toRemove.Clear();
        _toHide.Clear();
        _toShow.Clear();

        Character localPlayer = null;
        bool hasLocal = _playerController != null && _playerController.TryGetLocalPlayer(out localPlayer);
        UnityEngine.Vector3 localPlayerPos = hasLocal ? localPlayer.Position : UnityEngine.Vector3.zero;
        Dynel currentTarget = _playerController?.TargetingController != null
            ? _playerController.TargetingController.CurrentTarget
            : null;

        foreach (var kvp in _activeNameplates)
        {
            if (!kvp.Value.IsValid())
            {
                _toRemove.Add(kvp.Key);
                continue;
            }

            bool isSelected = kvp.Value.Dynel == currentTarget;
            if (hasLocal && kvp.Value.Dynel != localPlayer
                && !ShouldShowNameplate(camera, localPlayerPos, kvp.Value.Dynel, isSelected))
            {
                _toHide.Add(kvp.Key);
                continue;
            }

            if ((kvp.Value.State & NameplateState.Disabled) != 0)
                kvp.Value.SetDisabled(!isSelected);

            kvp.Value.UpdatePos(camera);
        }

        foreach (var kvp in _hiddenNameplates)
        {
            if (!kvp.Value.IsValid())
            {
                _toRemove.Add(kvp.Key);
                continue;
            }

            bool isSelected = kvp.Value.Dynel == currentTarget;
            if (!hasLocal || kvp.Value.Dynel == localPlayer
                || ShouldShowNameplate(camera, localPlayerPos, kvp.Value.Dynel, isSelected))
                _toShow.Add(kvp.Key);
        }

        for (int i = 0; i < _toRemove.Count; i++)
            HideNameplate(_toRemove[i]);

        for (int i = 0; i < _toHide.Count; i++)
        {
            Identity id = _toHide[i];
            if (!_activeNameplates.TryGetValue(id, out ScreenNameplateView view))
                continue;

            _activeNameplates.Remove(id);
            _hiddenNameplates[id] = view;
            view.Root.style.display = DisplayStyle.None;
        }

        for (int i = 0; i < _toShow.Count; i++)
        {
            Identity id = _toShow[i];
            if (!_hiddenNameplates.TryGetValue(id, out ScreenNameplateView view))
                continue;

            _hiddenNameplates.Remove(id);
            _activeNameplates[id] = view;
            view.UpdatePos(camera);
            view.Root.style.display = DisplayStyle.Flex;

            if ((view.State & NameplateState.Disabled) != 0)
                view.SetDisabled(view.Dynel != currentTarget);
        }

        if (hasLocal && _activeNameplates.TryGetValue(localPlayer.Identity, out ScreenNameplateView localView))
        {
            bool isTargeted = currentTarget == localPlayer;
            localView.Root.style.display = isTargeted ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    bool ShouldShowNameplate(Camera camera, UnityEngine.Vector3 localPlayerPos, Dynel dynel, bool isSelected)
    {
        if (camera == null || !dynel.LineOfSightFrom(camera.transform.position))
            return false;

        if (isSelected)
            return true;

        float sqr = (localPlayerPos - dynel.Position).sqrMagnitude;
        return sqr <= _maxNameplateDistanceSq;
    }
}
