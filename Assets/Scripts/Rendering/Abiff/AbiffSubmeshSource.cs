using UnityEngine;

public sealed class AbiffSubmeshSource
{
    public Vector3[] Positions;
    public Vector3[] Normals;
    public Vector2[] UVs;
    public int[] Triangles;
    public Vector3 BasePosition;
    public Quaternion BaseRotation;
    public AbiffMaterialDesc Material;
    public AbiffUvKey[] UvKeys;
    public bool UvLoop;
    public float UvDuration;
}
