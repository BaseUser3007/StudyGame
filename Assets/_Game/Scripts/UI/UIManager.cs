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
    // Куда выводить уведомления (в забеге — лог главного окна, в лагере — строка под панелью построек); null — только в консоль
    private INotificationDisplay _notificationDisplay;
    // Куда выводить ошибки запуска с объяснением (лог главного окна); null — как обычное уведомление
    private ICodeErrorDisplay _codeErrorDisplay;
    private Dictionary<string, CodeEditorWindowBase> _windows
        = new Dictionary<string, CodeEditorWindowBase>();

    // ─── Инициализация ────────────────────────────────────────

    public void Initialize(TopBar topBar, InventoryBar inventoryBar, CraftingPanel craftingPanel)
    {
        _topBar = topBar;
        _inventoryBar = inventoryBar;
        _craftingPanel = craftingPanel;

        UIEventBus.OnPlayerEnergyChanged += HandlePlayerEnergyChanged;
        UIEventBus.OnPlayerHealthChanged += HandlePlayerHealthChanged;
        UIEventBus.OnWindowEnergyCostChanged += HandleWindowEnergyCostChanged;
        UIEventBus.OnInventoryChanged += HandleInventoryChanged;
        UIEventBus.OnNotificationRequested += HandleNotificationRequested;
        UIEventBus.OnCodeErrorReported += HandleCodeErrorReported;
        UIEventBus.OnDayChanged += HandleDayChanged;
    }

    private void OnDestroy()
    {
        UIEventBus.OnPlayerEnergyChanged -= HandlePlayerEnergyChanged;
        UIEventBus.OnPlayerHealthChanged -= HandlePlayerHealthChanged;
        UIEventBus.OnWindowEnergyCostChanged -= HandleWindowEnergyCostChanged;
        UIEventBus.OnInventoryChanged -= HandleInventoryChanged;
        UIEventBus.OnNotificationRequested -= HandleNotificationRequested;
        UIEventBus.OnCodeErrorReported -= HandleCodeErrorReported;
        UIEventBus.OnDayChanged -= HandleDayChanged;
    }

    // ─── Уведомления ──────────────────────────────────────────

    /// <summary>Элемент, который показывает уведомления UIEventBus (null — снять).</summary>
    public void SetNotificationDisplay(INotificationDisplay display)
    {
        _notificationDisplay = display;
    }

    /// <summary>Элемент, который показывает ошибки запуска с объяснением (null — снять).</summary>
    public void SetCodeErrorDisplay(ICodeErrorDisplay display)
    {
        _codeErrorDisplay = display;
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

    private void HandlePlayerHealthChanged(float current, float max)
    {
        _topBar?.UpdateHealth(current, max);
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
        // В забеге — лог главного окна, в лагере — строка под панелью построек
        _notificationDisplay?.ShowNotification(message, type);
    }

    private void HandleCodeErrorReported(LuaErrorExplanation error)
    {
        if (error == null) return;
        Debug.Log($"[{LogEntryType.Error}] {error.Raw}");

        if (_codeErrorDisplay != null)
            _codeErrorDisplay.ShowCodeError(error);
        else
            _notificationDisplay?.ShowNotification(error.Raw, LogEntryType.Error);

        // Номер строки с ошибкой выделяется в окне, где она случилась
        if (!error.HasLine) return;
        foreach (CodeEditorWindowBase window in _windows.Values)
            if (window.Title == error.Window)
                window.SetErrorLine(error.Line);
    }
}
