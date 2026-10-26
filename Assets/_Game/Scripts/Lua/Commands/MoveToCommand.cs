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
    public override string Description =>
        "Walk in a straight line to the tile containing the point (x, y); moveTo(obj) — to an object or enemy from findObjects/check " +
        "(an enemy — to where it was when found). Water stops you, objects are walked around; " +
        "if stuck — stops after a couple of seconds. Returns the number of tiles walked";

    public MoveToCommand(PlayerCommandExecutor executor, IMapDataProvider mapData)
        : base(executor, mapData) { }

    protected override async ValueTask<int> ExecuteAsync(LuaFunctionExecutionContext context, CancellationToken ct)
    {
        // moveTo(x, y) или moveTo(obj) — таблица с полями x и y
        // Портал сплошной: moveTo(portal) идёт на клетку перед ним (approachX, approachY), а не в центр
        if (!LuaPortal.TryReadApproach(context, out Vector2 point)
            && (context.ArgumentCount == 0 || !HarvestCommand.TryReadPoint(context, out point)))
            throw new ArgumentException("moveTo: usage moveTo(x, y) or moveTo(obj)");

        // Координаты объектов дробные — идём в тайл, где лежит точка
        int stepsTaken = await Executor.MoveToAsync(MapPosition.ToTile(point), ct);
        context.Return(stepsTaken);
        return 1;
    }
}
