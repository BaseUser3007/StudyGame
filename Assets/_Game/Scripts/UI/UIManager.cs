using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Единственная точка обновления UI.
/// Подписывается на UIEventBus и обновляет нужные элементы.
/// </summary>
public class UIManager : MonoBehaviour
{
    private TopBar _topBar;
    private Dictionary<string, CodeEditorWindowBase> _windows
        = new Dictionary<string, CodeEditorWindowBase>();

    // ─── Инициализация ────────────────────────────────────────

    public void Initialize(TopBar topBar)
    {
        _topBar = topBar;

        UIEventBus.OnPlayerEnergyChanged += HandlePlayerEnergyChanged;
        UIEventBus.OnWindowEnergyCostChanged += HandleWindowEnergyCostChanged;
        UIEventBus.OnInventoryChanged += HandleInventoryChanged;
        UIEventBus.OnNotificationRequested += HandleNotificationRequested;
    }

    private void OnDestroy()
    {
        UIEventBus.OnPlayerEnergyChanged -= HandlePlayerEnergyChanged;
        UIEventBus.OnWindowEnergyCostChanged -= HandleWindowEnergyCostChanged;
        UIEventBus.OnInventoryChanged -= HandleInventoryChanged;
        UIEventBus.OnNotificationRequested -= HandleNotificationRequested;
    }

    // ─── Регистрация окон ─────────────────────────────────────

    public void RegisterWindow(CodeEditorWindowBase window)
    {
        _windows[window.WindowId] = window;
    }

    public void UnregisterWindow(CodeEditorWindowBase window)
    {
        _windows.Remove(window.WindowId);
    }

    // ─── Handlers ─────────────────────────────────────────────

    private void HandlePlayerEnergyChanged(float current, float max)
    {
        _topBar?.UpdateEnergy(current, max);
    }

    private void HandleWindowEnergyCostChanged(string windowId, float cost)
    {
        if (_windows.TryGetValue(windowId, out CodeEditorWindowBase window))
        {
            if (window is IEnergyDisplay display)
                display.UpdateEnergyCost(cost);
        }
    }

    private void HandleInventoryChanged(Dictionary<string, int> items)
    {
        _topBar?.UpdateInventory(items);
    }

    private void HandleNotificationRequested(string message, LogEntryType type)
    {
        Debug.Log($"[{type}] {message}");
        // Позже заменим на всплывающее окно
    }
}