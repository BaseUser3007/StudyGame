/// <summary>
/// Текущий запас энергии игрока. Тратится во время выполнения кода (например, на добычу).
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

    public void Publish()
    {
        UIEventBus.PublishPlayerEnergyChanged(Current, Max);
    }
}
