using System;
using System.Collections.Generic;

public static class UIEventBus
{
    // ─── Энергия игрока ───────────────────────────────────────
    public static event Action<float, float> OnPlayerEnergyChanged;

    // ─── Стоимость кода в окне ────────────────────────────────
    public static event Action<string, float> OnWindowEnergyCostChanged;

    // ─── Инвентарь ────────────────────────────────────────────
    public static event Action<IReadOnlyList<InventorySlot>> OnInventoryChanged;

    // ─── Уведомления ──────────────────────────────────────────
    public static event Action<string, LogEntryType> OnNotificationRequested;

    // ─── Publish ──────────────────────────────────────────────

    public static void PublishPlayerEnergyChanged(float current, float max)
        => OnPlayerEnergyChanged?.Invoke(current, max);

    public static void PublishWindowEnergyCostChanged(string windowId, float cost)
        => OnWindowEnergyCostChanged?.Invoke(windowId, cost);

    public static void PublishInventoryChanged(IReadOnlyList<InventorySlot> slots)
        => OnInventoryChanged?.Invoke(slots);

    public static void PublishNotification(string message, LogEntryType type)
        => OnNotificationRequested?.Invoke(message, type);
}