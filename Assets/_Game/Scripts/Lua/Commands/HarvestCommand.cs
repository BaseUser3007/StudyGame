using System.Threading;
using System.Threading.Tasks;
using Lua;
using UnityEngine;

public class HarvestCommand : PlayerCommandBase
{
    public override string Name => "harvest";
    // Подбор с земли дешёвый: только плата за вызов (прочность тратится при mine)
    public override float EnergyCost => 1f;
    public override string Signature => "harvest([x, y])";
    public override string Description => "Подобрать с земли в инвентарь: палки, камни, выпавшую добычу. " +
                                          "Без аргументов — ближайшее к персонажу, harvest(x, y) или harvest(obj) — в точке рядом. " +
                                          "Стоит только плату за вызов. Деревья и валуны не подбираются — для них mine(). " +
                                          "Возвращает true или false и причину";

    private readonly ResourceHarvester _harvester;

    public HarvestCommand(
        PlayerCommandExecutor executor,
        IMapDataProvider mapData,
        ResourceHarvester harvester)
        : base(executor, mapData)
    {
        _harvester = harvester;
    }

    // Команда мгновенная — без async, чтобы не было пустого конечного автомата
    protected override ValueTask<int> ExecuteAsync(LuaFunctionExecutionContext context, CancellationToken ct)
    {
        string error;
        if (context.ArgumentCount == 0 || context.GetArgument(0).Type == LuaValueType.Nil)
            error = _harvester.TryHarvestNearest();
        else if (TryReadPoint(context, out Vector2 point))
            error = _harvester.TryHarvestAt(point);
        else
            error = "usage: harvest(), harvest(x, y) or harvest(obj)";

        if (error == null)
        {
            context.Return(true);
            return new ValueTask<int>(1);
        }

        context.Return(false, error);
        return new ValueTask<int>(2);
    }

    // ─── Helpers ──────────────────────────────────────────────

    /// <summary>
    /// Принимает harvest(x, y) или harvest(obj), где obj — таблица с полями x и y.
    /// Возвращает false, если аргументы не подходят.
    /// </summary>
    public static bool TryReadPoint(LuaFunctionExecutionContext context, out Vector2 point)
    {
        point = default;
        double x, y;

        if (context.GetArgument(0).TryRead(out LuaTable table))
        {
            if (!table["x"].TryRead(out x) || !table["y"].TryRead(out y))
                return false;
        }
        else if (context.ArgumentCount < 2
                 || !context.GetArgument(0).TryRead(out x)
                 || !context.GetArgument(1).TryRead(out y))
        {
            return false;
        }

        point = new Vector2((float)x, (float)y);
        return true;
    }
}
