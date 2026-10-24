using System.Threading;
using System.Threading.Tasks;
using Lua;

/// <summary>
/// Базовый класс Lua-команды: регистрирует функцию и при каждом вызове
/// списывает <see cref="EnergyCost"/>, а затем выполняет <see cref="ExecuteAsync"/>.
/// Новые команды наследуются от него (или от PlayerCommandBase) — плата за вызов берётся автоматически.
/// </summary>
public abstract class LuaCommandBase : ILuaCommand
{
    public abstract string Name { get; }
    public abstract float EnergyCost { get; }
    public abstract string Signature { get; }
    public abstract string Description { get; }
    public virtual bool IsReadyMade => false;

    /// <summary>
    /// Состояние Lua, в котором зарегистрирована команда (например, для создания функций в таблицах).
    /// </summary>
    protected LuaState State { get; private set; }

    /// <summary>
    /// Счётчик энергии — для функций, которые команда создаёт сама (например, методы в возвращаемых таблицах).
    /// </summary>
    protected EnergyMeter Meter { get; private set; }

    public void Register(LuaState state, EnergyMeter meter)
    {
        State = state;
        Meter = meter;

        // Бесплатных команд быть не должно: иначе готовые команды и циклы теряют смысл
        UnityEngine.Debug.Assert(EnergyCost > 0f, $"Lua command '{Name}' must cost energy (EnergyCost > 0)");

        state.Environment[Name] = new LuaFunction(async (context, ct) =>
        {
            meter?.Track(context.State);
            meter?.Charge(EnergyCost, $"{Name}()");
            // Вызов оплачен — команда «использована» (для достижений), даже если дальше вернёт ошибку
            GameEventBus.Publish(GameEventType.CommandUsed, Name);
            return await ExecuteAsync(context, ct);
        });
    }

    /// <summary>
    /// Тело команды. Возвращает количество значений, переданных в context.Return.
    /// Передавайте ct во все ожидаемые вызовы, чтобы работала остановка (Stop).
    /// </summary>
    protected abstract ValueTask<int> ExecuteAsync(LuaFunctionExecutionContext context, CancellationToken ct);
}
