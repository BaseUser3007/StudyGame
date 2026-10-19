using UnityEngine;

/// <summary>
/// Запас энергии игрока на текущий день. Тратится во время выполнения кода
/// (конструкции языка, команды, добыча); в начале дня восстанавливается (DayCycle).
/// </summary>
public class PlayerEnergy
{
    public float Current { get; private set; }
    public float Max { get; private set; }
    /// <summary>
    /// Сколько всего потрачено с начала загрузки сцены — разница двух значений даёт расход
    /// за отрезок (запуск кода, окно достижения «собрать за ≤ N энергии»).
    /// </summary>
    public float TotalSpent { get; private set; }

    public PlayerEnergy(float max)
    {
        Max = max;
        Current = max;
    }

    // ─── Public API ───────────────────────────────────────────

    public bool CanSpend(float amount) => amount <= Current;

    /// <summary>
    /// Списывает энергию. Возвращает false и ничего не списывает, если энергии не хватает.
    /// </summary>
    public bool TrySpend(float amount)
    {
        if (!CanSpend(amount)) return false;

        Current -= amount;
        TotalSpent += amount;
        Publish();
        return true;
    }

    /// <summary>
    /// Возвращает списанное, если действие после оплаты не удалось (крафт без места в инвентаре).
    /// </summary>
    public void Refund(float amount)
    {
        if (amount <= 0f) return;

        Current = Mathf.Min(Max, Current + amount);
        TotalSpent = Mathf.Max(0f, TotalSpent - amount);
        Publish();
    }

    /// <summary>
    /// Добавляет энергию (еда) не выше максимума. В отличие от Refund, не уменьшает TotalSpent:
    /// съеденное — новая энергия, а не отмена траты. Возвращает, сколько реально добавлено.
    /// </summary>
    public float Restore(float amount)
    {
        if (amount <= 0f) return 0f;

        float restored = Mathf.Min(amount, Mathf.Max(0f, Max - Current));
        if (restored <= 0f) return 0f;

        Current += restored;
        Publish();
        return restored;
    }

    /// <summary>
    /// Новый максимум (начало дня). refill — заполнить запас до нового максимума.
    /// </summary>
    public void SetMax(float max, bool refill)
    {
        Max = Mathf.Max(0f, max);
        Current = refill ? Max : Mathf.Min(Current, Max);
        Publish();
    }

    public void Publish()
    {
        UIEventBus.PublishPlayerEnergyChanged(Current, Max);
    }
}
