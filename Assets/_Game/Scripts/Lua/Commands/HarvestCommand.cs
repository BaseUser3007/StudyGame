using System.Threading.Tasks;
using Lua;
using UnityEngine;

public class HarvestCommand : PlayerCommandBase
{
    public override string Name => "harvest";
    // Реальная стоимость зависит от прочности ресурса и списывается при добыче
    public override float EnergyCost => 0f;
    public override string Signature => "harvest([x, y])";
    public override string Description => "Без аргументов — добыть ближайший к персонажу объект. " +
                                          "harvest(x, y) или harvest(obj) — объект в точке рядом с персонажем. " +
                                          "Тратит энергию по прочности ресурса. Возвращает true или false и причину";

    private readonly ObjectManager _objectManager;
    private readonly PlayerEnergy _playerEnergy;
    private readonly PlayerInventory _inventory;

    // Насколько далеко от указанной точки может лежать объект
    private const float PickTolerance = 0.5f;
    // Радиус поиска в тайлах для harvest() без аргументов — не меньше InteractionRadius объектов
    private const int SearchRadius = 2;

    public HarvestCommand(
        PlayerCommandExecutor executor,
        IMapDataProvider mapData,
        ObjectManager objectManager,
        PlayerEnergy playerEnergy,
        PlayerInventory inventory)
        : base(executor, mapData)
    {
        _objectManager = objectManager;
        _playerEnergy = playerEnergy;
        _inventory = inventory;
    }

    public override void Register(LuaState state)
    {
        // Команда мгновенная — без async, чтобы не было пустого конечного автомата
        state.Environment[Name] = new LuaFunction((context, ct) =>
        {
            string error;
            if (context.ArgumentCount == 0 || context.GetArgument(0).Type == LuaValueType.Nil)
                error = TryHarvestNearest();
            else if (TryReadPoint(context, out Vector2 point))
                error = TryHarvestAt(point);
            else
                error = "usage: harvest(), harvest(x, y) or harvest(obj)";

            if (error == null)
            {
                context.Return(true);
                return new ValueTask<int>(1);
            }

            context.Return(false, error);
            return new ValueTask<int>(2);
        });
    }

    // ─── Helpers ──────────────────────────────────────────────

    /// <summary>
    /// Принимает harvest(x, y) или harvest(obj), где obj — таблица с полями x и y.
    /// Возвращает false, если аргументы не подходят.
    /// </summary>
    private static bool TryReadPoint(LuaFunctionExecutionContext context, out Vector2 point)
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
    /// Возвращает null при успехе, иначе причину неудачи.
    /// </summary>
    private string TryHarvestAt(Vector2 point)
    {
        ResourceObject obj = _objectManager.GetNearestObjectAt(point, PickTolerance);
        if (obj == null || obj.Variant == null)
            return $"no object at ({point.x:0.##}, {point.y:0.##})";

        if (Vector2.Distance(Executor.CurrentTile, obj.Position) > obj.InteractionRadius)
            return "object is too far";

        return TryHarvest(obj);
    }

    /// <summary>
    /// Ближайший к персонажу объект в зоне досягаемости. Объекты, для которых
    /// нужен инструмент, пропускаются, если рядом есть что собрать без него.
    /// </summary>
    private string TryHarvestNearest()
    {
        Vector2Int playerTile = Executor.CurrentTile;
        ResourceObject nearest = null;
        ResourceObject nearestWithTool = null;
        float minDistance = float.MaxValue;
        float minDistanceWithTool = float.MaxValue;

        foreach (ResourceObject obj in _objectManager.GetObjectsInRadius<ResourceObject>(playerTile, SearchRadius))
        {
            if (obj.Variant == null) continue;

            float distance = Vector2.Distance(playerTile, obj.Position);
            if (distance > obj.InteractionRadius) continue;

            if (obj.RequiredTool != null)
            {
                if (distance < minDistanceWithTool)
                {
                    minDistanceWithTool = distance;
                    nearestWithTool = obj;
                }
            }
            else if (distance < minDistance)
            {
                minDistance = distance;
                nearest = obj;
            }
        }

        ResourceObject target = nearest ?? nearestWithTool;
        if (target == null)
            return "no objects nearby";

        return TryHarvest(target);
    }

    private string TryHarvest(ResourceObject obj)
    {
        if (obj.RequiredTool != null)
            return $"tool required: {obj.RequiredTool.Value.ToString().ToLower()}";

        // Без добычи не тратим энергию впустую (ошибка настройки ResourceVariant)
        ResourceVariant variant = obj.Variant;
        if (variant.drop == null || variant.dropAmount <= 0)
            return "nothing to harvest";

        if (!_inventory.CanAdd(variant.drop, variant.dropAmount))
            return "inventory is full";

        float cost = variant.HarvestEnergyCost;
        if (!_playerEnergy.TrySpend(cost))
            return $"not enough energy: need {cost:0.#}, have {_playerEnergy.Current:0.#}";

        var drop = obj.Interact();
        if (drop.HasValue)
            _inventory.Add(drop.Value.item, drop.Value.amount);

        return null;
    }
}
