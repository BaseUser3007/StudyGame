using System.Threading;
using System.Threading.Tasks;
using Lua;

public class GetPositionCommand : PlayerCommandBase
{
    public override string Name => "getPosition";
    public override float EnergyCost => 1f;
    public override string Signature => "getPosition()";
    public override string Description => "Текущая позиция: таблица { x, y }";

    public GetPositionCommand(PlayerCommandExecutor executor, IMapDataProvider mapData)
        : base(executor, mapData) { }

    protected override ValueTask<int> ExecuteAsync(LuaFunctionExecutionContext context, CancellationToken ct)
    {
        var pos = Executor.CurrentTile;
        var table = new LuaTable();
        table["x"] = (double)pos.x;
        table["y"] = (double)pos.y;
        context.Return(table);
        return new ValueTask<int>(1);
    }
}
