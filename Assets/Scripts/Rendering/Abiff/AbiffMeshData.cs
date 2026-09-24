using UnityEngine;

public sealed class AbiffMeshData
{
    public Vector3[] Vertices;
    public Vector3[] Normals;
    public Vector2[] UVs;

    /// <summary>
    /// Optional second UV set. On merged statel meshes x is the base-color texture-array slice
    /// (see <see cref="AbiffTextureArrays"/>); null everywhere else.
    /// </summary>
    public Vector2[] UV1;

    public int[] Triangles;
}
