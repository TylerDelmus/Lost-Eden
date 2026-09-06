using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Hand-authored volumes where grass must not be placed, stored per playfield.
///
/// Replaces the earlier approach of deriving occluders from statel renderers. That needed
/// a layer on every object and a walk over thousands of renderers at load, which cost more
/// than the grass itself. A zone needs a handful of volumes, so they are authored by hand
/// and stored here.
///
/// Volumes are in world space, matching the coordinates PlayfieldGrassBuilder places grass
/// in, so what you see in the scene is what gets cut.
///
/// Every volume carries a stable Id, assigned when it is created. Authoring happens in play
/// mode (playfields load from the RDB), and play-mode GameObjects are destroyed on exit, so
/// each session starts with an empty scene. Saving therefore has to merge by Id rather than
/// replace, or every session would wipe the last one's work.
/// </summary>
[CreateAssetMenu(fileName = "GrassExclusionVolumes", menuName = "AORebirth/Grass Exclusion Volumes")]
public sealed class GrassExclusionVolumes : ScriptableObject
{
    public enum Shape
    {
        /// <summary>Oriented box. Default, and what an unmarked volume is treated as.</summary>
        Box = 0,

        /// <summary>
        /// Elliptical cylinder along the volume's local Y. Size.x and Size.z are the two
        /// diameters, Size.y the height - so a uniformly scaled child gives a plain circle,
        /// and a stretched one gives an ellipse. Useful where a square cut would read as an
        /// obviously straight edge in open ground.
        /// </summary>
        Cylinder = 1
    }

    [Serializable]
    public struct Volume
    {
        [Tooltip("Stable unique id, generated when the volume is created. Identity for merging.")]
        public string Id;

        [Tooltip("Optional human-readable name, shown in the scene view.")]
        public string Label;

        public Shape Shape;
        public Vector3 Center;
        public Vector3 Size;
        public Vector3 EulerAngles;
    }

    [Serializable]
    public sealed class PlayfieldEntry
    {
        public int PlayfieldId;

        // Renamed from Boxes when cylinders were added; FormerlySerializedAs keeps any
        // already-authored volumes.
        [FormerlySerializedAs("Boxes")]
        public List<Volume> Volumes = new List<Volume>();
    }

    [SerializeField] List<PlayfieldEntry> _playfields = new List<PlayfieldEntry>();

    public IReadOnlyList<PlayfieldEntry> Playfields => _playfields;

    public static string NewId() => Guid.NewGuid().ToString("N").Substring(0, 8);

    /// <summary>Null when this playfield has no authored volumes - the common case.</summary>
    public List<Volume> GetVolumes(int playfieldId)
    {
        for (int i = 0; i < _playfields.Count; i++)
        {
            if (_playfields[i] != null && _playfields[i].PlayfieldId == playfieldId)
                return _playfields[i].Volumes;
        }

        return null;
    }

    /// <summary>
    /// Adds the volume, or replaces the existing one with the same Id. Returns true when it
    /// was an addition. Volumes already in the asset that are not in the scene are left
    /// alone - use <see cref="Remove"/> to delete one deliberately.
    /// </summary>
    public bool Upsert(int playfieldId, Volume volume)
    {
        if (string.IsNullOrEmpty(volume.Id))
            volume.Id = NewId();

        List<Volume> list = GetOrCreateEntry(playfieldId).Volumes;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Id == volume.Id)
            {
                list[i] = volume;
                return false;
            }
        }

        list.Add(volume);
        return true;
    }

    public bool Remove(int playfieldId, string id)
    {
        if (string.IsNullOrEmpty(id))
            return false;

        List<Volume> list = GetVolumes(playfieldId);
        if (list == null)
            return false;

        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Id == id)
            {
                list.RemoveAt(i);
                return true;
            }
        }

        return false;
    }

    /// <summary>Destructive: discards anything stored for this playfield that is not in the list.</summary>
    public void ReplaceVolumes(int playfieldId, List<Volume> volumes)
        => GetOrCreateEntry(playfieldId).Volumes = volumes ?? new List<Volume>();

    PlayfieldEntry GetOrCreateEntry(int playfieldId)
    {
        for (int i = 0; i < _playfields.Count; i++)
        {
            if (_playfields[i] != null && _playfields[i].PlayfieldId == playfieldId)
                return _playfields[i];
        }

        var entry = new PlayfieldEntry { PlayfieldId = playfieldId };
        _playfields.Add(entry);
        return entry;
    }
}