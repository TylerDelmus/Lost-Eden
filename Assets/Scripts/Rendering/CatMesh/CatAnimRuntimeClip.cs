using System.Collections.Generic;
using AODB.Common.RDBObjects;
using UnityEngine;
using AoQuaternion = AODB.Common.Structs.Quaternion;
using AoVector3 = AODB.Common.Structs.Vector3;

public sealed class CatAnimRuntimeClip
{
    public readonly int AnimId;
    public readonly string Name;
    public readonly float SourceDuration;
    public readonly float LoopStart;
    public readonly float LoopEnd;
    public readonly float Duration;
    public readonly bool HasLoopTiming;
    public readonly float NoteTimeMs;
    public readonly BoneTrack[] Tracks;
    public readonly int BoneCount;

    /// <summary>Notes with a stock event id (<see cref="AnimNoteIds"/>), by time. Source-clip seconds.</summary>
    public readonly Note[] Notes;

    public const uint AlwaysOnFlags = 0xFFFFFFFFu;

    public struct BoneTrack
    {
        public int BoneIndex;
        public uint Flags;
        public Vector3Key[] Positions;
        public QuaternionKey[] Rotations;
    }

    public struct Vector3Key
    {
        public float Time;
        public Vector3 Value;
    }

    public struct QuaternionKey
    {
        public float Time;
        public Quaternion Value;
    }

    public struct Note
    {
        public float Time;
        public int EventId;
    }

    CatAnimRuntimeClip(
        int animId,
        string name,
        float sourceDuration,
        float loopStart,
        float loopEnd,
        bool hasLoopTiming,
        float noteTimeMs,
        BoneTrack[] tracks,
        int boneCount,
        Note[] notes)
    {
        AnimId = animId;
        Name = name;
        SourceDuration = sourceDuration;
        LoopStart = loopStart;
        LoopEnd = loopEnd;
        HasLoopTiming = hasLoopTiming;
        NoteTimeMs = noteTimeMs;
        Duration = Mathf.Max(loopEnd - loopStart, 0.001f);
        Tracks = tracks;
        BoneCount = boneCount;
        Notes = notes ?? System.Array.Empty<Note>();
        IndexTracksByBone();
    }

    BoneTrack[] _tracksByBone;

    // True when every key array is in non-decreasing time order, so a binary search lands on the same
    // key pair as the linear scan it replaces. The data is not guaranteed sorted; if any array is not,
    // the scan is kept.
    bool _keysSorted;

    void IndexTracksByBone()
    {
        _tracksByBone = new BoneTrack[BoneCount];
        for (int i = 0; i < BoneCount; i++)
            _tracksByBone[i].BoneIndex = -1;

        _keysSorted = true;
        for (int i = 0; i < Tracks.Length; i++)
        {
            BoneTrack track = Tracks[i];
            _keysSorted &= IsSorted(track.Positions) && IsSorted(track.Rotations);
            if (track.BoneIndex < 0 || track.BoneIndex >= BoneCount)
                continue;
            _tracksByBone[track.BoneIndex] = track;
        }
    }

    static bool IsSorted(Vector3Key[] keys)
    {
        if (keys == null)
            return true;
        for (int i = 1; i < keys.Length; i++)
        {
            if (keys[i].Time < keys[i - 1].Time)
                return false;
        }
        return true;
    }

    static bool IsSorted(QuaternionKey[] keys)
    {
        if (keys == null)
            return true;
        for (int i = 1; i < keys.Length; i++)
        {
            if (keys[i].Time < keys[i - 1].Time)
                return false;
        }
        return true;
    }

    /// <summary>
    /// The bone's local position and rotation at <paramref name="time"/> — loop-relative (from
    /// <see cref="LoopStart"/>) unless <paramref name="absoluteSourceTime"/>. False when the bone has no
    /// track or <paramref name="activeMask"/> disables it; a channel with no keys comes back unset.
    /// </summary>
    public bool TrySample(
        int boneIndex,
        int activeMask,
        float time,
        bool absoluteSourceTime,
        out Vector3 position,
        out bool hasPosition,
        out Quaternion rotation,
        out bool hasRotation)
    {
        position = default;
        rotation = default;
        hasPosition = false;
        hasRotation = false;

        if (_tracksByBone == null || (uint)boneIndex >= (uint)_tracksByBone.Length)
            return false;

        ref readonly BoneTrack track = ref _tracksByBone[boneIndex];
        if (track.BoneIndex != boneIndex || ((uint)activeMask & track.Flags) == 0)
            return false;

        float sourceTime = absoluteSourceTime ? time : LoopStart + time;

        Vector3Key[] positions = track.Positions;
        if (positions != null && positions.Length > 0)
        {
            position = _keysSorted ? SamplePositionSorted(positions, sourceTime) : SamplePosition(positions, sourceTime);
            hasPosition = true;
        }

        QuaternionKey[] rotations = track.Rotations;
        if (rotations != null && rotations.Length > 0)
        {
            rotation = _keysSorted ? SampleRotationSorted(rotations, sourceTime) : SampleRotation(rotations, sourceTime);
            hasRotation = true;
        }

        return true;
    }

    public void CountTrackFlags(out int alwaysOn, out int flag1, out int flag2, out int other)
    {
        alwaysOn = 0;
        flag1 = 0;
        flag2 = 0;
        other = 0;
        for (int i = 0; i < Tracks.Length; i++)
        {
            uint flags = Tracks[i].Flags;
            if (flags == AlwaysOnFlags)
                alwaysOn++;
            else if (flags == 1)
                flag1++;
            else if (flags == 2)
                flag2++;
            else
                other++;
        }
    }

    /// <summary>
    /// One-shots play the authored prefix (0 → loopstart) when loop markers exist,
    /// otherwise the full source clip. Looping playback still uses <see cref="Duration"/>.
    /// </summary>
    public float GetOneShotDuration()
    {
        if (HasLoopTiming && LoopStart > 0.001f)
            return Mathf.Min(LoopStart, SourceDuration);

        return SourceDuration;
    }

    public static CatAnimRuntimeClip Create(
        CATAnim catAnim,
        int animId,
        int boneCount)
    {
        if (catAnim?.Animation.BoneData == null || boneCount <= 0)
            return null;

        List<BoneData> boneDataList = catAnim.Animation.BoneData;
        var tracks = new List<BoneTrack>(boneDataList.Count);
        float sourceDuration = 0f;

        for (int i = 0; i < boneDataList.Count; i++)
        {
            BoneData boneData = boneDataList[i];
            int boneIndex = boneData.BoneId;
            if (boneIndex < 0 || boneIndex >= boneCount)
                continue;

            Vector3Key[] positions = BuildPositionKeys(boneData.TranslationKeys, ref sourceDuration);
            QuaternionKey[] rotations = BuildRotationKeys(boneData.RotationKeys, ref sourceDuration);
            if ((positions == null || positions.Length == 0) && (rotations == null || rotations.Length == 0))
                continue;

            tracks.Add(new BoneTrack
            {
                BoneIndex = boneIndex,
                Flags = ReadTrackFlags(boneData),
                Positions = positions,
                Rotations = rotations
            });
        }

        if (tracks.Count == 0)
            return null;

        sourceDuration = Mathf.Max(sourceDuration, 0.001f);

        float loopStart = 0f;
        float loopEnd = sourceDuration;
        bool hasLoopTiming = false;
        if (catAnim.TryGetLoopTiming(out int loopStartMs, out int loopEndMs)
            && loopEndMs > loopStartMs)
        {
            loopStart = ToSeconds(loopStartMs);
            loopEnd = ToSeconds(loopEndMs);
            hasLoopTiming = true;
        }

        ClampLoop(sourceDuration, ref loopStart, ref loopEnd);

        string name = BuildName(catAnim.Name, animId);
        float noteTimeMs = hasLoopTiming ? loopStart * 1000f : sourceDuration * 1000f;
        return new CatAnimRuntimeClip(
            animId,
            name,
            sourceDuration,
            loopStart,
            loopEnd,
            hasLoopTiming,
            noteTimeMs,
            tracks.ToArray(),
            boneCount,
            BuildNotes(catAnim));
    }

    /// <summary>Named notes → stock event ids; notes that map to 0 fire nothing and are dropped.</summary>
    static Note[] BuildNotes(CATAnim catAnim)
    {
        if (catAnim.AnimationIdentifiers == null || catAnim.AnimationIdentifiers.Length == 0)
            return System.Array.Empty<Note>();

        var notes = new List<Note>(catAnim.AnimationIdentifiers.Length);
        foreach (var id in catAnim.AnimationIdentifiers)
        {
            int eventId = AnimNoteIds.FromName(id.Name);
            if (eventId != AnimNoteIds.None)
                notes.Add(new Note { Time = ToSeconds(id.TimeMs), EventId = eventId });
        }
        notes.Sort((a, b) => a.Time.CompareTo(b.Time));
        return notes.ToArray();
    }

    static uint ReadTrackFlags(BoneData boneData)
    {
        int raw = boneData.Flags;
        if (raw == 0)
            return AlwaysOnFlags;
        return unchecked((uint)raw);
    }

    static void ClampLoop(float sourceDuration, ref float loopStart, ref float loopEnd)
    {
        loopStart = Mathf.Clamp(loopStart, 0f, sourceDuration);
        loopEnd = Mathf.Clamp(loopEnd, 0f, sourceDuration);

        if (loopEnd > loopStart + 0.001f)
            return;

        loopStart = 0f;
        loopEnd = sourceDuration;
    }

    static Vector3Key[] BuildPositionKeys(List<TranslationKey> keys, ref float duration)
    {
        if (keys == null || keys.Count == 0)
            return null;

        var result = new Vector3Key[keys.Count];
        for (int i = 0; i < keys.Count; i++)
        {
            float time = ToSeconds(keys[i].Time);
            AoVector3 pos = keys[i].Position;
            result[i] = new Vector3Key
            {
                Time = time,
                Value = new Vector3(pos.X, pos.Y, pos.Z)
            };
            if (time > duration)
                duration = time;
        }

        return result;
    }

    static QuaternionKey[] BuildRotationKeys(List<RotationKey> keys, ref float duration)
    {
        if (keys == null || keys.Count == 0)
            return null;

        var result = new QuaternionKey[keys.Count];
        for (int i = 0; i < keys.Count; i++)
        {
            float time = ToSeconds(keys[i].Time);
            AoQuaternion rot = keys[i].Rotation;
            result[i] = new QuaternionKey
            {
                Time = time,
                Value = new Quaternion(rot.X, rot.Y, rot.Z, rot.W)
            };
            if (time > duration)
                duration = time;
        }

        return result;
    }

    static float ToSeconds(float rawTime) => rawTime / 1000f;

    static Vector3 SamplePosition(Vector3Key[] keys, float time)
    {
        if (keys.Length == 1)
            return keys[0].Value;

        if (time <= keys[0].Time)
            return keys[0].Value;

        if (time >= keys[keys.Length - 1].Time)
            return keys[keys.Length - 1].Value;

        for (int i = 0; i < keys.Length - 1; i++)
        {
            Vector3Key a = keys[i];
            Vector3Key b = keys[i + 1];
            if (time > b.Time)
                continue;

            float span = Mathf.Max(b.Time - a.Time, 1e-6f);
            float t = (time - a.Time) / span;
            return Vector3.LerpUnclamped(a.Value, b.Value, t);
        }

        return keys[keys.Length - 1].Value;
    }

    // Binary-search forms of the two samplers. With keys sorted, the first key at or after `time` is
    // the `b` the linear scan stops on, so both interpolate the same pair with the same arithmetic.

    static Vector3 SamplePositionSorted(Vector3Key[] keys, float time)
    {
        int last = keys.Length - 1;
        if (last == 0 || time <= keys[0].Time)
            return keys[0].Value;
        if (time >= keys[last].Time)
            return keys[last].Value;

        int lo = 1, hi = last;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (keys[mid].Time < time)
                lo = mid + 1;
            else
                hi = mid;
        }

        ref readonly Vector3Key a = ref keys[lo - 1];
        ref readonly Vector3Key b = ref keys[lo];
        float span = Mathf.Max(b.Time - a.Time, 1e-6f);
        return Vector3.LerpUnclamped(a.Value, b.Value, (time - a.Time) / span);
    }

    static Quaternion SampleRotationSorted(QuaternionKey[] keys, float time)
    {
        int last = keys.Length - 1;
        if (last == 0 || time <= keys[0].Time)
            return keys[0].Value;
        if (time >= keys[last].Time)
            return keys[last].Value;

        int lo = 1, hi = last;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (keys[mid].Time < time)
                lo = mid + 1;
            else
                hi = mid;
        }

        ref readonly QuaternionKey a = ref keys[lo - 1];
        ref readonly QuaternionKey b = ref keys[lo];
        float span = Mathf.Max(b.Time - a.Time, 1e-6f);
        return Quaternion.SlerpUnclamped(a.Value, b.Value, (time - a.Time) / span);
    }

    static Quaternion SampleRotation(QuaternionKey[] keys, float time)
    {
        if (keys.Length == 1)
            return keys[0].Value;

        if (time <= keys[0].Time)
            return keys[0].Value;

        if (time >= keys[keys.Length - 1].Time)
            return keys[keys.Length - 1].Value;

        for (int i = 0; i < keys.Length - 1; i++)
        {
            QuaternionKey a = keys[i];
            QuaternionKey b = keys[i + 1];
            if (time > b.Time)
                continue;

            float span = Mathf.Max(b.Time - a.Time, 1e-6f);
            float t = (time - a.Time) / span;
            return Quaternion.SlerpUnclamped(a.Value, b.Value, t);
        }

        return keys[keys.Length - 1].Value;
    }

    static string BuildName(string catAnimName, int animId)
    {
        if (string.IsNullOrEmpty(catAnimName))
            return $"anim_{animId}";

        string trimmed = catAnimName.Trim().Trim('\0');
        if (trimmed.EndsWith(".ani", System.StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed.Substring(0, trimmed.Length - 4);

        return string.IsNullOrEmpty(trimmed) ? $"anim_{animId}" : $"{trimmed}_{animId}";
    }
}
