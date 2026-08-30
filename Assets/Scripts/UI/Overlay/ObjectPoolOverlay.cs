using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.UIElements;

internal abstract class ObjectPoolOverlay<T> where T : TrackTransformView
{
    protected readonly Dictionary<int, T> _cache;
    protected readonly ObjectPool<T> Pool;
    protected readonly VisualElement Root;

    internal int MaxElements = 500;
    internal Vector3 Offset = Vector3.zero;

    protected ObjectPoolOverlay(VisualElement root, int defaultCapacity = 32)
    {
        Root = root;
        _cache = new Dictionary<int, T>();
        Pool = new ObjectPool<T>(CreateView, EnableView, DisableView, DestroyView, defaultCapacity: defaultCapacity);
    }

    internal abstract T CreateView();

    protected void DestroyView(T view)
    {
        view.Root.RemoveFromHierarchy();
    }

    protected void DisableView(T view)
    {
        if (view.Root.parent == Root)
            Root.Remove(view.Root);
    }

    protected void EnableView(T view)
    {
        Root.Add(view.Root);
    }
}
