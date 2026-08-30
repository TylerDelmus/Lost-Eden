using UnityEngine;
using UnityEngine.UIElements;

public abstract class TrackTransformView
{
    internal VisualElement Root;
    protected Vector3 Offset = Vector3.zero;
    VisualElement _trackRoot;
    int _lastPx = int.MinValue;
    int _lastPy = int.MinValue;
    bool _lastVisible;

    protected TrackTransformView(VisualTreeAsset viewAsset)
    {
        Root = viewAsset.Instantiate();
        _trackRoot = Root.Q<VisualElement>("TrackRoot") ?? Root;
        _trackRoot.style.position = Position.Absolute;
        _trackRoot.style.left = 0;
        _trackRoot.style.top = 0;
    }

    internal abstract void UpdatePos(Camera camera);

    protected virtual void Init(Vector3 offset)
    {
        Offset = offset;
        _lastPx = int.MinValue;
        _lastPy = int.MinValue;
        _lastVisible = false;
    }

    protected void UpdatePos(Vector3 worldPos, Camera camera)
    {
        if (camera == null || _trackRoot?.panel == null)
        {
            SetVisible(false);
            return;
        }

        Vector3 screenPoint = camera.WorldToScreenPoint(worldPos + Offset);
        if (screenPoint.z <= 0f)
        {
            SetVisible(false);
            return;
        }

        Vector2 panelPoint = RuntimePanelUtils.CameraTransformWorldToPanel(
            _trackRoot.panel, worldPos + Offset, camera);

        float panelW = _trackRoot.panel.visualTree.layout.width;
        float panelH = _trackRoot.panel.visualTree.layout.height;
        if (panelW > 0f && panelH > 0f
            && (panelPoint.x < 0f || panelPoint.y < 0f || panelPoint.x > panelW || panelPoint.y > panelH))
        {
            SetVisible(false);
            return;
        }

        int px = Mathf.RoundToInt(panelPoint.x);
        int py = Mathf.RoundToInt(panelPoint.y);

        if (_lastVisible && px == _lastPx && py == _lastPy)
            return;

        _lastPx = px;
        _lastPy = py;
        _trackRoot.style.translate = new Translate(px, py);
        SetVisible(true);
    }

    void SetVisible(bool visible)
    {
        if (_lastVisible == visible)
            return;

        _lastVisible = visible;
        _trackRoot.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
