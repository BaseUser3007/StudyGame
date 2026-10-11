using UnityEngine;

/// <summary>
/// Добыча ресурсов персонажем: проверки (дистанция, инструмент, место в инвентаре),
/// трата энергии по прочности ресурса и перенос добычи в инвентарь.
/// Общая для harvest() и готовых команд (obtain), чтобы добыча везде стоила одинаково.
/// Все методы возвращают null при успехе, иначе причину неудачи.
/// </summary>
public class ResourceHarvester
{
    // Насколько далеко от указанной точки может лежать объект
    private const float PickTolerance = 0.5f;
    // Радиус поиска в тайлах вокруг персонажа — не меньше InteractionRadius объектов
    private const int ReachSearchRadius = 2;

    private readonly PlayerCommandExecutor _executor;
    private readonly ObjectManager _objectManager;
    private readonly PlayerEnergy _playerEnergy;
    private readonly PlayerInventory _inventory;

    public ResourceHarvester(
        PlayerCommandExecutor executor,
        ObjectManager objectManager,
        PlayerEnergy playerEnergy,
        PlayerInventory inventory)
    {
        _executor = executor;
        _objectManager = objectManager;
        _playerEnergy = playerEnergy;
        _inventory = inventory;
    }

    // ─── Public API ───────────────────────────────────────────

    /// <summary>
    /// Объект, ближайший к точке (не дальше PickTolerance).
    /// </summary>
    public string TryHarvestAt(Vector2 point)
    {
        ResourceObject obj = _objectManager.GetNearestObjectAt(point, PickTolerance);
        if (obj == null || obj.Variant == null)
            return $"no object at ({point.x:0.##}, {point.y:0.##})";

        return TryHarvest(obj);
    }

    /// <summary>
    /// Ближайший к персонажу объект в зоне досягаемости. Объекты, для которых
    /// нужен инструмент, пропускаются, если рядом есть что собрать без него.
    /// </summary>
    public string TryHarvestNearest()
    {
        Vector2Int playerTile = _executor.CurrentTile;
        ResourceObject nearest = null;
        ResourceObject nearestWithTool = null;
        float minDistance = float.MaxValue;
        float minDistanceWithTool = float.MaxValue;

        foreach (ResourceObject obj in _objectManager.GetObjectsInRadius<ResourceObject>(playerTile, ReachSearchRadius))
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

    /// <summary>
    /// Добыть конкретный объект: он должен быть в зоне досягаемости персонажа.
    /// </summary>
    public string TryHarvest(ResourceObject obj)
    {
        if (obj == null || !obj.IsAvailable)
            return "object is gone";

        if (Vector2.Distance(_executor.CurrentTile, obj.Position) > obj.InteractionRadius)
            return "object is too far";

        string problem = CheckHarvestable(obj);
        if (problem != null) return problem;

        float cost = obj.Variant.HarvestEnergyCost;
        if (!_playerEnergy.TrySpend(cost))
            return $"not enough energy: need {cost:0.#}, have {_playerEnergy.Current:0.#}";

        var drop = obj.Interact();
        if (drop.HasValue)
            _inventory.Add(drop.Value.item, drop.Value.amount);

        // Инструмент изнашивается за каждую добычу; сломанный исчезает
        if (obj.RequiredTool != null)
        {
            int toolSlot = _inventory.FindToolSlot(obj.RequiredTool.Value);
            if (toolSlot >= 0 && _inventory.GetSlot(toolSlot).Tool is ToolItem tool)
                _inventory.WearTool(toolSlot, tool.wearPerUse);
        }

        return null;
    }

    /// <summary>
    /// Можно ли добыть объект в принципе (без учёта расстояния и энергии):
    /// есть нужный исправный инструмент, есть добыча и для неё есть место.
    /// </summary>
    public string CheckHarvestable(ResourceObject obj)
    {
        if (obj.RequiredTool != null && _inventory.FindToolSlot(obj.RequiredTool.Value) < 0)
            return $"tool required: {obj.RequiredTool.Value.ToString().ToLower()}";

        // Без добычи не тратим энергию впустую (ошибка настройки ResourceVariant)
        ResourceVariant variant = obj.Variant;
        if (variant == null || variant.drop == null || variant.dropAmount <= 0)
            return "nothing to harvest";

        if (!_inventory.CanAdd(variant.drop, variant.dropAmount))
            return "inventory is full";

        return null;
    }
}
