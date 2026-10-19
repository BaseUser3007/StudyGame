using System.Threading;
using System.Threading.Tasks;
using Lua;

/// <summary>
/// Содержимое инвентаря для кода: занятые ячейки с названием, количеством и прочностью инструментов.
/// </summary>
public class GetInventoryCommand : LuaCommandBase
{
    public override string Name => "getInventory";
    public override float EnergyCost => 1f;
    public override string Signature => "getInventory()";
    public override string Description => "Содержимое инвентаря: список занятых ячеек { slot, name, count }, " +
                                          "у инструментов ещё tool, durability и maxDurability, " +
                                          "у еды — food = true, energy и health (сколько восстанавливает eat). " +
                                          "У результата есть count (занято ячеек) и free (свободно)";

    private readonly PlayerInventory _inventory;

    public GetInventoryCommand(PlayerInventory inventory)
    {
        _inventory = inventory;
    }

    protected override ValueTask<int> ExecuteAsync(LuaFunctionExecutionContext context, CancellationToken ct)
    {
        LuaTable result = new LuaTable();
        int occupied = 0;

        for (int i = 0; i < _inventory.SlotCount; i++)
        {
            InventorySlot slot = _inventory.GetSlot(i);
            if (slot.IsEmpty) continue;

            occupied++;
            result[occupied] = BuildSlotTable(i, slot);
        }

        result["count"] = (double)occupied;
        result["free"] = (double)(_inventory.SlotCount - occupied);

        context.Return(result);
        return new ValueTask<int>(1);
    }

    // ─── Helpers ──────────────────────────────────────────────

    private static LuaTable BuildSlotTable(int index, InventorySlot slot)
    {
        LuaTable table = new LuaTable();

        // Номер ячейки как на панели (с единицы) — подходит для drop(slot)
        table["slot"] = (double)(index + 1);
        // То же название, что принимают drop() и craft()
        table["name"] = string.IsNullOrEmpty(slot.Item.itemId) ? slot.Item.DisplayName : slot.Item.itemId;
        table["count"] = (double)slot.Count;

        if (slot.Tool != null)
        {
            table["tool"] = slot.Tool.toolType.ToString().ToLower();
            table["durability"] = (double)slot.Durability;
            table["maxDurability"] = (double)slot.MaxDurability;
        }

        // Еда: сколько восстанавливает одна штука (eat)
        if (slot.Item is FoodItem food)
        {
            table["food"] = true;
            table["energy"] = (double)food.EnergyRestore;
            table["health"] = (double)food.HealthRestore;
        }

        return table;
    }
}
