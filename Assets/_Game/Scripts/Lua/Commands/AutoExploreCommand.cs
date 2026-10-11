using System.Threading;
using System.Threading.Tasks;
using Lua;
using UnityEngine;

public class AutoExploreCommand : PlayerCommandBase
{
    public override string Name => "autoExplore";
    public override float EnergyCost => 25f;
    public override string Signature => "autoExplore()";
    public override string Description => "Пройти 5 шагов в случайном направлении";

    public AutoExploreCommand(PlayerCommandExecutor executor, IMapDataProvider mapData)
        : base(executor, mapData) { }

    protected override async ValueTask<int> ExecuteAsync(LuaFunctionExecutionContext context, CancellationToken ct)
    {
        Direction[] directions = (Direction[])System.Enum.GetValues(typeof(Direction));
        Direction random = directions[Random.Range(0, directions.Length)];
        await Executor.MoveAsync(random, 5, ct);
        context.Return();
        return 0;
    }
}
