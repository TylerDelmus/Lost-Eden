using System;
using AODB.Common.RDBObjects;
using UnityEngine;
using AoVector3 = AODB.Common.Structs.Vector3;

public sealed class CatMeshBindPose
{
    readonly Vector3[] _positions;
    readonly Quaternion[] _rotations;
    readonly int[] _positionSamples;

    public int JointCount => _positions.Length;

    CatMeshBindPose(Vector3[] positions, Quaternion[] rotations, int[] positionSamples)
    {
        _positions = positions;
        _rotations = rotations;
        _positionSamples = positionSamples;
    }

    public static CatMeshBindPose FromRdbCatMesh(RDBCatMesh catMesh)
    {
        int jointCount = catMesh?.Joints?.Count ?? 0;
        var positions = new Vector3[jointCount];
        var rotations = new Quaternion[jointCount];
        var samples = new int[jointCount];

        for (int i = 0; i < jointCount; i++)
            rotations[i] = Quaternion.identity;

        if (catMesh?.MeshGroups == null)
            return new CatMeshBindPose(positions, rotations, samples);

        for (int g = 0; g < catMesh.MeshGroups.Count; g++)
        {
            RDBCatMesh.MeshGroup group = catMesh.MeshGroups[g];
            if (group?.Meshes == null)
                continue;

            for (int m = 0; m < group.Meshes.Count; m++)
            {
                RDBCatMesh.Mesh mesh = group.Meshes[m];
                if (mesh?.Vertices == null)
                    continue;

                for (int v = 0; v < mesh.Vertices.Count; v++)
                    AccumulateBindJoint(mesh.Vertices[v], positions, samples);
            }
        }

        for (int i = 0; i < jointCount; i++)
        {
            if (samples[i] > 0)
                positions[i] /= samples[i];
        }

        FillMissingFromParents(catMesh, positions, samples);
        return new CatMeshBindPose(positions, rotations, samples);
    }

    static void FillMissingFromParents(RDBCatMesh catMesh, Vector3[] positions, int[] samples)
    {
        if (catMesh?.Joints == null)
            return;

        // Walk hierarchy from root so children without samples inherit parent bind position.
        for (int i = 0; i < catMesh.Joints.Count; i++)
            FillMissingRecursive(catMesh, i, positions, samples);
    }

    static void FillMissingRecursive(RDBCatMesh catMesh, int joint, Vector3[] positions, int[] samples)
    {
        RDBCatMesh.Joint source = catMesh.Joints[joint];
        if (source?.ChildJoints == null)
            return;

        for (int c = 0; c < source.ChildJoints.Length; c++)
        {
            int child = source.ChildJoints[c];
            if (child < 0 || child >= positions.Length)
                continue;

            if (samples[child] <= 0)
            {
                positions[child] = positions[joint];
                samples[child] = 1;
            }

            FillMissingRecursive(catMesh, child, positions, samples);
        }
    }

    static void AccumulateBindJoint(
        RDBCatMesh.Vertex vertex,
        Vector3[] positions,
        int[] samples)
    {
        Vector3 position = ToUnity(vertex.Position);

        if (vertex.Joint1Weight >= 0.99f)
            AccumulateJoint(position, ToUnity(vertex.RelToJoint1), vertex.Joint1, positions, samples);

        if (vertex.Joint1Weight <= 0.01f)
            AccumulateJoint(position, ToUnity(vertex.RelToJoint2), vertex.Joint2, positions, samples);
    }

    static void AccumulateJoint(
        Vector3 position,
        Vector3 relative,
        int joint,
        Vector3[] positions,
        int[] samples)
    {
        if (joint < 0 || joint >= positions.Length)
            return;

        positions[joint] += position - relative;
        samples[joint]++;
    }

    public Vector3 GetPosition(int joint)
    {
        if (joint < 0 || joint >= _positions.Length)
            return Vector3.zero;
        return _positions[joint];
    }

    public Quaternion GetRotation(int joint)
    {
        if (joint < 0 || joint >= _rotations.Length)
            return Quaternion.identity;
        return _rotations[joint];
    }

    public Vector3 GetSkinnedPosition(RDBCatMesh.Vertex vertex)
    {
        Vector3 pos1 = GetJointPoint(vertex.Joint1, ToUnity(vertex.RelToJoint1));
        Vector3 pos2 = GetJointPoint(vertex.Joint2, ToUnity(vertex.RelToJoint2));
        return Vector3.Lerp(pos2, pos1, vertex.Joint1Weight);
    }

    Vector3 GetJointPoint(int joint, Vector3 relative)
    {
        if (joint < 0 || joint >= _positions.Length)
            return relative;

        return _positions[joint] + _rotations[joint] * relative;
    }

    static Vector3 ToUnity(AoVector3 v) => new Vector3(v.X, v.Y, v.Z);
}
