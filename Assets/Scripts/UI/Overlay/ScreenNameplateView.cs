using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

public class ScreenNameplateView : TrackTransformView
{
    internal Dynel Dynel;

    readonly Label _nameLabel;
    readonly Label _levelLabel;
    readonly VisualElement _healthBar;
    readonly VisualElement _healthFill;

    System.Action _onSelected;
    float _lastHealth = -1f;
    float _lastMaxHealth = -1f;

    static readonly StateColorEntry[] StateColors =
    {
        new(NameplateState.InCombat, Color.red),
        new(NameplateState.ItemRarity_Uncommon, new Color(0.12f, 0.87f, 0.12f)),
        new(NameplateState.ItemRarity_Common, Color.white),
        new(NameplateState.ItemRarity_Rare, new Color(0.25f, 0.5f, 1f)),
        new(NameplateState.ItemRarity_Epic, new Color(0.64f, 0.21f, 0.93f)),
        new(NameplateState.ItemRarity_Legendary, new Color(1f, 0.5f, 0f)),
    };

    internal ScreenNameplateView(VisualTreeAsset asset) : base(asset)
    {
        _nameLabel = Root.Q<Label>("Name");
        _levelLabel = Root.Q<Label>("Level");
        _healthBar = Root.Q<VisualElement>("HealthBar");
        _healthFill = Root.Q<VisualElement>("HealthFill");

        if (_healthBar != null)
            _healthBar.style.display = DisplayStyle.None;

        if (_levelLabel != null)
            _levelLabel.style.display = DisplayStyle.None;

        Root.RegisterCallback<PointerDownEvent>(OnClick);
    }

    internal void Init(Dynel dynel, Vector3 offset)
    {
        Dynel = dynel;
        base.Init(offset);
        _lastHealth = -1f;
        _lastMaxHealth = -1f;
    }

    internal override void UpdatePos(Camera camera)
    {
        if (Dynel == null || Dynel.gameObject == null)
            return;

        if (!Dynel.TryGetIndicatorPosition(out Vector3 worldPos))
            worldPos = Dynel.transform.position;

        UpdatePos(worldPos, camera);
    }

    internal void UpdateContent(string displayName)
    {
        if (_nameLabel != null)
            _nameLabel.text = displayName ?? string.Empty;
    }

    internal void SetNameColor(Color color)
    {
        if (_nameLabel != null)
            _nameLabel.style.color = color;
    }

    internal void SetSelectedCallback(System.Action onSelected)
    {
        _onSelected = onSelected;
    }

    internal void SetHealth(float current, float max)
    {
        if (_healthFill == null || max <= 0f)
            return;

        if (Mathf.Approximately(current, _lastHealth) && Mathf.Approximately(max, _lastMaxHealth))
            return;

        _lastHealth = current;
        _lastMaxHealth = max;
        _healthFill.style.width = Length.Percent(Mathf.Clamp01(current / max) * 100f);
    }

    internal void SetLevel(int level)
    {
        if (_levelLabel != null)
            _levelLabel.text = level.ToString();
    }

    internal NameplateState State { get; private set; }

    internal void SetDisabled(bool disabled) => Root.style.opacity = disabled ? 0.15f : 1f;

    internal void ApplyState(NameplateState state)
    {
        State = state;
        SetDisabled((state & NameplateState.Disabled) != 0);

        bool healthVisible = (state & NameplateState.HealthVisible) != 0;
        if (_healthBar != null)
            _healthBar.style.display = healthVisible ? DisplayStyle.Flex : DisplayStyle.None;

        bool hasLevel = (state & NameplateState.HasLevel) != 0;
        if (_levelLabel != null)
            _levelLabel.style.display = hasLevel ? DisplayStyle.Flex : DisplayStyle.None;

        Color textColor = StateColors.FirstOrDefault(e => (state & e.Flag) != 0)?.Color ?? Color.white;
        SetNameColor(textColor);
    }

    void OnClick(PointerDownEvent evt)
    {
        _onSelected?.Invoke();
        evt.StopPropagation();
    }

    internal bool IsValid()
    {
        return Dynel != null && Dynel.gameObject != null && Dynel.ShowNameplate;
    }

    sealed class StateColorEntry
    {
        public NameplateState Flag { get; }
        public Color Color { get; }

        public StateColorEntry(NameplateState flag, Color color)
        {
            Flag = flag;
            Color = color;
        }
    }
}
