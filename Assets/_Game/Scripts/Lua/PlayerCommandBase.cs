/// <summary>
/// Базовый класс для команд которые управляют персонажем.
/// Хранит ссылку на executor и mapData.
/// </summary>
public abstract class PlayerCommandBase : LuaCommandBase
{
    protected readonly PlayerCommandExecutor Executor;
    protected readonly IMapDataProvider MapData;

    protected PlayerCommandBase(PlayerCommandExecutor executor, IMapDataProvider mapData)
    {
        Executor = executor;
        MapData = mapData;
    }
}
