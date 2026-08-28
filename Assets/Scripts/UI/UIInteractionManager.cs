using System.Collections.Generic;
using UnityEngine.UIElements;

public class UIInteractionManager : IUINotifyService
{
    private readonly HashSet<VisualElement> _hoveredElements = new();
    private VisualElement _draggedElement;

    private bool _isGameDragging;

    public bool IsPointerOverUI => !_isGameDragging && _hoveredElements.Count > 0;
    public bool IsDraggingUI => _draggedElement != null;
    public bool IsInteractingWithUI => IsDraggingUI || IsPointerOverUI;

    // DEV - remove later
    public IReadOnlyCollection<VisualElement> HoveredElements => _hoveredElements;
    public VisualElement DraggedElement => _draggedElement;

    public void NotifyHoverStart(VisualElement element) => _hoveredElements.Add(element);
    public void NotifyHoverEnd(VisualElement element) => _hoveredElements.Remove(element);
    public void NotifyGameDragStart() => _isGameDragging = true;
    public void NotifyGameDragEnd() => _isGameDragging = false;

    public void NotifyDragStart(VisualElement element) => _draggedElement = element;
    public void NotifyDragEnd(VisualElement element)
    {
        if (_draggedElement == element)
            _draggedElement = null;
    }
}

public interface IUINotifyService
{
    bool IsInteractingWithUI { get; }
    bool IsPointerOverUI { get; }
    bool IsDraggingUI { get; }

    // DEV - remove later
    IReadOnlyCollection<VisualElement> HoveredElements { get; }
    VisualElement DraggedElement { get; }

    void NotifyHoverStart(VisualElement element);
    void NotifyHoverEnd(VisualElement element);
    void NotifyDragStart(VisualElement element);
    void NotifyDragEnd(VisualElement element);
    void NotifyGameDragStart();
    void NotifyGameDragEnd();
}
