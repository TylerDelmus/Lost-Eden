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
    public readonly float TrimStart;
    public readonly float TrimEnd;
    public readonly float Duration;
    public readonly BoneTrack[] Tracks;

    public struct BoneTrack
    {
        public int BoneIndex;
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

    CatAnimRuntimeClip(
        int animId,
        string name,
        float sourceDuration,
        float trimStart,
        float trimEnd,
        BoneTrack[] tracks)
    {
        AnimId = animId;
        Name = name;
        SourceDuration = sourceDuration;
        TrimStart = trimStart;
        TrimEnd = trimEnd;
        Duration = Mathf.Max(sourceDuration - trimStart - trimEnd, 0.001f);
        Tracks = tracks;
    }

    public static CatAnimRuntimeClip Create(
        CATAnim catAnim,
        int animId,
        int boneCount,
        float trimStart = 0f,
        float trimEnd = 0f)
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
                Positions = positions,
                Rotations = rotations
            });
        }

        if (tracks.Count == 0)
            return null;

        sourceDuration = Mathf.Max(sourceDuration, 0.001f);
        ClampTrim(sourceDuration, ref trimStart, ref trimEnd);

        string name = BuildName(catAnim.Name, animId);
        return new CatAnimRuntimeClip(animId, name, sourceDuration, trimStart, trimEnd, tracks.ToArray());
    }

    public void Evaluate(int boneIndex, float time, out Vector3? localPosition, out Quaternion? localRotation)
    {
        localPosition = null;
        localRotation = null;

        float sourceTime = TrimStart + time;

        for (int i = 0; i < Tracks.Length; i++)
        {
            BoneTrack track = Tracks[i];
            if (track.BoneIndex != boneIndex)
                continue;

            if (track.Positions != null && track.Positions.Length > 0)
                localPosition = SamplePosition(track.Positions, sourceTime);

            if (track.Rotations != null && track.Rotations.Length > 0)
                localRotation = SampleRotation(track.Rotations, sourceTime);

            return;
        }
    }

    static void ClampTrim(float sourceDuration, ref float trimStart, ref float trimEnd)
    {
        trimStart = Mathf.Max(0f, trimStart);
        trimEnd = Mathf.Max(0f, trimEnd);

        float maxTrim = Mathf.Max(sourceDuration - 0.001f, 0f);
        if (trimStart + trimEnd <= maxTrim)
            return;

        if (trimStart >= maxTrim)
        {
            trimStart = maxTrim;
            trimEnd = 0f;
            return;
        }

        trimEnd = maxTrim - trimStart;
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
