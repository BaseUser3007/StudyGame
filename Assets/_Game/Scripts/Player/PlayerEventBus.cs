using System;
using UnityEngine;

/// <summary>
/// Статическая шина событий игрока.
/// Системы подписываются на события не зная друг о друге.
/// </summary>
public static class PlayerEventBus
{
    public static event Action<Vector2Int> OnPlayerMoved;
    public static event Action OnDayStarted;
    public static event Action OnUIFocused;
    public static event Action OnUIUnfocused;
    public static event Action OnCameraMoving;
    public static event Action OnCameraStopped;

    public static void PublishCameraMoving() => OnCameraMoving?.Invoke();
    public static void PublishCameraStopped() => OnCameraStopped?.Invoke();

    public static void PublishUIFocused()
    {
        OnUIFocused?.Invoke();
    }

    public static void PublishUIUnfocused()
    {
        OnUIUnfocused?.Invoke();
    }

    public static event Action OnDayEnded;

    public static void PublishPlayerMoved(Vector2Int newTile)
    {
        OnPlayerMoved?.Invoke(newTile);
    }

    public static void PublishDayStarted()
    {
        OnDayStarted?.Invoke();
    }

    public static void PublishDayEnded()
    {
        OnDayEnded?.Invoke();
    }
}