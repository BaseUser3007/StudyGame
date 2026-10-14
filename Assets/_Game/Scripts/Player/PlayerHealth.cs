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

    public void Heal(float amount)
    {
        if (IsDead || amount <= 0f) return;

        Current = Mathf.Min(Max, Current + amount);
        Publish();
    }

    public void Publish()
    {
        UIEventBus.PublishPlayerHealthChanged(Current, Max);
    }
}
