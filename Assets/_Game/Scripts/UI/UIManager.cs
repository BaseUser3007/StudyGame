using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Единственная точка обновления UI.
/// Подписывается на UIEventBus и обновляет нужные элементы.
/// </summary>
public class UIManager : MonoBehaviour
{
    private TopBar _topBar;
    private InventoryBar _inventoryBar;
    private CraftingPanel _craftingPanel;
    private Dictionary<string, CodeEditorWindowBase> _windows
        = new Dictionary<string, CodeEditorWindowBase>();

    // ─── Инициализация ────────────────────────────────────────

    public void Initialize(TopBar topBar, InventoryBar inventoryBar, CraftingPanel craftingPanel)
    {
        _topBar = topBar;
        _inventoryBar = inventoryBar;
        _craftingPanel = craftingPanel;

        UIEventBus.OnPlayerEnergyChanged += HandlePlayerEnergyChanged;
        UIEventBus.OnWindowEnergyCostChanged += HandleWindowEnergyCostChanged;
        UIEventBus.OnInventoryChanged += HandleInventoryChanged;
        UIEventBus.OnNotificationRequested += HandleNotificationRequested;
        UIEventBus.OnDayChanged += HandleDayChanged;
    }

    private void OnDestroy()
    {
        UIEventBus.OnPlayerEnergyChanged -= HandlePlayerEnergyChanged;
        UIEventBus.OnWindowEnergyCostChanged -= HandleWindowEnergyCostChanged;
        UIEventBus.OnInventoryChanged -= HandleInventoryChanged;
        UIEventBus.OnNotificationRequested -= HandleNotificationRequested;
        UIEventBus.OnDayChanged -= HandleDayChanged;
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

    private void HandleDayChanged(DayInfo info)
    {
        _topBar?.UpdateDay(info);
    }

    private void HandleInventoryChanged(IReadOnlyList<InventorySlot> slots)
    {
        _inventoryBar?.UpdateSlots(slots);
        _craftingPanel?.UpdateInventory(slots);
    }

    private void HandleNotificationRequested(string message, LogEntryType type)
    {
        Debug.Log($"[{type}] {message}");
        // Позже заменим на всплывающее окно
    }
}