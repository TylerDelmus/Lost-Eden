using System;
using AODB.Common.RDBObjects;
using UnityEngine;
using AoQuaternion = AODB.Common.Structs.Quaternion;
using AoVector3 = AODB.Common.Structs.Vector3;

public static class CatMeshSkeleton
{
    /// <summary>
    /// Create joint transforms parented under <paramref name="root"/> using an inferred bind pose
    /// (positions from mesh verts, identity rotations). Used for static / no-anim meshes.
    /// </summary>
    public static Transform[] Create(RDBCatMesh catMesh, CatMeshBindPose bindPose, Transform root)
    {
        if (catMesh?.Joints == null || catMesh.Joints.Count == 0)
            return Array.Empty<Transform>();

        int count = catMesh.Joints.Count;
        var bones = new Transform[count];

        for (int i = 0; i < count; i++)
        {
            RDBCatMesh.Joint joint = catMesh.Joints[i];
            string name = string.IsNullOrEmpty(joint?.Name) ? $"Joint_{i}" : joint.Name;
            var go = new GameObject(name);
            bones[i] = go.transform;
            bones[i].SetParent(root, false);
            bones[i].position = root.TransformPoint(bindPose.GetPosition(i));
            bones[i].rotation = root.rotation * bindPose.GetRotation(i);

            float scale = joint?.Scale ?? 1f;
            if (scale > 0f && !Mathf.Approximately(scale, 1f))
                bones[i].localScale = Vector3.one * scale;
        }

        ParentHierarchy(catMesh, bones, worldPositionStays: true);
        return bones;
    }

    /// <summary>
    /// Create joint hierarchy with identity locals (CirExport style). Apply a CATAnim frame-0
    /// pose afterward so rest locals match animation space.
    /// </summary>
    public static Transform[] CreateHierarchy(RDBCatMesh catMesh, Transform root)
    {
        if (catMesh?.Joints == null || catMesh.Joints.Count == 0)
            return Array.Empty<Transform>();

        int count = catMesh.Joints.Count;
        var bones = new Transform[count];

        for (int i = 0; i < count; i++)
        {
            RDBCatMesh.Joint joint = catMesh.Joints[i];
            string name = string.IsNullOrEmpty(joint?.Name) ? $"Joint_{i}" : joint.Name;
            var go = new GameObject(name);
            bones[i] = go.transform;
            bones[i].SetParent(root, false);
            bones[i].localPosition = Vector3.zero;
            bones[i].localRotation = Quaternion.identity;
            bones[i].localScale = Vector3.one;

            float scale = joint?.Scale ?? 1f;
            if (scale > 0f && !Mathf.Approximately(scale, 1f))
                bones[i].localScale = Vector3.one * scale;
        }

        ParentHierarchy(catMesh, bones, worldPositionStays: false);
        return bones;
    }

    /// <summary>
    /// Apply the first keyframe of <paramref name="catAnim"/> as local TR — same rest pose
    /// CirExport uses when building the Assimp skeleton.
    /// </summary>
    public static void ApplyFirstFramePose(Transform[] bones, CATAnim catAnim)
    {
        if (bones == null || catAnim?.Animation.BoneData == null)
            return;

        for (int i = 0; i < catAnim.Animation.BoneData.Count; i++)
        {
            BoneData boneData = catAnim.Animation.BoneData[i];
            int boneId = boneData.BoneId;
            if (boneId < 0 || boneId >= bones.Length || bones[boneId] == null)
                continue;

            if (boneData.TranslationKeys != null && boneData.TranslationKeys.Count > 0)
            {
                AoVector3 pos = boneData.TranslationKeys[0].Position;
                bones[boneId].localPosition = new Vector3(pos.X, pos.Y, pos.Z);
            }

            if (boneData.RotationKeys != null && boneData.RotationKeys.Count > 0)
            {
                AoQuaternion rot = boneData.RotationKeys[0].Rotation;
                bones[boneId].localRotation = new Quaternion(rot.X, rot.Y, rot.Z, rot.W);
            }
        }
    }

    public static Matrix4x4[] CreateBindPoses(Transform[] bones, Transform root)
    {
        if (bones == null || bones.Length == 0)
            return Array.Empty<Matrix4x4>();

        var bindPoses = new Matrix4x4[bones.Length];
        Matrix4x4 rootLocalToWorld = root.localToWorldMatrix;
        for (int i = 0; i < bones.Length; i++)
            bindPoses[i] = bones[i].worldToLocalMatrix * rootLocalToWorld;

        return bindPoses;
    }

    static void ParentHierarchy(RDBCatMesh catMesh, Transform[] bones, bool worldPositionStays)
    {
        int count = bones.Length;
        for (int i = 0; i < count; i++)
        {
            RDBCatMesh.Joint joint = catMesh.Joints[i];
            if (joint?.ChildJoints == null)
                continue;

            for (int c = 0; c < joint.ChildJoints.Length; c++)
            {
                int child = joint.ChildJoints[c];
                if (child < 0 || child >= count)
                    continue;

                bones[child].SetParent(bones[i], worldPositionStays);
            }
        }
    }
}
