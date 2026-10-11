using UnityEngine;

/// <summary>
/// Запас энергии игрока на текущий день. Тратится во время выполнения кода
/// (конструкции языка, команды, добыча); в начале дня восстанавливается (DayCycle).
/// </summary>
public class PlayerEnergy
{
    public float Current { get; private set; }
    public float Max { get; private set; }

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
        Publish();
        return true;
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
