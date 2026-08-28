using System.Collections.Generic;
using UnityEngine;

public sealed class AttractorCollection : MonoBehaviour
{
    readonly Dictionary<AttractorPlace, Attractor> _byPlace = new();

    public IReadOnlyDictionary<AttractorPlace, Attractor> ByPlace => _byPlace;

    public void Clear() => _byPlace.Clear();

    public void Add(AttractorPlace place, Attractor attractor)
    {
        if (attractor == null)
            return;

        _byPlace[place] = attractor;
    }

    public bool TryGet(AttractorPlace place, out Attractor attractor)
        => _byPlace.TryGetValue(place, out attractor);
}
