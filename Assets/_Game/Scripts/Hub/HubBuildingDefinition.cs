using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Что меняет постройка хаба в <see cref="RunModifiers"/>.
/// Unity пишет enum в ассеты числом — новые значения добавляйте только в конец.
/// </summary>
public enum HubEffectType
{
    /// <summary>+ к RunModifiers.BaseEnergyLimit (запас энергии на день).</summary>
    EnergyLimit,
    /// <summary>+ дней до мировой угрозы (RunModifiers.ThreatDay).</summary>
    ThreatDays,
    /// <summary>+ слотов инвентаря (RunModifiers.InventorySlots).</summary>
    InventorySlots,
    /// <summary>+ к максимуму здоровья (RunModifiers.MaxHealth).</summary>
    MaxHealth,
    /// <summary>+ запусков кода в день (RunModifiers.RunsPerDay).</summary>
    RunsPerDay
}

/// <summary>
/// Один эффект постройки: тип и величина (для целочисленных параметров округляется).
/// </summary>
[Serializable]
public class HubBuildingEffect
{
    public HubEffectType type = HubEffectType.EnergyLimit;
    [Tooltip("Прибавка к параметру забега. Слоты, дни и запуски округляются до целого")]
    public float amount = 1f;

    /// <summary>Описание для UI (UI игры на английском), например "+10 energy limit".</summary>
    public string Describe()
    {
        string value = amount >= 0f ? $"+{FormatAmount()}" : FormatAmount();
        switch (type)
        {
            // Тексты для UI — на языке игры (шаблоны в LocRuContent)
            case HubEffectType.EnergyLimit: return Loc.F("{0} energy limit", value);
            case HubEffectType.ThreatDays: return Loc.F("{0} day(s) before the threat", value);
            case HubEffectType.InventorySlots: return Loc.F("{0} inventory slot(s)", value);
            case HubEffectType.MaxHealth: return Loc.F("{0} max health", value);
            case HubEffectType.RunsPerDay: return Loc.F("{0} code run(s) per day", value);
            default: return $"{value} {type}";
        }
    }

    private string FormatAmount() =>
        IsInteger(type) ? Mathf.RoundToInt(amount).ToString() : amount.ToString("0.##");

    /// <summary>Параметр забега целочисленный (слоты, дни, запуски).</summary>
    public static bool IsInteger(HubEffectType type) =>
        type == HubEffectType.ThreatDays || type == HubEffectType.InventorySlots || type == HubEffectType.RunsPerDay;
}

/// <summary>
/// Определение постройки хаба (этап 4): id (ключ в MetaProgress — не меняйте после выпуска), название,
/// описание, вид, размер на сетке, стоимость в материалах склада хаба и эффекты на следующие забеги.
/// Открывается наградой достижения (AchievementRewardKind.HubBuilding с этим id).
/// Все определения собираются в <see cref="HubBuildingCatalog"/>, его получает Level.
/// </summary>
[CreateAssetMenu(fileName = "HubBuilding", menuName = "CodeRogue/Hub/HubBuilding")]
public class HubBuildingDefinition : ScriptableObject
{
    [Tooltip("Уникальный id — ключ в сохранении и в наградах достижений (storage, shrine…). " +
             "Пусто — имя ассета")]
    [SerializeField] private string id;
    [SerializeField] private string title;
    [TextArea(2, 5)]
    [SerializeField] private string description;

    [Header("View")]
    [Tooltip("Спрайт постройки в хабе и иконка в панели. Опорная точка — любая: спрайт ставится " +
             "основанием на нижний край занятых клеток и масштабируется по их ширине")]
    [SerializeField] private Sprite sprite;
    [Tooltip("Необязательно: префаб вместо спрайта (опорная точка — у основания, по центру области)")]
    [SerializeField] private GameObject prefab;

    [Header("Grid")]
    [Tooltip("Сколько клеток сетки хаба занимает (ширина × высота)")]
    [SerializeField] private Vector2Int size = Vector2Int.one;

    [Header("Cost & effects")]
    [Tooltip("Материалы со склада хаба (ключ — itemId предмета, иначе имя ассета)")]
    [SerializeField] private List<ItemStack> cost = new List<ItemStack>();
    [SerializeField] private List<HubBuildingEffect> effects = new List<HubBuildingEffect>();

    public string Id => string.IsNullOrWhiteSpace(id) ? name : id.Trim();
    public string Title => string.IsNullOrWhiteSpace(title) ? Id : title;
    public string Description => description ?? string.Empty;
    public Sprite Sprite => sprite;
    public GameObject Prefab => prefab;
    public Vector2Int Size => new Vector2Int(Mathf.Max(1, size.x), Mathf.Max(1, size.y));
    public IReadOnlyList<HubBuildingEffect> Effects => effects;

    /// <summary>
    /// Иконка для UI: спрайт определения, иначе первый спрайт префаба.
    /// </summary>
    public Sprite Icon
    {
        get
        {
            if (sprite != null) return sprite;
            if (prefab == null) return null;

            foreach (SpriteRenderer sr in prefab.GetComponentsInChildren<SpriteRenderer>(true))
                if (sr.sprite != null) return sr.sprite;
            return null;
        }
    }

    // ─── Стоимость ────────────────────────────────────────────

    /// <summary>
    /// Стоимость без пустых строк, с объединёнными дублями — (ключ склада, предмет, количество).
    /// Ключ — <see cref="MetaProgress.GetItemKey"/>, как у склада хаба. Используйте только это, а не сырой список.
    /// </summary>
    public List<HubBuildingCost> GetCost()
    {
        var result = new List<HubBuildingCost>();
        if (cost == null) return result;

        foreach (ItemStack stack in cost)
        {
            if (stack.item == null || stack.amount <= 0) continue;

            string key = MetaProgress.GetItemKey(stack.item);
            int index = result.FindIndex(c => c.Key == key);
            if (index >= 0)
                result[index] = new HubBuildingCost(key, result[index].Item, result[index].Amount + stack.amount);
            else
                result.Add(new HubBuildingCost(key, stack.item, stack.amount));
        }

        return result;
    }

    /// <summary>Строки эффектов для UI ("+2 inventory slot(s)").</summary>
    public IEnumerable<string> DescribeEffects()
    {
        if (effects == null) yield break;
        foreach (HubBuildingEffect effect in effects)
            if (effect != null)
                yield return effect.Describe();
    }

    // ─── Validation ───────────────────────────────────────────

    /// <summary>Проблемы настройки. Пустой список — всё в порядке.</summary>
    public List<string> Validate()
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(id)) problems.Add("id is empty (asset name is used)");
        if (string.IsNullOrWhiteSpace(title)) problems.Add("title is empty");
        if (sprite == null && prefab == null) problems.Add("neither sprite nor prefab is assigned");
        if (size.x < 1 || size.y < 1) problems.Add($"size {size} must be at least 1×1");
        if (GetCost().Count == 0) problems.Add("cost is empty — the building is free");
        if (effects == null || effects.Count == 0) problems.Add("no effects");

        if (cost != null)
            for (int i = 0; i < cost.Count; i++)
                if (cost[i].item == null)
                    problems.Add($"cost #{i + 1} has no item");
                else if (!cost[i].item.IsMaterial)
                    problems.Add($"cost #{i + 1}: '{cost[i].item.name}' is not a material — it never reaches the hub stock");

        return problems;
    }

    private void OnValidate()
    {
        size = new Vector2Int(Mathf.Max(1, size.x), Mathf.Max(1, size.y));
        cost ??= new List<ItemStack>();
        effects ??= new List<HubBuildingEffect>();
    }
}

/// <summary>
/// Строка стоимости постройки: ключ склада хаба (MetaProgress.GetItemKey), предмет (для иконки/имени) и количество.
/// </summary>
public readonly struct HubBuildingCost
{
    public readonly string Key;
    public readonly ResourceItem Item;
    public readonly int Amount;

    public HubBuildingCost(string key, ResourceItem item, int amount)
    {
        Key = key;
        Item = item;
        Amount = amount;
    }
}
