using System;
using System.Threading;
using Lua;
using System.Threading.Tasks;
using UnityEngine;

public class MoveToCommand : PlayerCommandBase
{
    public override string Name => "moveTo";
    public override float EnergyCost => 2f;
    public override string Signature => "moveTo(x, y)";
    public override string Description => "Идти к тайлу, содержащему точку (x, y), по прямой; moveTo(obj) — к объекту или врагу из findObjects/check " +
                                          "(врага — туда, где он был при поиске). Вода останавливает, объекты обходятся; " +
                                          "если застрял — стоп через пару секунд. Возвращает число пройденных тайлов";

    public MoveToCommand(PlayerCommandExecutor executor, IMapDataProvider mapData)
        : base(executor, mapData) { }

    protected override async ValueTask<int> ExecuteAsync(LuaFunctionExecutionContext context, CancellationToken ct)
    {
        // moveTo(x, y) или moveTo(obj) — таблица с полями x и y
        if (context.ArgumentCount == 0 || !HarvestCommand.TryReadPoint(context, out Vector2 point))
            throw new ArgumentException("moveTo: usage moveTo(x, y) or moveTo(obj)");

        // Координаты объектов дробные — идём в тайл, где лежит точка
        int stepsTaken = await Executor.MoveToAsync(MapPosition.ToTile(point), ct);
        context.Return(stepsTaken);
        return 1;
    }
}
