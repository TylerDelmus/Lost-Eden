using AOSharp.Common.GameData;
using Reflex.Attributes;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using UnityEngine;
using UnityEngine.UIElements;

[DisallowMultipleComponent]
[RequireComponent(typeof(UIDocument))]
public class WorldOverlayController : MonoBehaviour
{
    const string OverlayResourcePath = "UI/WorldOverlay";
    const string NameplateResourcePath = "UI/ScreenNameplate";
    const string HitIndicatorResourcePath = "UI/HitIndicator";
    const int SortOrder = 50;

    [Inject] PlayerController _playerController;
    [Inject] PlayfieldFactory _playfieldFactory;
    [Inject] NetworkClient _networkClient;

    UiMenu _menu;
    ScreenNameplateOverlay _nameplates;
    HitIndicatorOverlay _hits;
    Playfield _boundPlayfield;
    Dynel _healthSubscribedTarget;
    bool _started;

    void Awake()
    {
        _menu = UserInterface.Load(this, OverlayResourcePath, SortOrder, startVisible: true, logName: "WorldOverlay", stretchContentRoot: true, centerPanelRoot: false);
        if (_menu == null)
            return;

        ApplyOverlayPicking();
    }

    void ApplyOverlayPicking()
    {
        if (_menu?.Root != null)
            _menu.Root.pickingMode = PickingMode.Ignore;

        if (_menu?.Document?.rootVisualElement != null)
            _menu.Document.rootVisualElement.pickingMode = PickingMode.Ignore;
    }

    void Start()
    {
        _started = true;
        EnsureOverlays();

        if (_networkClient != null)
            _networkClient.AttackInfoReceived += OnAttackInfo;

        if (_playfieldFactory != null)
        {
            _playfieldFactory.CurrentPlayfieldChanged += OnCurrentPlayfieldChanged;
            BindPlayfield(_playfieldFactory.Current);
        }

        if (_playerController?.TargetingController != null)
            _playerController.TargetingController.TargetChanged += OnTargetChanged;
    }

    void OnDestroy()
    {
        if (_started)
        {
            if (_networkClient != null)
                _networkClient.AttackInfoReceived -= OnAttackInfo;

            if (_playfieldFactory != null)
                _playfieldFactory.CurrentPlayfieldChanged -= OnCurrentPlayfieldChanged;

            if (_playerController?.TargetingController != null)
                _playerController.TargetingController.TargetChanged -= OnTargetChanged;
        }

        UnsubscribeTargetHealth();
        BindPlayfield(null);
        _nameplates?.ClearAll();
        _hits?.ClearAll();

        if (_menu != null)
            UserInterface.Unregister(_menu);
    }

    void LateUpdate()
    {
        if (_nameplates == null && _hits == null)
            return;

        Camera camera = ResolveCamera();
        _nameplates?.Tick(camera);
        _hits?.Tick(camera);
    }

    void EnsureOverlays()
    {
        if (_menu == null)
            return;

        VisualElement root = _menu.Root;
        VisualElement nameplateRoot = root.Q<VisualElement>("NameplateOverlayRoot") ?? root;
        VisualElement hitRoot = root.Q<VisualElement>("HitIndicatorOverlayRoot") ?? root;

        if (_nameplates == null)
        {
            VisualTreeAsset nameplateAsset = UserInterface.LoadTemplate(NameplateResourcePath);
            if (nameplateAsset == null)
                Debug.LogError($"[WorldOverlay] Missing Resources/{NameplateResourcePath}");
            else
                _nameplates = new ScreenNameplateOverlay(nameplateAsset, nameplateRoot, _playerController);
        }

        if (_hits == null)
        {
            VisualTreeAsset hitAsset = UserInterface.LoadTemplate(HitIndicatorResourcePath);
            if (hitAsset == null)
                Debug.LogError($"[WorldOverlay] Missing Resources/{HitIndicatorResourcePath}");
            else
                _hits = new HitIndicatorOverlay(hitAsset, hitRoot);
        }
    }

    Camera ResolveCamera()
    {
        if (_playerController != null && _playerController.CameraController != null)
            return _playerController.CameraController.Camera;
        return null;
    }

    void OnCurrentPlayfieldChanged(Playfield playfield) => BindPlayfield(playfield);

    void BindPlayfield(Playfield playfield)
    {
        if (_boundPlayfield != null)
        {
            _boundPlayfield.DynelSpawned -= OnDynelSpawned;
            _boundPlayfield.DynelDespawned -= OnDynelDespawned;
        }

        _nameplates?.ClearAll();
        _hits?.ClearAll();
        UnsubscribeTargetHealth();

        _boundPlayfield = playfield;
        if (_boundPlayfield == null)
            return;

        _boundPlayfield.DynelSpawned += OnDynelSpawned;
        _boundPlayfield.DynelDespawned += OnDynelDespawned;
    }

    void OnDynelSpawned(Dynel dynel)
    {
        EnsureOverlays();
        if (_nameplates == null || dynel == null || !dynel.ShowNameplate)
            return;

        NameplateState state = NameplateState.HasLevel;
        if (dynel is Character
            && _playerController?.TargetingController?.CurrentTarget == dynel)
            state |= NameplateState.HealthVisible;

        _nameplates.ShowNameplate(dynel, dynel.Name, state, () => SelectDynel(dynel));

        if ((state & NameplateState.HealthVisible) != 0
            && _nameplates.TryGetNameplate(dynel, out ScreenNameplateView view))
            ScreenNameplateOverlay.RefreshHealthIfNeeded(view, dynel, state);
    }

    void OnDynelDespawned(Dynel dynel)
    {
        if (dynel == null)
            return;

        if (_healthSubscribedTarget == dynel)
            UnsubscribeTargetHealth();

        _nameplates?.HideNameplate(dynel);
    }

    void OnTargetChanged(Dynel target, TargetingController.TargetType type)
    {
        EnsureOverlays();
        UnsubscribeTargetHealth();

        if (_nameplates == null || target is not Character character)
            return;

        SubscribeTargetHealth(character);

        NameplateState state = NameplateState.HasLevel | NameplateState.HealthVisible;
        if (_nameplates.TryGetNameplate(character, out ScreenNameplateView view))
        {
            view.ApplyState(view.State | NameplateState.HealthVisible);
            ScreenNameplateOverlay.RefreshHealthIfNeeded(view, character, view.State);
        }
        else
        {
            _nameplates.ShowNameplate(character, character.Name, state, () => SelectDynel(character));
            if (_nameplates.TryGetNameplate(character, out view))
                ScreenNameplateOverlay.RefreshHealthIfNeeded(view, character, state);
        }
    }

    void SubscribeTargetHealth(Character target)
    {
        _healthSubscribedTarget = target;
        target.Stats.StatChanged += OnTargetStatChanged;
    }

    void UnsubscribeTargetHealth()
    {
        if (_healthSubscribedTarget != null)
        {
            _healthSubscribedTarget.Stats.StatChanged -= OnTargetStatChanged;

            if (_nameplates != null
                && _nameplates.TryGetNameplate(_healthSubscribedTarget, out ScreenNameplateView view))
            {
                view.ApplyState(view.State & ~NameplateState.HealthVisible);
            }
        }

        _healthSubscribedTarget = null;
    }

    void OnTargetStatChanged(Stat stat, int previousValue, int value, bool isInitialSet)
    {
        if (stat != Stat.Health && stat != Stat.MaxHealth)
            return;

        if (_healthSubscribedTarget == null || _nameplates == null)
            return;

        if (!_nameplates.TryGetNameplate(_healthSubscribedTarget, out ScreenNameplateView view))
            return;

        view.ApplyState(view.State | NameplateState.HealthVisible);
        ScreenNameplateOverlay.RefreshHealthIfNeeded(view, _healthSubscribedTarget, view.State);
    }

    void OnAttackInfo(AttackInfoMessage msg)
    {
        EnsureOverlays();
        if (_hits == null || msg == null || msg.Amount <= 0)
            return;

        if (_playfieldFactory == null || !_playfieldFactory.TryGetCharacter(msg.Target, out Character victim))
            return;

        bool isCrit = msg.HitType == HitType.Critical;
        _hits.OnObjectHit(victim, new HitIndicatorInfo(msg.Amount, isCrit), ResolveCamera());
    }

    void SelectDynel(Dynel dynel)
    {
        _playerController?.TargetingController?.SelectTarget(dynel);
    }
}
