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

    public static Matrix4x4[] CreateBindPoses(Matrix4x4[] boneWorldMatrices)
    {
        if (boneWorldMatrices == null || boneWorldMatrices.Length == 0)
            return Array.Empty<Matrix4x4>();

        var bindPoses = new Matrix4x4[boneWorldMatrices.Length];
        for (int i = 0; i < boneWorldMatrices.Length; i++)
            bindPoses[i] = boneWorldMatrices[i].inverse;
        return bindPoses;
    }

    public static int[] BuildParentIndices(RDBCatMesh catMesh)
    {
        if (catMesh?.Joints == null || catMesh.Joints.Count == 0)
            return Array.Empty<int>();

        int count = catMesh.Joints.Count;
        var parents = new int[count];
        for (int i = 0; i < count; i++)
            parents[i] = -1;

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
                parents[child] = i;
            }
        }

        return parents;
    }

    public static void ExtractRestLocals(
        int jointCount,
        CATAnim catAnim,
        out Vector3[] localPositions,
        out Quaternion[] localRotations)
    {
        localPositions = new Vector3[jointCount];
        localRotations = new Quaternion[jointCount];
        for (int i = 0; i < jointCount; i++)
        {
            localPositions[i] = Vector3.zero;
            localRotations[i] = Quaternion.identity;
        }

        if (catAnim?.Animation.BoneData == null)
            return;

        for (int i = 0; i < catAnim.Animation.BoneData.Count; i++)
        {
            BoneData boneData = catAnim.Animation.BoneData[i];
            int boneId = boneData.BoneId;
            if (boneId < 0 || boneId >= jointCount)
                continue;

            if (boneData.TranslationKeys != null && boneData.TranslationKeys.Count > 0)
            {
                AoVector3 pos = boneData.TranslationKeys[0].Position;
                localPositions[boneId] = new Vector3(pos.X, pos.Y, pos.Z);
            }

            if (boneData.RotationKeys != null && boneData.RotationKeys.Count > 0)
            {
                AoQuaternion rot = boneData.RotationKeys[0].Rotation;
                localRotations[boneId] = new Quaternion(rot.X, rot.Y, rot.Z, rot.W);
            }
        }
    }

    public static Matrix4x4[] ComputeWorldMatrices(
        int[] parents,
        Vector3[] localPositions,
        Quaternion[] localRotations,
        float[] scales)
    {
        int count = localPositions?.Length ?? 0;
        var worlds = new Matrix4x4[count];
        if (count == 0)
            return worlds;

        var locals = new Matrix4x4[count];
        for (int i = 0; i < count; i++)
        {
            float scale = scales != null && i < scales.Length && scales[i] > 0f ? scales[i] : 1f;
            locals[i] = Matrix4x4.TRS(localPositions[i], localRotations[i], Vector3.one * scale);
        }

        bool[] computed = new bool[count];
        for (int i = 0; i < count; i++)
            ComputeWorldRecursive(i, parents, locals, worlds, computed);

        return worlds;
    }

    static void ComputeWorldRecursive(
        int index,
        int[] parents,
        Matrix4x4[] locals,
        Matrix4x4[] worlds,
        bool[] computed)
    {
        if (computed[index])
            return;

        int parent = parents != null && index < parents.Length ? parents[index] : -1;
        if (parent >= 0 && parent < locals.Length)
        {
            ComputeWorldRecursive(parent, parents, locals, worlds, computed);
            worlds[index] = worlds[parent] * locals[index];
        }
        else
        {
            worlds[index] = locals[index];
        }

        computed[index] = true;
    }

    public static Transform[] CreateHierarchyFromBuildData(CatMeshBuildData build, Transform root)
    {
        if (build == null || build.JointNames == null || build.JointNames.Length == 0)
            return Array.Empty<Transform>();

        int count = build.JointNames.Length;
        var bones = new Transform[count];

        for (int i = 0; i < count; i++)
        {
            string name = string.IsNullOrEmpty(build.JointNames[i]) ? $"Joint_{i}" : build.JointNames[i];
            var go = new GameObject(name);
            bones[i] = go.transform;
            bones[i].SetParent(root, false);
            bones[i].localPosition = i < build.RestLocalPositions.Length ? build.RestLocalPositions[i] : Vector3.zero;
            bones[i].localRotation = i < build.RestLocalRotations.Length ? build.RestLocalRotations[i] : Quaternion.identity;

            float scale = i < build.JointScales.Length ? build.JointScales[i] : 1f;
            bones[i].localScale = scale > 0f ? Vector3.one * scale : Vector3.one;
        }

        for (int i = 0; i < count; i++)
        {
            int parent = i < build.JointParents.Length ? build.JointParents[i] : -1;
            if (parent < 0 || parent >= count)
                continue;
            bones[i].SetParent(bones[parent], false);
        }

        return bones;
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
