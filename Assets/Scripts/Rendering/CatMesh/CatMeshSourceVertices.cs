using UnityEngine;

/// <summary>
/// The source (bind-pose) vertex positions of one CAT submesh. The uploaded mesh is not readable, and
/// stock's Deformer effect needs them after skinning (<see cref="CatMeshDeformHost"/>).
/// </summary>
public sealed class CatMeshSourceVertices : MonoBehaviour
{
    public Vector3[] Positions;
}
