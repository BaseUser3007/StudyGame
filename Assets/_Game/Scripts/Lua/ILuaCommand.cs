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
    /// Сигнатура для подсказок редактора, например "move(direction, steps)".
    /// </summary>
    string Signature { get; }

    /// <summary>
    /// Короткое описание для подсказок редактора.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Готовая команда — заменяет несколько базовых и стоит дороже их суммы (в справке — отдельным разделом).
    /// </summary>
    bool IsReadyMade { get; }

    /// <summary>
    /// Регистрирует команду в Lua среде. meter списывает EnergyCost при каждом вызове
    /// (реализовано в <see cref="LuaCommandBase"/>).
    /// </summary>
    void Register(LuaState state, EnergyMeter meter);
}