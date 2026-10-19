using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Параметры одного забега. Собираются при старте из базовых значений PlayerConfig
/// и эффектов построек хаба (HubBuildingDefinition.Effects) — системы забега читают их отсюда, а не из PlayerConfig напрямую.
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
    /// <summary>Сколько здоровья лечит ночь у спального места.</summary>
    public float SleepHeal { get; private set; }
    public IReadOnlyList<ItemStack> StartingItems => _startingItems;

    private readonly List<ItemStack> _startingItems = new List<ItemStack>();

    private RunModifiers() { }

    /// <summary>
    /// Параметры забега: база из PlayerConfig + эффекты построек хаба из progress.HubBuildings,
    /// найденных в catalog (без каталога — только база; постройки не из каталога пропускаются).
    /// </summary>
    public static RunModifiers Create(PlayerConfig config, MetaProgress progress, HubBuildingCatalog catalog = null)
    {
        var modifiers = new RunModifiers
        {
            BaseEnergyLimit = Mathf.Max(0f, config.baseEnergyLimit),
            MaxHealth = Mathf.Max(1f, config.maxHealth),
            InventorySlots = Mathf.Max(1, config.inventorySlots),
            RunsPerDay = Mathf.Max(1, config.runsPerDay),
            ThreatDay = Mathf.Max(1, config.threatDay),
            SleepHeal = Mathf.Max(0f, config.sleepHealAmount)
        };

        foreach (ItemStack stack in config.startingItems)
            if (stack.item != null && stack.amount > 0)
                modifiers._startingItems.Add(stack);

        if (progress != null && catalog != null)
            modifiers.ApplyHubBuildings(progress, catalog);

        return modifiers;
    }

    // ─── Бонусы во время забега ───────────────────────────────

    /// <summary>+к запасу энергии на день до конца забега (награда головоломки). DayCycle читает его каждый день.</summary>
    public void AddEnergyLimit(float amount)
    {
        BaseEnergyLimit = Mathf.Max(0f, BaseEnergyLimit + amount);
    }

    /// <summary>+к максимуму здоровья до конца забега (само здоровье меняет вызывающий через PlayerHealth.SetMax).</summary>
    public void AddMaxHealth(float amount)
    {
        MaxHealth = Mathf.Max(1f, MaxHealth + amount);
    }

    /// <summary>Отодвинуть мировую угрозу на days дней (DayCycle читает ThreatDay отсюда).</summary>
    public void DelayThreat(int days)
    {
        ThreatDay = Mathf.Max(1, ThreatDay + days);
    }

    // ─── Постройки хаба ───────────────────────────────────────

    private void ApplyHubBuildings(MetaProgress progress, HubBuildingCatalog catalog)
    {
        var applied = new HashSet<string>(System.StringComparer.Ordinal);

        foreach (PlacedHubBuilding placed in progress.HubBuildings)
        {
            // Каждая постройка действует один раз, даже если в файле оказалась дважды
            if (placed == null || !applied.Add(placed.buildingId)) continue;

            HubBuildingDefinition definition = catalog.Find(placed.buildingId);
            if (definition == null) continue;

            foreach (HubBuildingEffect effect in definition.Effects)
                if (effect != null)
                    Apply(effect);
        }
    }

    private void Apply(HubBuildingEffect effect)
    {
        int whole = Mathf.RoundToInt(effect.amount);
        switch (effect.type)
        {
            case HubEffectType.EnergyLimit:
                BaseEnergyLimit = Mathf.Max(0f, BaseEnergyLimit + effect.amount);
                break;
            case HubEffectType.ThreatDays:
                ThreatDay = Mathf.Max(1, ThreatDay + whole);
                break;
            case HubEffectType.InventorySlots:
                InventorySlots = Mathf.Max(1, InventorySlots + whole);
                break;
            case HubEffectType.MaxHealth:
                MaxHealth = Mathf.Max(1f, MaxHealth + effect.amount);
                break;
            case HubEffectType.RunsPerDay:
                RunsPerDay = Mathf.Max(1, RunsPerDay + whole);
                break;
            default:
                Debug.LogWarning($"RunModifiers: unsupported hub effect {effect.type}");
                break;
        }
    }
}
