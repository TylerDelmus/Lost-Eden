using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

internal class HitIndicatorOverlay : ObjectPoolOverlay<HitIndicatorView>
{
    readonly VisualTreeAsset _hitAsset;
    readonly List<int> _toRelease = new();
    readonly float _hideTime;
    int _index;

    public HitIndicatorOverlay(VisualTreeAsset hitIndicatorAsset, VisualElement root, float hideTime = 1f)
        : base(root, defaultCapacity: 32)
    {
        _hitAsset = hitIndicatorAsset;
        _hideTime = hideTime;
        MaxElements = 64;
    }

    internal override HitIndicatorView CreateView()
    {
        return new HitIndicatorView(_hitAsset);
    }

    public void OnObjectHit(Character obj, HitIndicatorInfo info, Camera camera)
    {
        if (obj == null || info.Damage <= 0)
            return;

        HitIndicatorView indicatorView = Pool.Get();
        int key = GetNextIndex();
        if (_cache.TryGetValue(key, out HitIndicatorView previous))
            Pool.Release(previous);

        _cache[key] = indicatorView;

        UnityEngine.Vector3 worldPos = obj.TryGetIndicatorPosition(out UnityEngine.Vector3 indicator)
            ? indicator
            : obj.transform.position;

        indicatorView.Init(worldPos, Offset, info.IsCrit, _hideTime, camera);
        indicatorView.UpdateContent(info);
    }

    int GetNextIndex()
    {
        _index = _index % MaxElements + 1;
        return _index;
    }

    public void Tick(Camera camera)
    {
        _toRelease.Clear();

        foreach (var kvp in _cache)
        {
            if (kvp.Value.HitTimerExpired())
            {
                _toRelease.Add(kvp.Key);
                continue;
            }

            kvp.Value.UpdatePos(camera);
        }

        for (int i = 0; i < _toRelease.Count; i++)
        {
            int key = _toRelease[i];
            if (!_cache.Remove(key, out HitIndicatorView view))
                continue;

            Pool.Release(view);
        }
    }

    public void ClearAll()
    {
        foreach (var kvp in _cache)
            Pool.Release(kvp.Value);
        _cache.Clear();
    }
}
