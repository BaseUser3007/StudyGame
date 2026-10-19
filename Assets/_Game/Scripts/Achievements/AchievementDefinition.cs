using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Как считается условие достижения.</summary>
public enum AchievementConditionType
{
    /// <summary>Накопить amount (сумма GameEvent.Amount подходящих событий) — за всё время, между забегами.</summary>
    Counter,
    /// <summary>В пределах ОДНОГО запуска кода набрать amount, потратив с начала запуска не больше maxEnergy.</summary>
    EfficientGather,
    /// <summary>Первое подходящее событие (amount не важен) — например, первое использование конструкции.</summary>
    FirstUse
}

/// <summary>Что даёт достижение.</summary>
public enum AchievementRewardKind
{
    /// <summary>Lua-команда; id — ILuaCommand.Name.</summary>
    Command,
    /// <summary>Конструкция языка; id — из LuaConstructs (local, if, for, while, repeat, function, classes).</summary>
    Construct,
    /// <summary>Рецепт с флагом locked; id — itemId результата, иначе имя ассета.</summary>
    Recipe,
    /// <summary>Постройка хаба (этап 4); id — id определения постройки.</summary>
    HubBuilding,
    /// <summary>Событие/головоломка на карте (этап 6); id — id события.</summary>
    MapEvent
}

/// <summary>
/// Условие достижения: какое событие, с каким фильтром и сколько раз.
/// </summary>
[Serializable]
public class AchievementCondition
{
    public AchievementConditionType type = AchievementConditionType.Counter;

    [Tooltip("Игровое событие из GameEventBus")]
    public GameEventType gameEvent = GameEventType.Harvested;

    [Tooltip("itemId / тип объекта (tree, stick…) / id конструкции / имя команды — сравнивается с Id и Tag " +
             "события без учёта регистра. Пусто — любое")]
    public string filter;

    [Tooltip("Порог: сумма количеств подходящих событий (для FirstUse не важен)")]
    [Min(1)] public int amount = 1;

    [Tooltip("EfficientGather: максимум энергии, потраченной с начала запуска кода до набора amount")]
    [Min(0f)] public float maxEnergy = 10f;

    /// <summary>Сколько нужно набрать.</summary>
    public int Target => type == AchievementConditionType.FirstUse ? 1 : Mathf.Max(1, amount);

    public bool Matches(GameEvent gameEvent) => gameEvent.Type == this.gameEvent && gameEvent.Matches(filter);
}

/// <summary>
/// Награда достижения: вид и id того, что открывается.
/// </summary>
[Serializable]
public class AchievementReward
{
    public AchievementRewardKind kind = AchievementRewardKind.Command;
    [Tooltip("Имя команды / id конструкции / ключ рецепта (itemId результата) / id постройки хаба / id события")]
    public string id;

    public string Id => id != null ? id.Trim() : string.Empty;

    /// <summary>Читаемое описание для UI (UI игры на английском), например "command `mine`".</summary>
    public string Describe()
    {
        switch (kind)
        {
            case AchievementRewardKind.Command: return $"command `{Id}`";
            case AchievementRewardKind.Construct: return LuaConstructs.GetDisplayName(Id);
            case AchievementRewardKind.Recipe: return $"recipe: {Id}";
            case AchievementRewardKind.HubBuilding: return $"camp building: {Id}";
            case AchievementRewardKind.MapEvent: return $"map event: {Id}";
            default: return Id;
        }
    }
}

/// <summary>
/// Определение достижения: id (ключ в MetaProgress — не меняйте после выпуска), название, описание,
/// условие и награды. Все определения собираются в <see cref="AchievementCatalog"/>, его получает Level.
/// </summary>
[CreateAssetMenu(fileName = "Achievement", menuName = "CodeRogue/Achievements/Achievement")]
public class AchievementDefinition : ScriptableObject
{
    [Tooltip("Уникальный id — ключ в сохранении. Пусто — имя ассета (переименование ассета сбросит прогресс)")]
    [SerializeField] private string id;
    [SerializeField] private string title;
    [TextArea(2, 5)]
    [SerializeField] private string description;
    [Tooltip("Иконка для панели достижений (необязательно)")]
    [SerializeField] private Sprite icon;

    [SerializeField] private AchievementCondition condition = new AchievementCondition();
    [SerializeField] private List<AchievementReward> rewards = new List<AchievementReward>();

    public string Id => string.IsNullOrWhiteSpace(id) ? name : id.Trim();
    public string Title => string.IsNullOrWhiteSpace(title) ? Id : title;
    public string Description => description ?? string.Empty;
    public Sprite Icon => icon;
    public AchievementCondition Condition => condition;
    public IReadOnlyList<AchievementReward> Rewards => rewards;

    // ─── Validation ───────────────────────────────────────────

    /// <summary>
    /// Проблемы настройки (пустые поля, неизвестные конструкции). Пустой список — всё в порядке.
    /// Имена команд и рецептов проверяет AchievementTracker — он знает реестр команд и книгу рецептов.
    /// </summary>
    public List<string> Validate()
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(id)) problems.Add("id is empty (asset name is used)");
        if (string.IsNullOrWhiteSpace(title)) problems.Add("title is empty");
        if (condition == null)
        {
            problems.Add("condition is missing");
        }
        else if (condition.type == AchievementConditionType.EfficientGather && condition.maxEnergy <= 0f)
        {
            problems.Add("EfficientGather needs maxEnergy > 0");
        }

        if (rewards != null)
        {
            for (int i = 0; i < rewards.Count; i++)
            {
                AchievementReward reward = rewards[i];
                if (reward == null || string.IsNullOrEmpty(reward.Id))
                    problems.Add($"reward #{i + 1} has empty id");
                else if (reward.kind == AchievementRewardKind.Construct && !LuaConstructs.IsKnown(reward.Id))
                    problems.Add($"reward #{i + 1}: unknown construct '{reward.Id}'");
            }
        }

        return problems;
    }

    private void OnValidate()
    {
        condition ??= new AchievementCondition();
        condition.amount = Mathf.Max(1, condition.amount);
        condition.maxEnergy = Mathf.Max(0f, condition.maxEnergy);
        rewards ??= new List<AchievementReward>();

        foreach (string problem in Validate())
            Debug.LogWarning($"AchievementDefinition '{name}': {problem}", this);
    }
}
