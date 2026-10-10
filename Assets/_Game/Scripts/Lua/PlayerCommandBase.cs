/// <summary>
/// Ѕазовый класс дл€ команд которые управл€ют персонажем.
/// ’ранит ссылку на executor и mapData.
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

    public abstract void Register(Lua.LuaState state);
}