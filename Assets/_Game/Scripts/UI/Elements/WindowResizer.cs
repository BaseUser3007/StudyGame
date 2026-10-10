using UnityEngine;
using UnityEngine.UIElements;

public class WindowResizer
{
    private readonly VisualElement _window;
    private readonly VisualElement _resizeHandle;

    private bool _isResizing;
    private Vector2 _resizeStartMouse;
    private Vector2 _resizeStartSize;

    private const float MinWidth = 300f;
    private const float MinHeight = 200f;

    public WindowResizer(VisualElement window, VisualElement resizeHandle)
    {
        _window = window;
        _resizeHandle = resizeHandle;

        RegisterCallbacks();
    }

    private void RegisterCallbacks()
    {
        _resizeHandle.RegisterCallback<PointerDownEvent>(OnPointerDown);
        _resizeHandle.RegisterCallback<PointerMoveEvent>(OnPointerMove);
        _resizeHandle.RegisterCallback<PointerUpEvent>(OnPointerUp);
    }

    private void OnPointerDown(PointerDownEvent evt)
    {
        _isResizing = true;
        _resizeStartMouse = evt.position;

        // Берём актуальный размер в момент начала ресайза
        _resizeStartSize = new Vector2(
            _window.resolvedStyle.width > 0 ? _window.resolvedStyle.width : _window.style.width.value.value,
            _window.resolvedStyle.height > 0 ? _window.resolvedStyle.height : _window.style.height.value.value);

        _resizeHandle.CapturePointer(evt.pointerId);
    }

    private void OnPointerMove(PointerMoveEvent evt)
    {
        if (!_isResizing) return;

        float newWidth = _resizeStartSize.x + (evt.position.x - _resizeStartMouse.x);
        float newHeight = _resizeStartSize.y + (evt.position.y - _resizeStartMouse.y);

        newWidth = Mathf.Max(newWidth, MinWidth);
        newHeight = Mathf.Max(newHeight, MinHeight);

        _window.style.width = newWidth;
        _window.style.height = newHeight;
    }

    private void OnPointerUp(PointerUpEvent evt)
    {
        _isResizing = false;
        _resizeHandle.ReleasePointer(evt.pointerId);
    }
}