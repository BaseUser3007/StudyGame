using Lua;

public class GetPositionCommand : PlayerCommandBase
{
    public override string Name => "getPosition";
    public override float EnergyCost => 0f;

    public GetPositionCommand(PlayerCommandExecutor executor, IMapDataProvider mapData)
        : base(executor, mapData) { }

    public override void Register(LuaState state)
    {
        state.Environment[Name] = new LuaFunction(async (context, ct) =>
        {
            var pos = Executor.CurrentTile;
            var table = new LuaTable();
            table["x"] = (double)pos.x;
            table["y"] = (double)pos.y;
            context.Return(table);
            return 1;
        });
    }
}