using System;
using UnityEngine;

/// <summary>
/// Thread-safe CPU prep for a CatMesh visual (rest-pose verts + bind poses).
/// Unity objects are created later on the main thread from this payload.
/// </summary>
public sealed class CatMeshBuildData
{
    public int CatMeshId;
    public int RestAnimId;
    public CatMeshSubmeshSource[] Submeshes = Array.Empty<CatMeshSubmeshSource>();
    public Matrix4x4[] BindPoses = Array.Empty<Matrix4x4>();
    public Vector3[] RestLocalPositions = Array.Empty<Vector3>();
    public Quaternion[] RestLocalRotations = Array.Empty<Quaternion>();
    public float[] JointScales = Array.Empty<float>();
    public string[] JointNames = Array.Empty<string>();
    public int[] JointParents = Array.Empty<int>();
    public CatMeshAttractorData[] Attractors = Array.Empty<CatMeshAttractorData>();
}

public sealed class CatMeshAttractorData
{
    public string Name;
    public AttractorPlace Place;
    public int BoneIndex;
    public Vector3 LocalPosition;
    public Quaternion LocalRotation;
    public float Scale;
}

public sealed class CatMeshCacheEntry
{
    public int CatMeshId;
    public int RestAnimId;
    public GameObject Prototype;
    public Mesh[] SharedMeshes;
}

public enum CatMeshBuildRole
{
    CacheHit,
    Builder,
    Waiter
}
