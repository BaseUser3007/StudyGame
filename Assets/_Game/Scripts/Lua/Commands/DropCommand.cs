using System.Threading;
using System.Threading.Tasks;
using Lua;

/// <summary>
/// Выбрасывает ресурсы из инвентаря: по номеру ячейки или по названию.
/// Предметы просто исчезают — подбираемых предметов на карте пока нет.
/// </summary>
public class DropCommand : LuaCommandBase
{
    public override string Name => "drop";
    public override float EnergyCost => 1f;
    public override string Signature => "drop(slot or name, [amount])";
    public override string Description => "Выбросить предметы: drop(1) — всю ячейку 1, drop(\"stone\") — все камни, " +
                                 "вторым аргументом можно указать количество. Возвращает true и число выброшенных или false и причину";

    private const string Usage = "usage: drop(slot, [amount]) or drop(\"item\", [amount])";

    private readonly PlayerInventory _inventory;

    public DropCommand(PlayerInventory inventory)
    {
        _inventory = inventory;
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

        // drop(1) — номер ячейки, как на панели инвентаря (с единицы)
        if (target.Type == LuaValueType.Number && target.TryRead(out double rawSlot))
        {
            if (rawSlot % 1 != 0 || rawSlot < 1 || rawSlot > _inventory.SlotCount)
                return $"slot must be 1..{_inventory.SlotCount}";

            int index = (int)rawSlot - 1;
            if (_inventory.GetSlot(index).IsEmpty)
                return $"slot {(int)rawSlot} is empty";

            dropped = _inventory.RemoveFromSlot(index, amount);
            return null;
        }

        // drop("stone") — по названию ресурса
        if (target.TryRead(out string itemName))
        {
            ResourceItem item = _inventory.FindItem(itemName);
            if (item == null)
                return $"no {itemName} in inventory";

            dropped = _inventory.Remove(item, amount);
            return null;
        }

        return Usage;
    }
}
