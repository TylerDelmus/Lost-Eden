using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

public class UIInteractionManager : IUINotifyService
{
    private readonly List<Func<Vector2, bool>> _hitTests = new();
    private VisualElement _draggedElement;

    private bool _isGameDragging;

    // Asked fresh on every read, not tracked through enter/leave events: a missed leave (a window
    // closed under the cursor, the cursor warped back after a camera drag) would otherwise leave
    // the pointer "over UI" until something happened to send that window another leave.
    public bool IsPointerOverUI => !_isGameDragging && PointerHitsUI();
    public bool IsDraggingUI => _draggedElement != null;
    public bool IsInteractingWithUI => IsDraggingUI || IsPointerOverUI;

    // DEV - remove later
    public VisualElement DraggedElement => _draggedElement;

    public void AddHitTest(Func<Vector2, bool> hitTest) => _hitTests.Add(hitTest);
    public void RemoveHitTest(Func<Vector2, bool> hitTest) => _hitTests.Remove(hitTest);
    public void NotifyGameDragStart() => _isGameDragging = true;
    public void NotifyGameDragEnd() => _isGameDragging = false;

    public void NotifyDragStart(VisualElement element) => _draggedElement = element;
    public void NotifyDragEnd(VisualElement element)
    {
        if (_draggedElement == element)
            _draggedElement = null;
    }

    private bool PointerHitsUI()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null)
            return false;

        Vector2 screen = mouse.position.ReadValue();
        for (int i = 0; i < _hitTests.Count; i++)
            if (_hitTests[i](screen))
                return true;
        return false;
    }
}

public interface IUINotifyService
{
    bool IsInteractingWithUI { get; }
    bool IsPointerOverUI { get; }
    bool IsDraggingUI { get; }

    // DEV - remove later
    VisualElement DraggedElement { get; }

    /// <summary>
    /// Adds a test for whether a screen point (Input System coordinates, origin bottom-left) is
    /// over UI that should keep clicks and the wheel from the world.
    /// </summary>
    void AddHitTest(Func<Vector2, bool> hitTest);
    void RemoveHitTest(Func<Vector2, bool> hitTest);
    void NotifyDragStart(VisualElement element);
    void NotifyDragEnd(VisualElement element);
    void NotifyGameDragStart();
    void NotifyGameDragEnd();
}
