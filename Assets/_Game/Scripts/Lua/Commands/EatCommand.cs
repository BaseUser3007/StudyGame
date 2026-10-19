using System.Threading;
using System.Threading.Tasks;
using Lua;

/// <summary>
/// eat(slot | "name") — съесть одну штуку еды (FoodItem) из инвентаря: восстанавливает энергию
/// и здоровье не выше максимума. Если всё, что даёт еда, уже на максимуме — предмет не тратится
/// (вызов всё равно оплачен). Публикует GameEvent FoodEaten (Id = ключ предмета).
/// </summary>
public class EatCommand : LuaCommandBase
{
    public override string Name => "eat";
    public override float EnergyCost => 1f;
    public override string Signature => "eat(slot or name)";
    public override string Description => "Съесть одну штуку еды: eat(\"berries\") или eat(1) — по номеру ячейки. " +
                                          "Восстанавливает энергию и/или здоровье, но не выше максимума. " +
                                          "Возвращает true и сколько восстановлено (энергия, здоровье) " +
                                          "или nil и причину (\"already full\" — еда не тратится)";

    public const string AlreadyFull = "already full";
    private const string Usage = "usage: eat(slot) or eat(\"food\")";
    // Погрешность сравнения float при проверке «уже полон»
    private const float FullEpsilon = 0.001f;

    private readonly PlayerEnergy _energy;
    private readonly PlayerHealth _health;
    private readonly PlayerInventory _inventory;

    public EatCommand(PlayerEnergy energy, PlayerHealth health, PlayerInventory inventory)
    {
        _energy = energy;
        _health = health;
        _inventory = inventory;
    }

    // Команда мгновенная — без async, чтобы не было пустого конечного автомата
    protected override ValueTask<int> ExecuteAsync(LuaFunctionExecutionContext context, CancellationToken ct)
    {
        string error = TryEat(context, out float energy, out float health);

        if (error == null)
        {
            context.Return(true, (double)energy, (double)health);
            return new ValueTask<int>(3);
        }

        context.Return(LuaValue.Nil, error);
        return new ValueTask<int>(2);
    }

    // ─── Helpers ──────────────────────────────────────────────

    /// <summary>
    /// Возвращает null при успехе, иначе причину неудачи.
    /// </summary>
    private string TryEat(LuaFunctionExecutionContext context, out float energy, out float health)
    {
        energy = 0f;
        health = 0f;
        if (context.ArgumentCount == 0) return Usage;

        LuaValue target = context.GetArgument(0);
        int slotIndex;

        // eat(1) — номер ячейки, как на панели инвентаря (с единицы)
        if (target.Type == LuaValueType.Number && target.TryRead(out double rawSlot))
        {
            if (rawSlot % 1 != 0 || rawSlot < 1 || rawSlot > _inventory.SlotCount)
                return $"slot must be 1..{_inventory.SlotCount}";

            slotIndex = (int)rawSlot - 1;
            InventorySlot slot = _inventory.GetSlot(slotIndex);
            if (slot.IsEmpty)
                return $"slot {(int)rawSlot} is empty";
            if (!(slot.Item is FoodItem))
                return $"{slot.Item.DisplayName} is not food";
        }
        // eat("berries") — по названию (itemId / отображаемое имя / имя ассета)
        else if (target.TryRead(out string itemName))
        {
            slotIndex = FindFoodSlot(itemName, out bool foundNonFood);
            if (slotIndex < 0)
                return foundNonFood ? $"{itemName} is not food" : $"no {itemName} in inventory";
        }
        else
        {
            return Usage;
        }

        var food = (FoodItem)_inventory.GetSlot(slotIndex).Item;
        if (food.EnergyRestore <= 0f && food.HealthRestore <= 0f)
            return $"{food.DisplayName} restores nothing";

        // Проверяем заранее: «уже полон» — предмет не тратится. Плата за сам вызов к этому моменту
        // уже списана — она не считается «местом» для энергии, иначе ягода при полной энергии
        // тратилась бы ради возврата этой платы
        bool energyRoom = food.EnergyRestore > 0f && _energy.Max - _energy.Current > EnergyCost + FullEpsilon;
        bool healthRoom = food.HealthRestore > 0f && _health != null && !_health.IsDead
                          && _health.Max - _health.Current > FullEpsilon;
        if (!energyRoom && !healthRoom)
            return AlreadyFull;

        if (_inventory.RemoveFromSlot(slotIndex, 1) < 1)
            return $"no {food.DisplayName} in inventory";

        energy = _energy.Restore(food.EnergyRestore);
        if (_health != null)
            health = _health.Heal(food.HealthRestore);

        GameEventBus.Publish(GameEventType.FoodEaten, MetaProgress.GetItemKey(food));
        return null;
    }

    /// <summary>
    /// Первая ячейка с едой, подходящей по названию; -1, если нет. foundNonFood — по названию нашёлся
    /// предмет, но не еда (для понятной ошибки).
    /// </summary>
    private int FindFoodSlot(string itemName, out bool foundNonFood)
    {
        foundNonFood = false;
        for (int i = 0; i < _inventory.SlotCount; i++)
        {
            InventorySlot slot = _inventory.GetSlot(i);
            if (slot.IsEmpty || !slot.Item.MatchesName(itemName)) continue;

            if (slot.Item is FoodItem) return i;
            foundNonFood = true;
        }

        return -1;
    }
}
