using UnityEngine;

/// <summary>
/// The source (bind-pose) vertex positions of one CAT submesh. The uploaded mesh is not readable, and
/// stock's Deformer and Shield effects need them after skinning (<see cref="CatMeshDeformHost"/>).
/// </summary>
public sealed class CatMeshSourceVertices : MonoBehaviour
{
    public Vector3[] Positions;

    /// <summary>
    /// The submesh's index into the CAT file's material table. Stock's CATRender keeps its materials by
    /// this index (randy31 <c>10057a30</c>), and a Shield with flag 0x10000 takes entry 0.
    /// </summary>
    public int MaterialId = -1;
}
