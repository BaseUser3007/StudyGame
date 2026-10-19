using UnityEngine;

/// <summary>
/// Работа персонажа с ресурсами на карте. Два действия:
///   • harvest — подобрать то, что лежит на земле (палки, камни, выпавшая добыча): сразу в инвентарь;
///   • mine — добыть объект, которому нужен инструмент (дерево, валун): инструмент изнашивается,
///     добыча (ResourceVariant.loot) выпадает на землю вокруг объекта.
/// Энергия: подбор — только плата за вызов команды; добыча mine — сумма «прочность × количество»
/// того, что выпадает (ResourceVariant.HarvestEnergyCost), см. GetEnergyCost.
/// Общая для harvest()/mine() и готовых команд (obtain), чтобы добыча везде стоила одинаково.
/// Все методы возвращают null при успехе, иначе причину неудачи.
/// </summary>
public class ResourceHarvester
{
    // Насколько далеко от указанной точки может лежать объект
    private const float PickTolerance = 0.5f;
    // Радиус поиска в тайлах вокруг персонажа — не меньше InteractionRadius объектов
    private const int ReachSearchRadius = 2;
    // Насколько далеко от добытого объекта разлетается добыча (в тайлах)
    private const float LootScatterRadius = 0.7f;
    private const int LootPlaceAttempts = 10;

    private readonly PlayerCommandExecutor _executor;
    private readonly IMapDataProvider _mapData;
    private readonly ObjectManager _objectManager;
    private readonly PlayerEnergy _playerEnergy;
    private readonly PlayerInventory _inventory;

    public ResourceHarvester(
        PlayerCommandExecutor executor,
        IMapDataProvider mapData,
        ObjectManager objectManager,
        PlayerEnergy playerEnergy,
        PlayerInventory inventory)
    {
        _executor = executor;
        _mapData = mapData;
        _objectManager = objectManager;
        _playerEnergy = playerEnergy;
        _inventory = inventory;
    }

    /// <summary>
    /// Объект добывается командой mine (нужен инструмент), а не подбирается.
    /// </summary>
    public static bool IsMinable(ResourceObject obj) => obj is ExtractableResource;

    /// <summary>
    /// Энергия на объект сверх платы за команду: добыча mine — по прочности добычи, подбор — 0.
    /// </summary>
    public static float GetEnergyCost(ResourceObject obj)
    {
        return IsMinable(obj) && obj.Variant != null ? obj.Variant.HarvestEnergyCost : 0f;
    }

    // ─── Harvest (подобрать) ──────────────────────────────────

    /// <summary>
    /// Подобрать объект, ближайший к точке (не дальше PickTolerance).
    /// </summary>
    public string TryHarvestAt(Vector2 point)
    {
        ResourceObject obj = FindAt(point, out string error);
        return obj != null ? TryHarvest(obj) : error;
    }

    /// <summary>
    /// Подобрать ближайший к персонажу объект в досягаемости (деревья и валуны пропускаются).
    /// </summary>
    public string TryHarvestNearest()
    {
        ResourceObject target = FindNearestInReach(minable: false);
        return target != null ? TryHarvest(target) : "nothing to pick up nearby";
    }

    /// <summary>
    /// Подобрать конкретный объект: он должен быть в досягаемости персонажа.
    /// </summary>
    public string TryHarvest(ResourceObject obj)
    {
        string problem = CheckReach(obj) ?? CheckHarvestable(obj);
        if (problem != null) return problem;

        // Подбор с земли дешёвый: энергия — только плата за вызов команды, прочность тратится при mine
        var drop = obj.Interact();
        if (drop.HasValue && drop.Value.item != null)
        {
            // Выброшенное игроком — не новая добыча (достижения такие события пропускают)
            bool recycled = obj.DroppedByPlayer;
            // Для EfficientGather: объект появился в этом же запуске кода (лут от mine в нём)
            bool fromThisRun = GameEventBus.IsFromActiveRun(obj.SpawnRunId);
            _inventory.Add(drop.Value.item, drop.Value.amount, recycled, fromThisRun);
            GameEventBus.Publish(new GameEvent(GameEventType.Harvested, MetaProgress.GetItemKey(drop.Value.item),
                drop.Value.amount, LuaObjectList.GetTypeName(obj), recycled, fromThisRun));
        }

        return null;
    }

    /// <summary>
    /// Можно ли подобрать объект (без учёта расстояния и энергии).
    /// </summary>
    public string CheckHarvestable(ResourceObject obj)
    {
        if (IsMinable(obj))
            return $"{TypeName(obj)} can't be picked up — use mine()";

        // Без добычи не тратим энергию впустую (ошибка настройки ResourceVariant)
        ResourceVariant variant = obj.Variant;
        if (variant == null || variant.drop == null || variant.dropAmount <= 0)
            return "nothing to harvest";

        if (!_inventory.CanAdd(variant.drop, variant.dropAmount))
            return "inventory is full";

        return null;
    }

    // ─── Mine (добыть инструментом) ───────────────────────────

    /// <summary>
    /// Добыть объект, ближайший к точке (не дальше PickTolerance).
    /// </summary>
    public string TryMineAt(Vector2 point)
    {
        ResourceObject obj = FindAt(point, out string error);
        return obj != null ? TryMine(obj) : error;
    }

    /// <summary>
    /// Добыть ближайший к персонажу объект, которому нужен инструмент.
    /// </summary>
    public string TryMineNearest()
    {
        ResourceObject target = FindNearestInReach(minable: true);
        return target != null ? TryMine(target) : "nothing to mine nearby";
    }

    /// <summary>
    /// Добыть конкретный объект: нужен исправный инструмент; добыча выпадает на землю вокруг объекта.
    /// </summary>
    public string TryMine(ResourceObject obj)
    {
        string problem = CheckReach(obj) ?? CheckMinable(obj);
        if (problem != null) return problem;

        problem = SpendEnergy(obj.Variant.HarvestEnergyCost);
        if (problem != null) return problem;

        // Инструмент изнашивается за каждую добычу; сломанный исчезает
        int toolSlot = _inventory.FindToolSlot(obj.RequiredTool.Value);
        if (toolSlot >= 0 && _inventory.GetSlot(toolSlot).Tool is ToolItem tool)
            _inventory.WearTool(toolSlot, tool.wearPerUse);

        Vector2 origin = obj.Position;
        ResourceVariant variant = obj.Variant;
        string typeName = LuaObjectList.GetTypeName(obj);
        obj.Interact();
        GameEventBus.Publish(GameEventType.Mined, typeName);

        foreach (LootEntry entry in variant.MiningLoot)
            DropLoot(entry.item, entry.amount, origin);

        return null;
    }

    /// <summary>
    /// Можно ли добыть объект (без учёта расстояния и энергии).
    /// </summary>
    public string CheckMinable(ResourceObject obj)
    {
        if (!IsMinable(obj) || obj.RequiredTool == null)
            return $"{TypeName(obj)} doesn't need mining — pick it up with harvest()";

        if (_inventory.FindToolSlot(obj.RequiredTool.Value) < 0)
            return $"tool required: {obj.RequiredTool.Value.ToString().ToLower()}";

        if (obj.Variant == null)
            return "nothing to mine";

        foreach (LootEntry _ in obj.Variant.MiningLoot)
            return null;
        return "nothing to mine";
    }

    // ─── Helpers ──────────────────────────────────────────────

    private ResourceObject FindAt(Vector2 point, out string error)
    {
        ResourceObject obj = _objectManager.GetNearestObjectAt(point, PickTolerance);
        error = obj == null || obj.Variant == null ? $"no object at ({point.x:0.##}, {point.y:0.##})" : null;
        return error == null ? obj : null;
    }

    private ResourceObject FindNearestInReach(bool minable)
    {
        Vector2Int playerTile = _executor.CurrentTile;
        ResourceObject nearest = null;
        float minDistance = float.MaxValue;

        foreach (ResourceObject obj in _objectManager.GetObjectsInRadius<ResourceObject>(playerTile, ReachSearchRadius))
        {
            if (obj.Variant == null || IsMinable(obj) != minable) continue;

            float distance = Vector2.Distance(playerTile, obj.Position);
            if (distance > obj.InteractionRadius || distance >= minDistance) continue;

            minDistance = distance;
            nearest = obj;
        }

        return nearest;
    }

    private string CheckReach(ResourceObject obj)
    {
        if (obj == null || !obj.IsAvailable)
            return "object is gone";

        if (Vector2.Distance(_executor.CurrentTile, obj.Position) > obj.InteractionRadius)
            return "object is too far";

        return null;
    }

    private string SpendEnergy(float cost)
    {
        return _playerEnergy.TrySpend(cost)
            ? null
            : $"not enough energy: need {cost:0.#}, have {_playerEnergy.Current:0.#}";
    }

    /// <summary>
    /// Добыча с World Prefab ложится на землю (по объекту на штуку), остальное — сразу в инвентарь.
    /// </summary>
    private void DropLoot(ResourceItem item, int amount, Vector2 origin)
    {
        if (item.worldPrefab == null)
        {
            _inventory.Add(item, amount);
            return;
        }

        for (int i = 0; i < amount; i++)
        {
            ResourceObject loot = _objectManager.Spawn(item.worldPrefab, FindLootPoint(origin));
            // Лут помечается запуском: подобрать его «эффективно» можно только в этом же запуске
            if (loot != null) loot.SpawnRunId = GameEventBus.ActiveRunId;
        }
    }

    /// <summary>
    /// Случайная точка на суше рядом с добытым объектом; не нашлась — сама точка объекта.
    /// </summary>
    private Vector2 FindLootPoint(Vector2 origin)
    {
        for (int attempt = 0; attempt < LootPlaceAttempts; attempt++)
        {
            Vector2 point = origin + Random.insideUnitCircle * LootScatterRadius;
            Vector2Int tile = MapPosition.ToTile(point);
            if (_mapData.IsWalkable(tile.x, tile.y))
                return point;
        }

        return origin;
    }

    private static string TypeName(ResourceObject obj)
    {
        return obj.GetType().Name.ToLower().Replace("object", "");
    }
}
