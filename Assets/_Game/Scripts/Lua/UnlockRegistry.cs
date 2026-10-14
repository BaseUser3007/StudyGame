using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Что открыто: команда или конструкция языка.</summary>
public enum UnlockKind
{
    Command,
    Construct
}

/// <summary>
/// Какие Lua-команды и конструкции языка доступны игроку.
/// Открыто = стартовый набор из <see cref="UnlockConfig"/> ∪ сохранённое в <see cref="MetaProgress"/>
/// (или всё, если включён unlockAll). Открытия сразу пишутся в MetaProgress и сохраняются на диск.
/// Песочница подписана на <see cref="OnUnlocked"/> и подключает новые команды (см. LuaSandbox).
/// </summary>
public class UnlockRegistry
{
    /// <summary>
    /// Что-то открыто впервые: вид и id (имя команды / id из <see cref="LuaConstructs"/>).
    /// Для всплывашки «Открыто: X» и достижений.
    /// </summary>
    public event Action<UnlockKind, string> OnUnlocked;

    private readonly MetaProgress _progress;
    private readonly HashSet<string> _startingCommands;
    private readonly HashSet<string> _startingConstructs;
    // null — список команд ещё не задан, имена не проверяются
    private HashSet<string> _knownCommands;

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
    /// (опечатка в награде) — предупреждение и false, без сохранения и события.
    /// </summary>
    public void SetKnownCommands(IEnumerable<string> names)
    {
        _knownCommands = names != null ? new HashSet<string>(names, StringComparer.Ordinal) : null;
    }

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
        if (_knownCommands != null && !_knownCommands.Contains(name))
        {
            Debug.LogWarning($"UnlockRegistry: unknown command '{name}'");
            return false;
        }

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

    // ─── Helpers ──────────────────────────────────────────────

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
