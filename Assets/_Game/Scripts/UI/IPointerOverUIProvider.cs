using UnityEngine;

/// <summary>
/// Знает, находится ли курсор над интерфейсом — чтобы колесо мыши
/// над окнами кода и панелями не зумило камеру.
/// </summary>
public interface IPointerOverUIProvider
{
    /// <summary>
    /// screenPosition — экранные координаты Input System (начало в левом нижнем углу).
    /// </summary>
    bool IsPointerOverUI(Vector2 screenPosition);
}
