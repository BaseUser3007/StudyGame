using System;
using UnityEngine;

/// <summary>
/// Здоровье игрока на забег (отдельно от энергии). Урон наносят враги, лечит еда.
/// При 0 — поражение (OnDied, один раз): забег заканчивается, материалы забега теряются.
/// </summary>
public class PlayerHealth
{
    public event Action OnDied;

    public float Current { get; private set; }
    public float Max { get; private set; }
    public bool IsDead { get; private set; }

    public PlayerHealth(float max)
    {
        Max = Mathf.Max(1f, max);
        Current = Max;
    }

    // ─── Public API ───────────────────────────────────────────

    public void TakeDamage(float amount)
    {
        if (IsDead || amount <= 0f) return;

        Current = Mathf.Max(0f, Current - amount);
        Publish();

        if (Current <= 0f)
        {
            IsDead = true;
            OnDied?.Invoke();
        }
    }

    /// <summary>
    /// Лечит не выше максимума. Возвращает, сколько здоровья реально добавлено (мёртвого не лечит).
    /// </summary>
    public float Heal(float amount)
    {
        if (IsDead || amount <= 0f) return 0f;

        float healed = Mathf.Min(amount, Mathf.Max(0f, Max - Current));
        if (healed <= 0f) return 0f;

        Current += healed;
        Publish();
        return healed;
    }

    /// <summary>
    /// Новый максимум (бонус на забег). healToFull — вылечить до нового максимума (мёртвого не лечит).
    /// </summary>
    public void SetMax(float max, bool healToFull)
    {
        Max = Mathf.Max(1f, max);
        Current = healToFull && !IsDead ? Max : Mathf.Min(Current, Max);
        Publish();
    }

    public void Publish()
    {
        UIEventBus.PublishPlayerHealthChanged(Current, Max);
    }
}
