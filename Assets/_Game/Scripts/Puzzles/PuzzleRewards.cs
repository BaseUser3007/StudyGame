using UnityEngine;

/// <summary>
/// Награды головоломок (GameDesign.md §10.4), все — до конца забега:
///   • sum — BaseEnergyLimit +energyLimitBonus и столько же энергии сейчас (максимум сегодня тоже растёт);
///   • max — MaxHealth +maxHealthBonus, здоровье до нового максимума;
///   • count — угроза на threatDelayDays позже, если ещё не началась, иначе как sum;
///   • maze — crystal × crystalAmount в инвентарь (не влезло — на землю в центре лабиринта).
/// Без конфига — значения по умолчанию из PuzzleConfig.
/// </summary>
public class PuzzleRewards
{
    private readonly PuzzleConfig _config;
    private readonly RunModifiers _modifiers;
    private readonly PlayerEnergy _energy;
    private readonly PlayerHealth _health;
    private readonly DayCycle _dayCycle;
    private readonly PlayerInventory _inventory;
    private readonly ObjectManager _objects;

    public PuzzleRewards(
        PuzzleConfig config, RunModifiers modifiers, PlayerEnergy energy, PlayerHealth health, DayCycle dayCycle,
        PlayerInventory inventory, ObjectManager objects)
    {
        _config = config != null ? config : ScriptableObject.CreateInstance<PuzzleConfig>();
        _modifiers = modifiers;
        _energy = energy;
        _health = health;
        _dayCycle = dayCycle;
        _inventory = inventory;
        _objects = objects;
    }

    // ─── Public API ───────────────────────────────────────────

    /// <summary>Выдаёт награду задачи на код. Возвращает текст для уведомления (англ.).</summary>
    public string ApplyCodeTask(CodeTaskKind kind)
    {
        switch (kind)
        {
            case CodeTaskKind.Max:
                return ApplyHealthBonus();

            case CodeTaskKind.Count:
                if (_dayCycle != null && !_dayCycle.IsThreatActive && _config.threatDelayDays > 0)
                {
                    _modifiers.DelayThreat(_config.threatDelayDays);
                    _dayCycle.Publish();
                    int days = _config.threatDelayDays;
                    return $"The threat is delayed by {days} day{(days > 1 ? "s" : "")} (day {_dayCycle.ThreatDay}).";
                }
                return ApplyEnergyBonus();

            default:
                return ApplyEnergyBonus();
        }
    }

    /// <summary>
    /// Награда лабиринта: кристаллы в инвентарь, остаток — на землю в точку dropPosition (если у предмета
    /// есть worldPrefab). Возвращает текст для уведомления.
    /// </summary>
    public string ApplyMaze(Vector2 dropPosition)
    {
        ResourceItem crystal = _config.crystalItem;
        int amount = _config.crystalAmount;
        if (crystal == null || amount <= 0)
        {
            Debug.LogWarning("PuzzleRewards: PuzzleConfig.crystalItem is not assigned — the maze gives nothing");
            return "The chest is empty.";
        }

        int added = _inventory != null ? _inventory.Add(crystal, amount) : 0;
        int left = amount - added;
        int dropped = 0;
        if (left > 0 && crystal.worldPrefab != null && _objects != null)
        {
            for (int i = 0; i < left; i++)
            {
                // Небольшой разброс вокруг центра, чтобы кристаллы не лежали друг в друге
                Vector2 offset = new Vector2((i % 2 == 0 ? -0.2f : 0.2f), (i / 2) * 0.2f - 0.1f);
                ResourceObject obj = _objects.Spawn(crystal.worldPrefab, dropPosition + offset);
                if (obj == null) continue;
                obj.SpawnRunId = GameEventBus.ActiveRunId;
                dropped++;
            }
        }

        string name = crystal.DisplayName;
        if (left <= 0) return $"Found {amount} {name} in the chest.";
        if (dropped > 0) return $"Found {amount} {name}: {added} taken, {dropped} on the ground (inventory is full).";
        return $"Found {amount} {name}, but the inventory is full — {left} lost.";
    }

    // ─── Helpers ──────────────────────────────────────────────

    private string ApplyEnergyBonus()
    {
        float bonus = _config.energyLimitBonus;
        _modifiers.AddEnergyLimit(bonus);
        // Сегодняшний максимум растёт сразу, следующие дни DayCycle считает от нового BaseEnergyLimit
        _energy.SetMax(_energy.Max + bonus, refill: false);
        _energy.Restore(bonus);
        _dayCycle?.Publish();
        return $"Max energy +{bonus:0.#} for this run, +{bonus:0.#} energy now.";
    }

    private string ApplyHealthBonus()
    {
        float bonus = _config.maxHealthBonus;
        _modifiers.AddMaxHealth(bonus);
        _health.SetMax(_health.Max + bonus, healToFull: true);
        return $"Max health +{bonus:0.#} for this run, health restored.";
    }
}
