using UnityEngine;
using UnityEngine.UIElements;

public class WindowDragger
{
    private readonly VisualElement _window;
    private readonly VisualElement _header;

    private bool _isDragging;
    private Vector2 _dragOffset;

    public WindowDragger(VisualElement window, VisualElement header)
    {
        _window = window;
        _header = header;

        RegisterCallbacks();
    }

    private void RegisterCallbacks()
    {
        _header.RegisterCallback<PointerDownEvent>(OnPointerDown);
        _header.RegisterCallback<PointerMoveEvent>(OnPointerMove);
        _header.RegisterCallback<PointerUpEvent>(OnPointerUp);
    }

    private void OnPointerDown(PointerDownEvent evt)
    {
        _isDragging = true;
        _dragOffset = new Vector2(
            evt.position.x - _window.resolvedStyle.left,
            evt.position.y - _window.resolvedStyle.top);
        _header.CapturePointer(evt.pointerId);
    }

    private void OnPointerMove(PointerMoveEvent evt)
    {
        if (!_isDragging) return;
        _window.style.left = evt.position.x - _dragOffset.x;
        _window.style.top = evt.position.y - _dragOffset.y;
    }

    private void OnPointerUp(PointerUpEvent evt)
    {
        _isDragging = false;
        _header.ReleasePointer(evt.pointerId);
    }
}