/// <summary>
/// Базовый класс для команд которые управляют персонажем.
/// Хранит ссылку на executor и mapData.
/// </summary>
public abstract class PlayerCommandBase : ILuaCommand
{
    protected readonly PlayerCommandExecutor Executor;
    protected readonly IMapDataProvider MapData;

    protected PlayerCommandBase(PlayerCommandExecutor executor, IMapDataProvider mapData)
    {
        Executor = executor;
        MapData = mapData;
    }

    public abstract string Name { get; }
    public abstract float EnergyCost { get; }
    public abstract string Signature { get; }
    public abstract string Description { get; }

    public abstract void Register(Lua.LuaState state);
}