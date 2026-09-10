using UnityEngine;

/// <summary>
/// Inspector + gizmo dump for a single statel placement (shear / rotation unpack).
/// Attached by <see cref="StatelParser"/> when <see cref="StatelParser.AttachDebugInfo"/> is true.
/// </summary>
public sealed class StatelDebugInfo : MonoBehaviour
{
    [Header("Identity")]
    [SerializeField] int _placementIndex;
    [SerializeField] int _meshId;
    [SerializeField] string _meshName;

    [Header("Placement")]
    [SerializeField] Vector3 _position;
    [SerializeField] Quaternion _rotation;
    [SerializeField] Vector3 _appliedScale;
    [SerializeField] string _flags;
    [SerializeField] byte _flags2;
    [SerializeField] int[] _textureOverrides;

    [Header("Scale / Rotation Inputs")]
    [SerializeField] bool _sheared;
    [SerializeField] string _rotationPacked;
    [SerializeField] float _baseScale;

    [Header("Shear Path")]
    [SerializeField] int _rotationSteps;
    [SerializeField] float _yawRadians;
    [SerializeField] float _yawDegrees;
    [SerializeField] int _scaleSteps;
    [SerializeField] float _scaleFactor;
    [SerializeField] float _finalScale;
    [SerializeField] float _shearFactor;
    [SerializeField] float _rawShearFactor;
    [SerializeField] Matrix4x4 _shearMatrix;
    [Tooltip("Mesh-space slant currently baked: z' = z + shearFactor * x")]
    [SerializeField] string _bakeFormula = "z' = z + shearFactor * x";

    [Header("Non-Shear Path")]
    [SerializeField] bool _rotationOverflow;
    [SerializeField] uint _x;
    [SerializeField] uint _v4;
    [SerializeField] float _angleXRadians;
    [SerializeField] float _angleYRadians;
    [SerializeField] float _angleZRadians;
    [SerializeField] float _angleXDegrees;
    [SerializeField] float _angleYDegrees;
    [SerializeField] float _angleZDegrees;

    [Header("Gizmos")]
    [SerializeField] float _gizmoExtent = 2f;
    [SerializeField] bool _drawAxes = true;
    [SerializeField] bool _drawShearSlant = true;

    public int PlacementIndex => _placementIndex;
    public int MeshId => _meshId;
    public string MeshName => _meshName;
    public bool Sheared => _sheared;
    public float ShearFactor => _shearFactor;

    public void Set(
        int placementIndex,
        int meshId,
        string meshName,
        Vector3 position,
        Vector3 appliedScale,
        int flags,
        byte flags2,
        int[] textureOverrides,
        StatelParser.ScaleRotationInfo info)
    {
        _placementIndex = placementIndex;
        _meshId = meshId;
        _meshName = meshName;
        _position = position;
        _rotation = info.Rotation;
        _appliedScale = appliedScale;
        _flags = $"0x{unchecked((uint)flags):X8}";
        _flags2 = flags2;
        _textureOverrides = textureOverrides != null
            ? (int[])textureOverrides.Clone()
            : System.Array.Empty<int>();

        _sheared = info.Sheared;
        _rotationPacked = $"0x{info.RotationPacked:X}";
        _baseScale = info.BaseScale;

        _rotationSteps = info.RotationSteps;
        _yawRadians = info.YawRadians;
        _yawDegrees = info.YawDegrees;
        _scaleSteps = info.ScaleSteps;
        _scaleFactor = info.ScaleFactor;
        _finalScale = info.FinalScale;
        _shearFactor = info.ShearFactor;
        _rawShearFactor = -info.ShearFactor;
        _shearMatrix = info.ShearMatrix;
        _bakeFormula = "z' = z + shearFactor * x";

        _rotationOverflow = info.RotationOverflow;
        _x = info.X;
        _v4 = info.V4;
        _angleXRadians = info.AngleXRadians;
        _angleYRadians = info.AngleYRadians;
        _angleZRadians = info.AngleZRadians;
        _angleXDegrees = info.AngleXDegrees;
        _angleYDegrees = info.AngleYDegrees;
        _angleZDegrees = info.AngleZDegrees;
    }

    [ContextMenu("Log Shear Breakdown")]
    public void LogShearBreakdown()
    {
        if (_sheared)
        {
            Debug.Log(
                $"[StatelDebug] #{_placementIndex} mesh={_meshId} '{_meshName}' " +
                $"flags={_flags} flags2={_flags2} packed={_rotationPacked} " +
                $"yaw={_yawDegrees:F2}° steps={_rotationSteps} scaleSteps={_scaleSteps} " +
                $"scaleFactor={_scaleFactor:F4} finalScale={_finalScale:F4} " +
                $"rawShear={_rawShearFactor:F4} appliedShear={_shearFactor:F4} " +
                $"appliedScale={_appliedScale} bake={_bakeFormula}",
                this);
            return;
        }

        Debug.Log(
            $"[StatelDebug] #{_placementIndex} mesh={_meshId} '{_meshName}' " +
            $"flags={_flags} packed={_rotationPacked} overflow={_rotationOverflow} " +
            $"x={_x} v4={_v4} angles(deg)=({_angleXDegrees:F2},{_angleYDegrees:F2},{_angleZDegrees:F2}) " +
            $"appliedScale={_appliedScale}",
            this);
    }

    void OnDrawGizmosSelected()
    {
        Transform t = transform;
        Vector3 origin = t.position;
        float extent = Mathf.Max(0.1f, _gizmoExtent);

        if (_drawAxes)
        {
            // Local axes after placement rotation (root space).
            Gizmos.color = Color.red;
            Gizmos.DrawLine(origin, origin + t.right * extent);
            Gizmos.color = Color.green;
            Gizmos.DrawLine(origin, origin + t.up * extent);
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(origin, origin + t.forward * extent);
        }

        if (!_sheared || !_drawShearSlant)
            return;

        // Mesh-space bake is z' = z + shear * x, then root rotation/scale.
        // Show unsheared +X edge (cyan) vs sheared edge (magenta) in local space.
        Vector3 localX = new Vector3(extent, 0f, 0f);
        Vector3 localSheared = new Vector3(extent, 0f, _shearFactor * extent);

        Vector3 worldX = t.TransformPoint(localX);
        Vector3 worldSheared = t.TransformPoint(localSheared);

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(origin, worldX);
        Gizmos.color = Color.magenta;
        Gizmos.DrawLine(origin, worldSheared);
        Gizmos.DrawLine(worldX, worldSheared);
    }
}
