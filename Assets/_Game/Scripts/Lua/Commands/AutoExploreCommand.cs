using System.Threading;
using System.Threading.Tasks;
using Lua;
using UnityEngine;

public class AutoExploreCommand : PlayerCommandBase
{
    public override string Name => "autoExplore";
    // Готовая команда: аналог из базовых — move(dir, 5) ≈ 1–2 энергии; наценка за удобство (GameDesign §9, решение 3)
    public override float EnergyCost => 10f;
    public override bool IsReadyMade => true;
    public override string Signature => "autoExplore()";
    public override string Description => "Готовая команда: пройти 5 шагов в случайном направлении. Дороже, чем move(direction, 5)";

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
