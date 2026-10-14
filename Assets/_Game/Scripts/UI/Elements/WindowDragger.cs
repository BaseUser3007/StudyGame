using System;
using UnityEngine;
using UnityEngine.UIElements;

public class WindowDragger
{
    private readonly VisualElement _window;
    private readonly VisualElement _header;

    private bool _isDragging;
    private Vector2 _dragOffset;
    private Vector2 _startPosition;

    // Позиция курсора в координатах панели — по ней проверяют, над чем отпустили окно
    public event Action<Vector2> OnDragMoved;
    public event Action<Vector2> OnDragEnded;

    public WindowDragger(VisualElement window, VisualElement header)
    {
        _window = window;
        _header = header;

        RegisterCallbacks();
    }

    /// <summary>
    /// Возвращает окно туда, откуда его начали тащить.
    /// </summary>
    public void ReturnToStart()
    {
        _window.style.left = _startPosition.x;
        _window.style.top = _startPosition.y;
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
        _startPosition = new Vector2(_window.resolvedStyle.left, _window.resolvedStyle.top);
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
        OnDragMoved?.Invoke(evt.position);
    }

    private void OnPointerUp(PointerUpEvent evt)
    {
        if (!_isDragging) return;
        _isDragging = false;
        _header.ReleasePointer(evt.pointerId);
        OnDragEnded?.Invoke(evt.position);
    }
}
