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
    public override string Description =>
        "Pick up from the ground into the inventory: sticks, stones, dropped loot. " +
        "Without arguments — the nearest one to the player, harvest(x, y) or harvest(obj) — at a nearby point. " +
        "Costs only the call. Trees and boulders can't be picked up — use mine() for them, enemies are hit with attack(). " +
        "Returns true or false and a reason";

    private readonly ResourceHarvester _harvester;
    private readonly ObjectManager _objectManager;
    private readonly EnemyManager _enemies;
    private readonly PuzzleManager _puzzles;

    public HarvestCommand(
        PlayerCommandExecutor executor,
        IMapDataProvider mapData,
        ResourceHarvester harvester,
        ObjectManager objectManager,
        EnemyManager enemies,
        PuzzleManager puzzles = null)
        : base(executor, mapData)
    {
        _harvester = harvester;
        _objectManager = objectManager;
        _enemies = enemies;
        _puzzles = puzzles;
    }

    // Команда мгновенная — без async, чтобы не было пустого конечного автомата
    protected override ValueTask<int> ExecuteAsync(LuaFunctionExecutionContext context, CancellationToken ct)
    {
        string error;
        if (context.ArgumentCount == 0 || context.GetArgument(0).Type == LuaValueType.Nil)
            error = _harvester.TryHarvestNearest();
        else if (TryReadPoint(context, out Vector2 point))
        {
            // Подвижный объект ищется по id; иначе враг на точке — не ресурс: подсказываем attack() вместо «no object at»
            int movingId = ReadMovingObjectId(context, _objectManager);
            error = (movingId != 0 ? null
                        : AttackCommand.GetNotHarvestableError(context, point, _objectManager, _enemies, "harvested", _puzzles))
                    ?? _harvester.TryHarvestAt(point, movingId);
        }
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

    /// <summary>
    /// id подвижного объекта карты (овцы) из таблицы findObjects (kind = "object"), иначе 0: такой объект
    /// мог уйти со своей точки, поэтому ищется по id, а не по координатам. Только живые подвижные объекты —
    /// экземпляры остальных переиспользует пул, и старая таблица могла бы попасть в новый объект.
    /// </summary>
    public static int ReadMovingObjectId(LuaFunctionExecutionContext context, ObjectManager objectManager)
    {
        if (objectManager == null || !context.GetArgument(0).TryRead(out LuaTable table)) return 0;
        if (!table["kind"].TryRead(out string kind) || kind != LuaObjectList.KindObject) return 0;
        if (!table["id"].TryRead(out double value)) return 0;
        int id = (int)value;
        return objectManager.GetById(id) is SheepObject ? id : 0;
    }
}
