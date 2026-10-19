using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Что открыто: команда, конструкция языка, рецепт, постройка хаба или событие на карте.</summary>
public enum UnlockKind
{
    Command,
    Construct,
    /// <summary>Рецепт с флагом CraftRecipe.locked; id — ключ результата (CraftRecipe.Key).</summary>
    Recipe,
    /// <summary>Постройка хаба (этап 4); id — id определения постройки.</summary>
    HubBuilding,
    /// <summary>Тип события/головоломки на карте (этап 6); id — id события.</summary>
    MapEvent
}

/// <summary>
/// Какие Lua-команды, конструкции языка и рецепты доступны игроку (плюс открытые постройки хаба
/// и события на карте — их читают этапы 4 и 6).
/// Открыто = стартовый набор из <see cref="UnlockConfig"/> ∪ сохранённое в <see cref="MetaProgress"/>
/// (или всё, если включён unlockAll). Рецепты без флага CraftRecipe.locked открыты всегда.
/// Открытие id, которого в игре пока нет (команда/рецепт/постройка хаба/событие будущего этапа), — отложенное:
/// id сохраняется в MetaProgress без события, а проверки открытости читают MetaProgress, поэтому
/// команда/рецепт станут доступны, как только появятся в игре. Открытия сразу пишутся в MetaProgress и сохраняются на диск.
/// Песочница подписана на <see cref="OnUnlocked"/> и подключает новые команды (см. LuaSandbox).
/// </summary>
public class UnlockRegistry
{
    /// <summary>
    /// Что-то открыто впервые: вид и id (имя команды / id из <see cref="LuaConstructs"/> / ключ рецепта /
    /// id постройки хаба / id события).
    /// Для всплывашки «Открыто: X» и достижений.
    /// </summary>
    public event Action<UnlockKind, string> OnUnlocked;

    private readonly MetaProgress _progress;
    private readonly HashSet<string> _startingCommands;
    private readonly HashSet<string> _startingConstructs;
    // null — список команд ещё не задан, имена не проверяются
    private HashSet<string> _knownCommands;
    // null — список рецептов ещё не задан, ключи не проверяются
    private HashSet<string> _knownRecipes;
    // null — каталога построек хаба / событий на карте ещё нет (этапы 4 и 6): открытия только сохраняются
    private HashSet<string> _knownHubBuildings;
    private HashSet<string> _knownMapEvents;

    /// <summary>Отладка: всё открыто (сохранение при этом не меняется).</summary>
    public bool UnlockAll { get; }

    /// <summary>Стартовые команды из конфига — для проверки опечаток в именах.</summary>
    public IReadOnlyCollection<string> StartingCommands => _startingCommands;

    public UnlockRegistry(UnlockConfig config, MetaProgress progress)
    {
        Debug.Assert(progress != null, "UnlockRegistry: MetaProgress is null");

        _progress = progress;
        _startingCommands = ToSet(config != null ? config.startingCommands : null);
        _startingConstructs = ToSet(config != null ? config.startingConstructs : null);

        // Без конфига (ассет ещё не привязан) открываем всё, чтобы игра оставалась играбельной
        if (config == null)
            Debug.LogWarning("UnlockRegistry: UnlockConfig is not assigned — everything is unlocked");
        UnlockAll = config == null || config.unlockAll;

        foreach (string id in _startingConstructs)
            if (!LuaConstructs.IsKnown(id))
                Debug.LogWarning($"UnlockRegistry: unknown construct id '{id}' in UnlockConfig");
    }

    // ─── Public API ───────────────────────────────────────────

    /// <summary>
    /// Имена всех команд игры (задаёт LuaSandbox). После этого UnlockCommand с неизвестным именем
    /// сохраняет его как отложенное открытие (лог), но не вызывает OnUnlocked и возвращает false.
    /// </summary>
    public void SetKnownCommands(IEnumerable<string> names)
    {
        _knownCommands = names != null ? new HashSet<string>(names, StringComparer.Ordinal) : null;
    }

    /// <summary>
    /// Ключи всех рецептов игры (CraftRecipe.Key; задаёт Level). После этого UnlockRecipe с неизвестным
    /// ключом — отложенное открытие (сохраняется, без события, false).
    /// </summary>
    public void SetKnownRecipes(IEnumerable<string> keys)
    {
        _knownRecipes = keys != null ? new HashSet<string>(keys, StringComparer.Ordinal) : null;
    }

    /// <summary>
    /// Id построек хаба из их каталога (этап 4). Пока не задан, UnlockHubBuilding сохраняет открытие
    /// отложенным, без OnUnlocked (показывать «новая постройка» нечего).
    /// </summary>
    public void SetKnownHubBuildings(IEnumerable<string> ids)
    {
        _knownHubBuildings = ids != null ? new HashSet<string>(ids, StringComparer.Ordinal) : null;
    }

    /// <summary>Id событий на карте из их каталога (этап 6). Пока не задан — открытия отложенные.</summary>
    public void SetKnownMapEvents(IEnumerable<string> ids)
    {
        _knownMapEvents = ids != null ? new HashSet<string>(ids, StringComparer.Ordinal) : null;
    }

    /// <summary>Команда известна игре (true, пока список команд не задан).</summary>
    public bool IsKnownCommand(string name) =>
        !string.IsNullOrEmpty(name) && (_knownCommands == null || _knownCommands.Contains(name));

    /// <summary>Рецепт с таким ключом есть в книге рецептов (true, пока список не задан).</summary>
    public bool IsKnownRecipe(string key) =>
        !string.IsNullOrEmpty(key) && (_knownRecipes == null || _knownRecipes.Contains(key));

    public bool IsCommandUnlocked(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        return UnlockAll || _startingCommands.Contains(name) || _progress.IsCommandUnlocked(name);
    }

    public bool IsConstructUnlocked(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        return UnlockAll || _startingConstructs.Contains(id) || _progress.IsConstructUnlocked(id);
    }

    /// <summary>
    /// Открывает команду навсегда (MetaProgress + Save). True — если открыта впервые.
    /// unlockAll здесь не учитывается: в отладке открытие тоже сохраняется и вызывает OnUnlocked.
    /// </summary>
    public bool UnlockCommand(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (!IsKnownCommand(name))
            return SaveDeferred("command", name, _progress.UnlockCommand);

        bool wasUnlocked = _startingCommands.Contains(name) || _progress.IsCommandUnlocked(name);
        if (_progress.UnlockCommand(name))
            _progress.Save();
        if (wasUnlocked) return false;

        OnUnlocked?.Invoke(UnlockKind.Command, name);
        return true;
    }

    /// <summary>
    /// Открывает конструкцию языка (id из <see cref="LuaConstructs"/>) навсегда. True — если открыта впервые.
    /// </summary>
    public bool UnlockConstruct(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        // Набор конструкций закрыт — неизвестный id это опечатка, а не награда будущего этапа
        if (!LuaConstructs.IsKnown(id))
        {
            Debug.LogWarning($"UnlockRegistry: unknown construct id '{id}'");
            return false;
        }

        bool wasUnlocked = _startingConstructs.Contains(id) || _progress.IsConstructUnlocked(id);
        if (_progress.UnlockConstruct(id))
            _progress.Save();
        if (wasUnlocked) return false;

        OnUnlocked?.Invoke(UnlockKind.Construct, id);
        return true;
    }

    // ─── Рецепты ──────────────────────────────────────────────

    /// <summary>Рецепт открыт мета-прогрессом (или unlockAll). Ключ — CraftRecipe.Key.</summary>
    public bool IsRecipeUnlocked(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        return UnlockAll || _progress.IsRecipeUnlocked(key);
    }

    /// <summary>
    /// Рецепт доступен для craft() и панели крафта: без флага locked — всегда, иначе — если открыт.
    /// </summary>
    public bool IsRecipeAvailable(CraftRecipe recipe)
    {
        return recipe != null && (!recipe.locked || IsRecipeUnlocked(recipe.Key));
    }

    /// <summary>
    /// Открывает рецепт навсегда по ключу результата (itemId, иначе имя ассета). True — если впервые.
    /// </summary>
    public bool UnlockRecipe(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        if (!IsKnownRecipe(key))
            return SaveDeferred("recipe", key, _progress.UnlockRecipe);

        return Unlock(UnlockKind.Recipe, key, _progress.IsRecipeUnlocked, _progress.UnlockRecipe);
    }

    // ─── Хаб и события на карте ───────────────────────────────

    public bool IsHubBuildingUnlocked(string id) =>
        !string.IsNullOrEmpty(id) && (UnlockAll || _progress.IsHubBuildingUnlocked(id));

    /// <summary>
    /// Открывает постройку хаба (этап 4). True — если впервые и постройка есть в каталоге;
    /// иначе (каталога ещё нет или id неизвестен) — отложенное открытие без события.
    /// </summary>
    public bool UnlockHubBuilding(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        if (_knownHubBuildings == null || !_knownHubBuildings.Contains(id))
            return SaveDeferred("hub building", id, _progress.UnlockHubBuilding);

        return Unlock(UnlockKind.HubBuilding, id, _progress.IsHubBuildingUnlocked, _progress.UnlockHubBuilding);
    }

    public bool IsMapEventUnlocked(string id) =>
        !string.IsNullOrEmpty(id) && (UnlockAll || _progress.IsEventUnlocked(id));

    /// <summary>
    /// Открывает тип события на карте (этап 6). True — если впервые и событие есть в каталоге;
    /// иначе — отложенное открытие без события.
    /// </summary>
    public bool UnlockMapEvent(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        if (_knownMapEvents == null || !_knownMapEvents.Contains(id))
            return SaveDeferred("map event", id, _progress.UnlockEvent);

        return Unlock(UnlockKind.MapEvent, id, _progress.IsEventUnlocked, _progress.UnlockEvent);
    }

    // ─── Helpers ──────────────────────────────────────────────

    /// <summary>
    /// Общее открытие без стартового набора: запись в MetaProgress + Save, событие при первом открытии.
    /// </summary>
    private bool Unlock(UnlockKind kind, string id, Func<string, bool> isSaved, Func<string, bool> save)
    {
        if (string.IsNullOrEmpty(id) || isSaved(id)) return false;

        save(id);
        _progress.Save();
        OnUnlocked?.Invoke(kind, id);
        return true;
    }

    /// <summary>
    /// Открытие того, чего в игре пока нет: сохраняется (применится, когда id появится), без события.
    /// Опечатку в награде видно по логу и предупреждению AchievementTracker при старте.
    /// </summary>
    private bool SaveDeferred(string what, string id, Func<string, bool> save)
    {
        if (save(id))
        {
            _progress.Save();
            Debug.Log($"UnlockRegistry: {what} '{id}' is not in the game yet — saved, will apply when it appears");
        }
        return false;
    }

    private static HashSet<string> ToSet(List<string> names)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (names == null) return set;

        foreach (string name in names)
            if (!string.IsNullOrWhiteSpace(name))
                set.Add(name.Trim());
        return set;
    }
}
