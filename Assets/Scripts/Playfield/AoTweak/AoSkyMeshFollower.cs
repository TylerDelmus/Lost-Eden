using UnityEngine;

/// <summary>
/// AO e_LockToCamera: world position = camera + Position offset (follows XYZ).
/// </summary>
public sealed class AoSkyMeshFollower : MonoBehaviour
{
    Vector3 _positionOffset;

    public void Init(Vector3 positionOffset)
    {
        _positionOffset = positionOffset;
    }

    void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam == null)
            return;

        transform.position = cam.transform.position + _positionOffset;
    }
}
