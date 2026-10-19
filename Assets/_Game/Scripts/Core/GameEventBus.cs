using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Тип игрового события — то, на что могут реагировать условия достижений.
/// Значения сохраняются в ассетах достижений по имени (Unity пишет enum числом —
/// новые типы добавляйте только в конец, не переставляя существующие).
/// </summary>
public enum GameEventType
{
    /// <summary>harvest/obtain: подобран предмет. Id — ключ предмета, Tag — тип объекта ("stick"), Amount — штук.</summary>
    Harvested,
    /// <summary>mine: добыт объект инструментом. Id — тип объекта ("tree"), Amount — 1.</summary>
    Mined,
    /// <summary>Предмет попал в инвентарь любым путём (подбор, добыча, крафт). Id — ключ предмета, Amount — штук.</summary>
    ItemObtained,
    /// <summary>craft: скрафчен предмет. Id — ключ результата, Amount — штук.</summary>
    Crafted,
    /// <summary>build: поставлена постройка. Id — ключ StructureItem, Tag — станция (CraftingStation).</summary>
    Built,
    /// <summary>Закончился день забега. Amount — 1, Tag — "exhausted" (без энергии) / "slept" (у кровати) / null.</summary>
    DayPassed,
    /// <summary>Вызвана Lua-команда (после списания энергии за вызов). Id — имя команды.</summary>
    CommandUsed,
    /// <summary>
    /// Конструкция языка есть в коде запуска, прошедшего проверки, — один раз за запуск на конструкцию
    /// (не за каждое выполнение). Id — id из LuaConstructs.
    /// </summary>
    ConstructUsed,
    /// <summary>Начался запуск кода.</summary>
    RunStarted,
    /// <summary>Закончился запуск кода. Id — "success" или "failed".</summary>
    RunFinished,

    // ─── Этап 3 ───
    /// <summary>Наступил день угрозы (DayCycle, один раз за забег). Amount — 1, Tag — номер дня.</summary>
    ThreatReached,
    /// <summary>Враг найден кодом (findObjects/check). Id — тип врага. Публикует Lua-команда (этап 3, lua).</summary>
    EnemyFound,
    /// <summary>Враг побеждён (EnemyManager). Id — тип врага ("goblin"), Tag — "guard" / "patrol".</summary>
    EnemyKilled,

    // ─── Будущие события (публикация появится на этапах 4–7) ───
    /// <summary>Этап 6: решена головоломка. Id — тип/id головоломки.</summary>
    PuzzleSolved,
    /// <summary>Этап 3–5: съедена еда. Id — ключ предмета.</summary>
    FoodEaten,
    /// <summary>Этап 4: поставлена постройка хаба. Id — id постройки.</summary>
    HubBuildingBuilt
}

/// <summary>
/// Игровое событие: тип, основной id (фильтр условий), дополнительный тег и количество.
/// Фильтр достижения совпадает, если равен Id или Tag (без учёта регистра).
/// </summary>
public readonly struct GameEvent
{
    public readonly GameEventType Type;
    public readonly string Id;
    public readonly string Tag;
    public readonly int Amount;
    /// <summary>
    /// Не новое получение: подбор выброшенного игроком (drop), стартовые предметы забега.
    /// Условия достижений такие события пропускают.
    /// </summary>
    public readonly bool Recycled;
    /// <summary>
    /// Получено благодаря текущему запуску кода. False — подбор объекта, который появился не в этом
    /// запуске (лежал на карте или выпал при mine в прошлом запуске). Только EfficientGather требует true;
    /// счётчики считают и такие события.
    /// </summary>
    public readonly bool FromThisRun;

    public GameEvent(
        GameEventType type, string id = null, int amount = 1, string tag = null, bool recycled = false,
        bool fromThisRun = true)
    {
        Type = type;
        Id = id;
        Tag = tag;
        Amount = amount;
        Recycled = recycled;
        FromThisRun = fromThisRun;
    }

    /// <summary>Подходит ли событие под фильтр (пустой фильтр — любое).</summary>
    public bool Matches(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return true;
        filter = filter.Trim();
        return string.Equals(filter, Id, StringComparison.OrdinalIgnoreCase)
            || string.Equals(filter, Tag, StringComparison.OrdinalIgnoreCase);
    }

    public override string ToString() => $"{Type}({Id}{(Tag != null ? "/" + Tag : "")} ×{Amount})";
}

/// <summary>
/// Итог запуска кода: успешен ли, сколько энергии потрачено за запуск (всё, что списано
/// с PlayerEnergy — конструкции, вызовы команд, добыча, крафт) и какие конструкции были в коде.
/// </summary>
public readonly struct CodeRunReport
{
    public readonly bool Succeeded;
    /// <summary>Код прошёл проверки синтаксиса и конструкций и начал выполняться.</summary>
    public readonly bool Started;
    public readonly float EnergySpent;
    public readonly IReadOnlyCollection<string> Constructs;

    public CodeRunReport(bool succeeded, bool started, float energySpent, IReadOnlyCollection<string> constructs)
    {
        Succeeded = succeeded;
        Started = started;
        EnergySpent = energySpent;
        Constructs = constructs ?? Array.Empty<string>();
    }
}

/// <summary>
/// Статическая шина игровых событий для достижений (и, позже, туториала).
/// Публикуют места, где событие реально происходит (ResourceHarvester, CraftCommand, StructureManager,
/// DayCycle, LuaSandbox, LuaCommandBase). Подписчики отписываются в OnDestroy/Dispose именованными методами.
/// Исключение в подписчике логируется и не ломает публикующий код (часто это Lua-команда).
/// </summary>
public static class GameEventBus
{
    public static event Action<GameEvent> OnGameEvent;
    public static event Action OnRunStarted;
    public static event Action<CodeRunReport> OnRunFinished;

    public const string RunSucceededId = "success";
    public const string RunFailedId = "failed";

    private static int _lastRunId;

    /// <summary>
    /// Номер идущего запуска кода (с 1) или 0, если код не выполняется. Им помечаются объекты,
    /// появившиеся во время запуска (ResourceObject.SpawnRunId), — для GameEvent.FromThisRun.
    /// </summary>
    public static int ActiveRunId { get; private set; }

    // Статика переживает Play Mode без перезагрузки домена — сбрасываем подписчиков и номера запусков
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        OnGameEvent = null;
        OnRunStarted = null;
        OnRunFinished = null;
        _lastRunId = 0;
        ActiveRunId = 0;
    }

    /// <summary>Объект появился в идущем запуске кода (spawnRunId — ResourceObject.SpawnRunId).</summary>
    public static bool IsFromActiveRun(int spawnRunId) => spawnRunId != 0 && spawnRunId == ActiveRunId;

    // ─── Publish ──────────────────────────────────────────────

    public static void Publish(GameEvent gameEvent)
    {
        Action<GameEvent> handlers = OnGameEvent;
        if (handlers == null) return;

        foreach (Delegate handler in handlers.GetInvocationList())
        {
            try { ((Action<GameEvent>)handler)(gameEvent); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    public static void Publish(GameEventType type, string id = null, int amount = 1, string tag = null)
        => Publish(new GameEvent(type, id, amount, tag));

    public static void PublishRunStarted()
    {
        ActiveRunId = ++_lastRunId;

        Action handlers = OnRunStarted;
        if (handlers != null)
        {
            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try { ((Action)handler)(); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        Publish(GameEventType.RunStarted);
    }

    /// <summary>
    /// Конец запуска: сначала ConstructUsed за каждую конструкцию в коде (если код начал выполняться),
    /// затем OnRunFinished и RunFinished.
    /// </summary>
    public static void PublishRunFinished(CodeRunReport report)
    {
        if (report.Started)
            foreach (string construct in report.Constructs)
                Publish(GameEventType.ConstructUsed, construct);

        Action<CodeRunReport> handlers = OnRunFinished;
        if (handlers != null)
        {
            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try { ((Action<CodeRunReport>)handler)(report); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        Publish(GameEventType.RunFinished, report.Succeeded ? RunSucceededId : RunFailedId);
        ActiveRunId = 0;
    }
}
