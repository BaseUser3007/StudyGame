using System.Threading;
using Lua;
using System.Threading.Tasks;
using UnityEngine;

public class MoveToCommand : PlayerCommandBase
{
    public override string Name => "moveTo";
    public override float EnergyCost => 2f;
    public override string Signature => "moveTo(x, y)";
    public override string Description => "Идти к тайлу, содержащему точку (x, y), по прямой. Вода останавливает, объекты обходятся; если застрял — стоп через пару секунд. Возвращает число пройденных тайлов";

    public MoveToCommand(PlayerCommandExecutor executor, IMapDataProvider mapData)
        : base(executor, mapData) { }

    protected override async ValueTask<int> ExecuteAsync(LuaFunctionExecutionContext context, CancellationToken ct)
    {
        // Координаты объектов дробные — идём в тайл, где лежит точка
        int x = MapPosition.ToTile(context.GetArgument<double>(0));
        int y = MapPosition.ToTile(context.GetArgument<double>(1));

        int stepsTaken = await Executor.MoveToAsync(new Vector2Int(x, y), ct);
        context.Return(stepsTaken);
        return 1;
    }
}