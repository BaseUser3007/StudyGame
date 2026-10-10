using Lua;

/// <summary>
/// Интерфейс Lua команды.
/// Каждая команда знает своё имя, стоимость энергии
/// и умеет регистрировать себя в LuaState.
/// </summary>
public interface ILuaCommand
{
    /// <summary>
    /// Имя команды в Lua (например "move", "chop").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Стоимость энергии за один вызов команды.
    /// </summary>
    float EnergyCost { get; }

    /// <summary>
    /// Регистрирует команду в Lua среде.
    /// </summary>
    void Register(LuaState state);
}