using System.Collections.Generic;
using UnityEngine;

public sealed class AttractorCollection : MonoBehaviour
{
    readonly Dictionary<AttractorPlace, Attractor> _byPlace = new();

    public IReadOnlyDictionary<AttractorPlace, Attractor> ByPlace
    {
        get
        {
            EnsureBuilt();
            return _byPlace;
        }
    }

    void Awake() => RebuildFromChildren();

    void OnEnable() => RebuildFromChildren();

    public void Clear() => _byPlace.Clear();

    public void Add(AttractorPlace place, Attractor attractor)
    {
        if (attractor == null)
            return;

        attractor.Place = place;
        _byPlace[place] = attractor;
    }

    public bool TryGet(AttractorPlace place, out Attractor attractor)
    {
        EnsureBuilt();
        return _byPlace.TryGetValue(place, out attractor);
    }

    /// <summary>
    /// Runtime dictionaries are not copied by Instantiate — rebuild from child Attractors.
    /// </summary>
    public void RebuildFromChildren()
    {
        _byPlace.Clear();
        Attractor[] attractors = GetComponentsInChildren<Attractor>(true);
        for (int i = 0; i < attractors.Length; i++)
        {
            Attractor attractor = attractors[i];
            if (attractor == null)
                continue;

            // Prefer name parse — Place enum Head=0 is indistinguishable from unset.
            if (!AttractorPlaceUtil.TryParse(attractor.name, out AttractorPlace place))
                place = attractor.Place;

            attractor.Place = place;
            _byPlace[place] = attractor;
        }
    }

    void EnsureBuilt()
    {
        if (_byPlace.Count > 0)
            return;
        RebuildFromChildren();
    }
}
