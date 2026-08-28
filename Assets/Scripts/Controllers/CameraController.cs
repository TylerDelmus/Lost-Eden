using Reflex.Attributes;
using UnityEngine;

[DefaultExecutionOrder(20000)]
public class CameraController : MonoBehaviour
{
    [SerializeField]
    [Range(0f, 1f)]
    private float _reorientSharpness;

    [Header("Rotation Settings")]
    [SerializeField]
    [Range(-90f, 90f)]
    private float _minPitch = -80f;

    [SerializeField]
    [Range(-90f, 90f)]
    private float _maxPitch = 80f;

    [SerializeField]
    private float _rotationSpeed = 120f;

    [SerializeField]
    private float _rotationSharpness = 25f;

    [Header("Follow Settings")]

    [SerializeField]
    private float _followSharpness = 10f;

    [SerializeField]
    private float _distanceSharpness = 10f;

    [SerializeField]
    private float _defaultFollowDistance = 2f;

    [Header("Zoom Settings")]

    [SerializeField]
    private float _minFollowDistance = 1f;

    [SerializeField]
    private float _maxFollowDistance = 15f;

    [SerializeField]
    private float _zoomStep = 2f;

    private float _targetFollowDistance;
    private float _currentFollowDistance;

    [SerializeField]
    private LayerMask _aimLayerMask;

    private EulerAngles _currentAngles = new EulerAngles();
    private EulerAngles _targetAngles = new EulerAngles();

    private Vector3 _lookInput;
    private float _zoomDelta;
    private bool _isMovingForward;
    private bool _leftMouseHeld;
    private bool _rightClickHeld;
    private Transform _followRoot;
    private Attractor _headAttractor;
    private Character _pendingTarget;

    [SerializeField]
    internal Camera Camera;

    [Inject]
    IUINotifyService _uiNotifyService;

    private void Awake()
    {
        _targetFollowDistance = _defaultFollowDistance;
        _currentFollowDistance = _defaultFollowDistance;
    }

    internal void SetInputs(ActorInput playerInput)
    {
        _lookInput = playerInput.LookInput;
        _zoomDelta = playerInput.ZoomDelta;
        _leftMouseHeld = playerInput.LeftClickHeld;
        _rightClickHeld = playerInput.RightClickHeld;
        _isMovingForward = playerInput.IsMovingForward;
    }

    private void LateUpdate()
    {
        if (_followRoot == null)
            TryResolveTarget();

        if (_followRoot == null || _headAttractor == null)
            return;

        float deltaTime = Time.deltaTime;
        UpdateZoom();
        UpdateRotation(deltaTime);
        UpdatePosition(deltaTime);
    }

    internal void SetTarget(Character character)
    {
        _pendingTarget = character;
        TryResolveTarget();
    }

    void TryResolveTarget()
    {
        if (_pendingTarget == null)
            return;

        if (!_pendingTarget.TryGetAttractor(AttractorPlace.Head, out _headAttractor))
            return;

        _followRoot = _pendingTarget.transform;
        _pendingTarget = null;

        Vector3 followPos = GetFollowPosition();
        Vector3 directionToCamera = (transform.position - followPos).normalized;
        _currentAngles.Yaw = _targetAngles.Yaw = Mathf.Atan2(directionToCamera.x, directionToCamera.z) * Mathf.Rad2Deg;
        _currentAngles.Pitch = _targetAngles.Pitch = Mathf.Asin(directionToCamera.y) * Mathf.Rad2Deg;
    }

    internal void ClearTarget()
    {
        _followRoot = null;
        _headAttractor = null;
        _pendingTarget = null;
    }

    Vector3 GetFollowPosition()
    {
        Vector3 rootPos = _followRoot.position;
        return new Vector3(rootPos.x, _headAttractor.transform.position.y, rootPos.z);
    }

    internal void SetFreePose(Vector3 position, Vector3 eulerAngles)
    {
        ClearTarget();
        transform.SetPositionAndRotation(position, Quaternion.Euler(eulerAngles));
        _currentAngles.Pitch = _targetAngles.Pitch = eulerAngles.x;
        _currentAngles.Yaw = _targetAngles.Yaw = eulerAngles.y;
    }

    private void UpdateZoom()
    {
        if (_zoomDelta == 0f)
            return;

        float scrollNormalized = Mathf.Sign(_zoomDelta);
        _targetFollowDistance -= scrollNormalized * _zoomStep;
        _targetFollowDistance = Mathf.Clamp(_targetFollowDistance, _minFollowDistance, _maxFollowDistance);
    }

    private void UpdateRotation(float deltaTime)
    {
        bool isHoldingClick = _leftMouseHeld || _rightClickHeld;
        float rotSharpness = _rotationSharpness;

        if (!isHoldingClick || _uiNotifyService.IsInteractingWithUI)
        {
            if (_isMovingForward)
            {
                Vector3 characterForward = _followRoot.forward;
                Vector3 characterForwardFlat = new Vector3(characterForward.x, 0f, characterForward.z).normalized;
                float targetYaw = Mathf.Atan2(characterForwardFlat.x, characterForwardFlat.z) * Mathf.Rad2Deg;
                _targetAngles.Yaw = targetYaw;
                rotSharpness = _rotationSharpness * _reorientSharpness;
            }

            var sharpness = 1f - Mathf.Exp(-rotSharpness * deltaTime);
            _currentAngles.Yaw = Mathf.LerpAngle(_currentAngles.Yaw, _targetAngles.Yaw, sharpness);
            _currentAngles.Pitch = Mathf.Lerp(_currentAngles.Pitch, _targetAngles.Pitch, sharpness);
        }
        else
        {
            _targetAngles.Yaw += _lookInput.x * _rotationSpeed * deltaTime;
            _targetAngles.Pitch -= _lookInput.y * _rotationSpeed * deltaTime;
            _targetAngles.Pitch = Mathf.Clamp(_targetAngles.Pitch, _minPitch, _maxPitch);

            _currentAngles.Yaw = _targetAngles.Yaw;
            _currentAngles.Pitch = _targetAngles.Pitch;
        }

        transform.rotation = Quaternion.Euler(_currentAngles.Pitch, _currentAngles.Yaw, 0f);
    }

    private void UpdatePosition(float deltaTime)
    {
        Vector3 cameraDirection = Quaternion.Euler(_currentAngles.Pitch, _currentAngles.Yaw, 0f) * Vector3.back;
        Vector3 followTargetPos = GetFollowPosition();

        if (Physics.SphereCast(followTargetPos, 0.3f, -Camera.transform.forward, out RaycastHit hit, _targetFollowDistance, _aimLayerMask))
        {
            _currentFollowDistance = Vector3.Distance(hit.point, followTargetPos);
        }
        else
        {
            _currentFollowDistance = Mathf.Lerp(_currentFollowDistance, _targetFollowDistance, 1f - Mathf.Exp(-_distanceSharpness * deltaTime));
        }

        Vector3 desiredPosition = followTargetPos + cameraDirection * _currentFollowDistance;

        transform.position = Vector3.Lerp(transform.position, desiredPosition, 1f - Mathf.Exp(-_followSharpness * deltaTime));
    }

    public Vector2 GetViewAngles()
    {
        return new Vector2(_currentAngles.Pitch, _currentAngles.Yaw);
    }
}