using UnityEngine;
using UnityEngine.UIElements;

internal class HitIndicatorView : TrackTransformView
{
    readonly Label _label;
    readonly float _regSize;
    readonly float _critSize;

    float _spawnTime;
    Vector3 _initPos;
    Vector3 _direction;
    float _speed;
    bool _isCrit;
    float _hideTime;

    const float MinSpeed = 0.65f;
    const float MaxSpeed = 0.85f;
    const float CritMinSpeed = 0.5f;
    const float CritMaxSpeed = 0.5f;
    const float BounceFrequency = 1.8f;
    const float BounceDamping = 2.0f;
    const float CritGrowDuration = 0.05f;
    const float FadeStart = 0.6f;

    internal HitIndicatorView(VisualTreeAsset asset) : base(asset)
    {
        _label = Root.Q<Label>("HitIndicator");
        _regSize = _label != null ? (_label.resolvedStyle.fontSize > 0 ? _label.resolvedStyle.fontSize : 24f) : 24f;
        _critSize = _regSize * 1.25f;
        if (_label != null)
            _label.style.fontSize = _regSize;
    }

    internal void Init(Vector3 initPos, Vector3 offset, bool isCrit, float hideTime, Camera camera)
    {
        base.Init(offset);
        _initPos = initPos;
        _spawnTime = Time.time;
        _isCrit = isCrit;
        _hideTime = Mathf.Max(0.05f, hideTime);

        float angle = Random.Range(0f, Mathf.PI * 2f);
        Vector3 raw = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        Vector3 camFwd = Vector3.forward;
        if (camera != null)
            camFwd = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized;

        const float depthBias = 0.75f;
        _direction = (raw - camFwd * Vector3.Dot(raw, camFwd) * (1f - depthBias)).normalized;
        _speed = isCrit ? Random.Range(CritMinSpeed, CritMaxSpeed) : Random.Range(MinSpeed, MaxSpeed);

        Root.style.opacity = 1f;
        if (_label != null)
            _label.style.fontSize = _regSize;
    }

    internal override void UpdatePos(Camera camera)
    {
        float t = Time.time - _spawnTime;
        float n = Mathf.Clamp01(t / _hideTime);
        float eased = 1f - (1f - n) * (1f - n);
        Vector3 worldPos;

        if (_isCrit)
        {
            Vector3 lateral = new Vector3(_direction.x, 0f, _direction.z) * (_speed * eased);
            float bounceY = _speed * Mathf.Abs(Mathf.Sin(t * BounceFrequency * Mathf.PI)) * Mathf.Exp(-t * BounceDamping);
            worldPos = _initPos + lateral + Vector3.up * bounceY;
        }
        else
        {
            worldPos = _initPos + _direction * (_speed * eased);
        }

        UpdatePos(worldPos, camera);

        Root.style.opacity = n < FadeStart ? 1f : 1f - (n - FadeStart) / (1f - FadeStart);

        if (_isCrit && _label != null)
            _label.style.fontSize = Mathf.Lerp(_regSize, _critSize, Mathf.Clamp01(t / CritGrowDuration));
    }

    internal void UpdateContent(HitIndicatorInfo info)
    {
        if (_label == null)
            return;

        _label.text = info.Damage.ToString();
        _label.style.fontSize = _regSize;
        _label.style.color = info.IsCrit ? Color.yellow : Color.white;
    }

    internal bool HitTimerExpired()
    {
        return Time.time > _spawnTime + _hideTime;
    }
}
