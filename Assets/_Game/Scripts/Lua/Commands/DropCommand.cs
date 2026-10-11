using System.Threading;
using System.Threading.Tasks;
using Lua;
using UnityEngine;

/// <summary>
/// Выбрасывает ресурсы из инвентаря: по номеру ячейки или по названию.
/// Предметы с World Prefab появляются на земле рядом с персонажем (по объекту на штуку)
/// и подбираются через harvest; остальные (инструменты, постройки) просто исчезают.
/// </summary>
public class DropCommand : PlayerCommandBase
{
    public override string Name => "drop";
    public override float EnergyCost => 1f;
    public override string Signature => "drop(slot or name, [amount])";
    public override string Description => "Выбросить предметы на землю рядом с персонажем: drop(1) — всю ячейку 1, " +
                                          "drop(\"stone\") — все камни, вторым аргументом можно указать количество. " +
                                          "Выброшенное подбирается через harvest (предметы без объекта на земле исчезают). " +
                                          "Возвращает true и число выброшенных или false и причину";

    private const string Usage = "usage: drop(slot, [amount]) or drop(\"item\", [amount])";
    // Насколько далеко от персонажа разлетаются выброшенные предметы (в тайлах)
    private const float ScatterRadius = 0.9f;
    private const int PlaceAttempts = 10;

    private readonly PlayerInventory _inventory;
    private readonly ObjectManager _objectManager;

    public DropCommand(
        PlayerCommandExecutor executor,
        IMapDataProvider mapData,
        PlayerInventory inventory,
        ObjectManager objectManager)
        : base(executor, mapData)
    {
        _inventory = inventory;
        _objectManager = objectManager;
    }

    // Команда мгновенная — без async, чтобы не было пустого конечного автомата
    protected override ValueTask<int> ExecuteAsync(LuaFunctionExecutionContext context, CancellationToken ct)
    {
        string error = TryDrop(context, out int dropped);

        if (error == null)
        {
            context.Return(true, dropped);
            return new ValueTask<int>(2);
        }

        context.Return(false, error);
        return new ValueTask<int>(2);
    }

    // ─── Helpers ──────────────────────────────────────────────

    /// <summary>
    /// Возвращает null при успехе, иначе причину неудачи.
    /// </summary>
    private string TryDrop(LuaFunctionExecutionContext context, out int dropped)
    {
        dropped = 0;
        if (context.ArgumentCount == 0) return Usage;

        // Количество — необязательный второй аргумент; без него выбрасываем всё
        int amount = int.MaxValue;
        if (context.ArgumentCount > 1 && context.GetArgument(1).Type != LuaValueType.Nil)
        {
            if (!context.GetArgument(1).TryRead(out double rawAmount) || rawAmount < 1 || rawAmount % 1 != 0)
                return "amount must be a positive integer";
            amount = (int)rawAmount;
        }

        LuaValue target = context.GetArgument(0);
        ResourceItem item;

        // drop(1) — номер ячейки, как на панели инвентаря (с единицы)
        if (target.Type == LuaValueType.Number && target.TryRead(out double rawSlot))
        {
            if (rawSlot % 1 != 0 || rawSlot < 1 || rawSlot > _inventory.SlotCount)
                return $"slot must be 1..{_inventory.SlotCount}";

            int index = (int)rawSlot - 1;
            InventorySlot slot = _inventory.GetSlot(index);
            if (slot.IsEmpty)
                return $"slot {(int)rawSlot} is empty";

            item = slot.Item;
            dropped = _inventory.RemoveFromSlot(index, amount);
        }
        // drop("stone") — по названию ресурса
        else if (target.TryRead(out string itemName))
        {
            item = _inventory.FindItem(itemName);
            if (item == null)
                return $"no {itemName} in inventory";

            dropped = _inventory.Remove(item, amount);
        }
        else
        {
            return Usage;
        }

        PlaceOnGround(item, dropped);
        return null;
    }

    /// <summary>
    /// Раскладывает выброшенное вокруг персонажа — по объекту на штуку.
    /// </summary>
    private void PlaceOnGround(ResourceItem item, int count)
    {
        if (item.worldPrefab == null) return;

        Vector2 center = Executor.CurrentTile;
        for (int i = 0; i < count; i++)
            _objectManager.Spawn(item.worldPrefab, FindDropPoint(center));
    }

    /// <summary>
    /// Случайная точка на суше рядом с персонажем; не нашлась — тайл персонажа.
    /// </summary>
    private Vector2 FindDropPoint(Vector2 center)
    {
        for (int attempt = 0; attempt < PlaceAttempts; attempt++)
        {
            Vector2 point = center + Random.insideUnitCircle * ScatterRadius;
            Vector2Int tile = MapPosition.ToTile(point);
            if (MapData.IsWalkable(tile.x, tile.y))
                return point;
        }

        return center;
    }
}
