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
/// Дополнительный уровень постройки хаба (2, 3, …): стоимость улучшения с предыдущего уровня
/// и эффекты-ПРИБАВКИ этого уровня (складываются с эффектами уровня 1 и всех предыдущих).
/// </summary>
[Serializable]
public class HubBuildingLevel
{
    [Tooltip("Материалы склада хаба за улучшение до этого уровня (ключ — itemId, иначе имя ассета)")]
    public List<ItemStack> cost = new List<ItemStack>();
    [Tooltip("Прибавка этого уровня к эффектам предыдущих (не итог)")]
    public List<HubBuildingEffect> effects = new List<HubBuildingEffect>();
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

    [Header("Levels")]
    [Tooltip("Уровни 2, 3, …: стоимость улучшения и ПРИБАВКА эффектов уровня. Уровень 1 — cost/effects выше. " +
             "Пусто — постройка одноуровневая")]
    [SerializeField] private List<HubBuildingLevel> levels = new List<HubBuildingLevel>();

    public string Id => string.IsNullOrWhiteSpace(id) ? name : id.Trim();
    public string Title => string.IsNullOrWhiteSpace(title) ? Id : title;
    public string Description => description ?? string.Empty;
    public Sprite Sprite => sprite;
    public GameObject Prefab => prefab;
    public Vector2Int Size => new Vector2Int(Mathf.Max(1, size.x), Mathf.Max(1, size.y));
    /// <summary>Эффекты уровня 1 (сырой список). Итог для уровня — <see cref="GetEffects(int)"/>.</summary>
    public IReadOnlyList<HubBuildingEffect> Effects => effects;

    /// <summary>Максимальный уровень: 1 + число дополнительных уровней (старые ассеты без levels — 1).</summary>
    public int MaxLevel => 1 + (levels?.Count ?? 0);

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

    /// <summary>Стоимость постройки (уровень 1) — то же, что <c>GetCost(1)</c>.</summary>
    public List<HubBuildingCost> GetCost() => GetCost(1);

    /// <summary>
    /// Стоимость получения уровня level: 1 — постройка, 2+ — улучшение с level − 1. Без пустых строк,
    /// с объединёнными дублями — (ключ склада, предмет, количество). Ключ — <see cref="MetaProgress.GetItemKey"/>,
    /// как у склада хаба. Уровень вне 1..MaxLevel — пустой список. Используйте только это, а не сырые списки.
    /// </summary>
    public List<HubBuildingCost> GetCost(int level) =>
        MergeCost(level == 1 ? cost : GetExtraLevel(level)?.cost);

    // ─── Эффекты ──────────────────────────────────────────────

    /// <summary>
    /// Эффекты-прибавки одного уровня (1 — базовые effects, 2+ — из levels), для UI «что даст улучшение».
    /// Уровень вне 1..MaxLevel — пусто.
    /// </summary>
    public IReadOnlyList<HubBuildingEffect> GetLevelEffects(int level)
    {
        List<HubBuildingEffect> list = level == 1 ? effects : GetExtraLevel(level)?.effects;
        return list != null ? list : (IReadOnlyList<HubBuildingEffect>)Array.Empty<HubBuildingEffect>();
    }

    /// <summary>
    /// Итоговые эффекты постройки уровня level: прибавки уровней 1..level сложены, по одному элементу
    /// на тип (в порядке первого появления; целые параметры округляются уже от суммы). level зажимается
    /// в 1..MaxLevel. Это читает RunModifiers. Возвращает новые объекты — менять их можно.
    /// </summary>
    public List<HubBuildingEffect> GetEffects(int level)
    {
        level = Mathf.Clamp(level, 1, MaxLevel);
        var result = new List<HubBuildingEffect>();

        for (int l = 1; l <= level; l++)
            foreach (HubBuildingEffect effect in GetLevelEffects(l))
            {
                if (effect == null) continue;

                HubBuildingEffect total = result.Find(e => e.type == effect.type);
                if (total != null)
                    total.amount += effect.amount;
                else
                    result.Add(new HubBuildingEffect { type = effect.type, amount = effect.amount });
            }

        return result;
    }

    /// <summary>Строки эффектов уровня 1 для UI ("+2 inventory slot(s)").</summary>
    public IEnumerable<string> DescribeEffects() => DescribeEffects(1);

    /// <summary>Строки итоговых эффектов уровня level для UI.</summary>
    public IEnumerable<string> DescribeEffects(int level)
    {
        foreach (HubBuildingEffect effect in GetEffects(level))
            yield return effect.Describe();
    }

    /// <summary>Строки прибавки одного уровня для UI ("+10 energy limit").</summary>
    public IEnumerable<string> DescribeLevelEffects(int level)
    {
        foreach (HubBuildingEffect effect in GetLevelEffects(level))
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

        if (levels != null)
            for (int i = 0; i < levels.Count; i++)
            {
                int level = i + 2;
                HubBuildingLevel extra = levels[i];
                if (extra == null)
                {
                    problems.Add($"level {level} is empty");
                    continue;
                }

                if (GetCost(level).Count == 0) problems.Add($"level {level}: cost is empty — the upgrade is free");
                if (extra.effects == null || extra.effects.Count == 0) problems.Add($"level {level}: no effects");
                if (extra.cost == null) continue;

                for (int c = 0; c < extra.cost.Count; c++)
                    if (extra.cost[c].item == null)
                        problems.Add($"level {level}: cost #{c + 1} has no item");
                    else if (!extra.cost[c].item.IsMaterial)
                        problems.Add($"level {level}: cost #{c + 1}: '{extra.cost[c].item.name}' is not a material");
            }

        return problems;
    }

    private void OnValidate()
    {
        size = new Vector2Int(Mathf.Max(1, size.x), Mathf.Max(1, size.y));
        cost ??= new List<ItemStack>();
        effects ??= new List<HubBuildingEffect>();
        levels ??= new List<HubBuildingLevel>();
    }

    // ─── Helpers ──────────────────────────────────────────────

    /// <summary>Дополнительный уровень (2, 3, …) или null.</summary>
    private HubBuildingLevel GetExtraLevel(int level)
    {
        int index = level - 2;
        return levels != null && index >= 0 && index < levels.Count ? levels[index] : null;
    }

    /// <summary>Строки стоимости без пустых, дубли одного предмета сложены.</summary>
    private static List<HubBuildingCost> MergeCost(List<ItemStack> stacks)
    {
        var result = new List<HubBuildingCost>();
        if (stacks == null) return result;

        foreach (ItemStack stack in stacks)
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
