using UnityEngine;

/// <summary>
/// Ticks <see cref="EffectHandler"/> after the camera has moved.
/// </summary>
[DefaultExecutionOrder(20100)]
public sealed class EffectRuntimeHost : MonoBehaviour
{
    EffectHandler _handler;
    PlayerController _playerController;

    public void Init(EffectHandler handler, PlayerController playerController)
    {
        _handler = handler;
        _playerController = playerController;
    }

    void LateUpdate()
    {
        if (_handler == null)
            return;

        Camera camera = null;
        if (_playerController != null && _playerController.CameraController != null)
            camera = _playerController.CameraController.Camera;
        if (camera == null)
            camera = Camera.main;
        _handler.Tick(Time.deltaTime, camera);
    }
}
