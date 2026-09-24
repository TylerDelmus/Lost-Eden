using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A <c>CATRender_t::RegisterVertexProcessCallback</c> client that only reads the skinned vertices
/// (Stars starType 15's <c>100f740b</c>). <see cref="CatMeshDeformHost"/> calls it once per CAT group
/// each frame, after the pose is set.
/// </summary>
public interface ICatVertexReader
{
    bool IsAlive { get; }

    /// <summary>
    /// One group: <paramref name="count"/> vertices starting at <paramref name="baseIndex"/> of the
    /// mesh's <paramref name="total"/>. Positions and normals are in the group's own space;
    /// <paramref name="toWorld"/> takes them to world space.
    /// </summary>
    void ReadGroup(int count, int baseIndex, int total, List<Vector3> positions, List<Vector3> normals, Matrix4x4 toWorld);
}
