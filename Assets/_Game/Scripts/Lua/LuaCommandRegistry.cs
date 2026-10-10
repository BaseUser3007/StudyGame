using System.Collections.Generic;
using Lua;
using UnityEngine;

/// <summary>
/// Реестр всех Lua команд.
/// Единственное место где команды регистрируются и хранятся.
/// </summary>
public class LuaCommandRegistry
{
    private readonly Dictionary<string, ILuaCommand> _commands
        = new Dictionary<string, ILuaCommand>();

    // ─── Регистрация ──────────────────────────────────────────

    /// <summary>
    /// Добавляет команду в реестр.
    /// </summary>
    public void RegisterCommand(ILuaCommand command)
    {
        if (_commands.ContainsKey(command.Name))
        {
            Debug.LogWarning($"LuaCommandRegistry: command '{command.Name}' already registered — overwriting");
        }

        _commands[command.Name] = command;
    }

    /// <summary>
    /// Регистрирует все команды в Lua среде.
    /// Вызывается один раз при инициализации LuaSandbox.
    /// </summary>
    public void RegisterAll(LuaState state)
    {
        foreach (ILuaCommand command in _commands.Values)
            command.Register(state);

        Debug.Log($"LuaCommandRegistry: registered {_commands.Count} commands");
    }

    // ─── Energy ───────────────────────────────────────────────

    /// <summary>
    /// Возвращает стоимость энергии команды по имени.
    /// Возвращает 0 если команда не найдена.
    /// </summary>
    public float GetEnergyCost(string commandName)
    {
        return _commands.TryGetValue(commandName, out ILuaCommand command)
            ? command.EnergyCost
            : 0f;
    }

    /// <summary>
    /// Проверяет зарегистрирована ли команда.
    /// </summary>
    public bool HasCommand(string commandName)
    {
        return _commands.ContainsKey(commandName);
    }

    /// <summary>
    /// Возвращает все зарегистрированные команды.
    /// </summary>
    public IReadOnlyCollection<ILuaCommand> GetAllCommands()
    {
        return _commands.Values;
    }
}