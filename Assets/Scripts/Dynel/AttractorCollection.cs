using System.Collections.Generic;
using UnityEngine;

public sealed class AttractorCollection : MonoBehaviour
{
    readonly Dictionary<AttractorPlace, Attractor> _byPlace = new();
    readonly Dictionary<string, Attractor> _byName = new(System.StringComparer.Ordinal);

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

    public void Clear()
    {
        _byPlace.Clear();
        _byName.Clear();
    }

    public void Add(AttractorPlace place, Attractor attractor)
    {
        if (attractor == null)
            return;

        attractor.Place = place;
        attractor.HasPlace = true;
        _byPlace[place] = attractor;
        _byName[attractor.name] = attractor;
    }

    /// <summary>An attractor with no place, findable only by name.</summary>
    public void AddNamed(Attractor attractor)
    {
        if (attractor == null)
            return;

        attractor.HasPlace = false;
        _byName[attractor.name] = attractor;
    }

    /// <summary>
    /// Any of the model's attractors by its exact CAT name, the way randy31's <c>RCATMesh_t::GetAttractor</c>
    /// finds it (a case-sensitive compare).
    /// </summary>
    public bool TryGetByName(string name, out Transform attractor)
    {
        attractor = null;
        if (string.IsNullOrEmpty(name))
            return false;
        EnsureBuilt();
        if (!_byName.TryGetValue(name, out Attractor found) || found == null)
            return false;
        attractor = found.transform;
        return true;
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
        _byName.Clear();
        Attractor[] attractors = GetComponentsInChildren<Attractor>(true);
        for (int i = 0; i < attractors.Length; i++)
        {
            Attractor attractor = attractors[i];
            if (attractor == null)
                continue;

            _byName[attractor.name] = attractor;
            if (!attractor.HasPlace)
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
        if (_byPlace.Count > 0 || _byName.Count > 0)
            return;
        RebuildFromChildren();
    }
}
