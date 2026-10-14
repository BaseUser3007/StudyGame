using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Параметры одного забега. Собираются при старте из базовых значений PlayerConfig
/// и построек хаба (этап 4) — системы забега читают их отсюда, а не из PlayerConfig напрямую.
/// </summary>
public class RunModifiers
{
    /// <summary>Запас энергии на день без бонусов и штрафов.</summary>
    public float BaseEnergyLimit { get; private set; }
    public float MaxHealth { get; private set; }
    public int InventorySlots { get; private set; }
    public int RunsPerDay { get; private set; }
    /// <summary>День, с которого начинается мировая угроза.</summary>
    public int ThreatDay { get; private set; }
    public IReadOnlyList<ItemStack> StartingItems => _startingItems;

    private readonly List<ItemStack> _startingItems = new List<ItemStack>();

    private RunModifiers() { }

    public static RunModifiers Create(PlayerConfig config, MetaProgress progress)
    {
        var modifiers = new RunModifiers
        {
            BaseEnergyLimit = Mathf.Max(0f, config.baseEnergyLimit),
            MaxHealth = Mathf.Max(1f, config.maxHealth),
            InventorySlots = Mathf.Max(1, config.inventorySlots),
            RunsPerDay = Mathf.Max(1, config.runsPerDay),
            ThreatDay = Mathf.Max(1, config.threatDay)
        };

        foreach (ItemStack stack in config.startingItems)
            if (stack.item != null && stack.amount > 0)
                modifiers._startingItems.Add(stack);

        // Бонусы построек хаба (progress.HubBuildings) добавятся на этапе 4
        return modifiers;
    }
}
