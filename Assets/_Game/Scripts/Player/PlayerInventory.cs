using System.Collections.Generic;

/// <summary>
/// Содержимое одной ячейки инвентаря (снимок для UI и Lua).
/// </summary>
public readonly struct InventorySlot
{
    public readonly ResourceItem Item;
    public readonly int Count;
    /// <summary>Текущая прочность инструмента (0, если у предмета нет прочности).</summary>
    public readonly int Durability;

    public bool IsEmpty => Item == null || Count <= 0;
    public ToolItem Tool => Item as ToolItem;
    public bool HasDurability => Tool != null;
    public int MaxDurability => Tool != null ? Tool.MaxDurability : 0;

    public InventorySlot(ResourceItem item, int count, int durability)
    {
        Item = item;
        Count = count;
        Durability = durability;
    }
}

/// <summary>
/// Инвентарь игрока: фиксированное число ячеек, в каждой — стопка одного ресурса
/// размером до <see cref="ResourceItem.maxStackSize"/>. Инструменты не складываются:
/// у каждого своя ячейка и своя прочность.
/// </summary>
public class PlayerInventory
{
    private readonly ResourceItem[] _items;
    private readonly int[] _counts;
    private readonly int[] _durability;

    public int SlotCount => _items.Length;

    public PlayerInventory(int slotCount)
    {
        _items = new ResourceItem[slotCount];
        _counts = new int[slotCount];
        _durability = new int[slotCount];
    }

    // ─── Public API ───────────────────────────────────────────

    /// <summary>
    /// Поместится ли весь указанный объём ресурса.
    /// </summary>
    public bool CanAdd(ResourceItem item, int amount)
    {
        return item != null && GetFreeSpace(item) >= amount;
    }

    /// <summary>
    /// Кладёт ресурс: сначала дополняет уже начатые стопки, затем занимает пустые ячейки.
    /// Возвращает, сколько удалось положить.
    /// </summary>
    /// <param name="recycled">Не новая добыча (стартовые предметы, подбор выброшенного) —
    /// ItemObtained помечается, и достижения его не считают.</param>
    /// <param name="fromThisRun">Получено благодаря текущему запуску кода (см. GameEvent.FromThisRun).</param>
    public int Add(ResourceItem item, int amount, bool recycled = false, bool fromThisRun = true)
    {
        int added = AddInternal(item, amount);
        if (added > 0)
        {
            Publish();
            GameEventBus.Publish(new GameEvent(
                GameEventType.ItemObtained, MetaProgress.GetItemKey(item), added, null, recycled, fromThisRun));
        }
        return added;
    }

    /// <summary>
    /// Атомарно забирает ресурсы и кладёт результат (крафт).
    /// Если чего-то не хватает или результат не помещается — инвентарь не меняется.
    /// </summary>
    public bool TryExchange(IReadOnlyList<RecipeIngredient> remove, ResourceItem add, int addAmount)
    {
        // Одинаковые ресурсы складываем, иначе каждая строка проверялась бы отдельно
        var required = new Dictionary<ResourceItem, int>();
        foreach (RecipeIngredient ingredient in remove)
        {
            if (ingredient.item == null || ingredient.amount <= 0) continue;
            required.TryGetValue(ingredient.item, out int amount);
            required[ingredient.item] = amount + ingredient.amount;
        }

        foreach (var pair in required)
            if (GetCount(pair.Key) < pair.Value)
                return false;

        ResourceItem[] itemsBackup = (ResourceItem[])_items.Clone();
        int[] countsBackup = (int[])_counts.Clone();
        int[] durabilityBackup = (int[])_durability.Clone();

        foreach (var pair in required)
            RemoveInternal(pair.Key, pair.Value);

        if (AddInternal(add, addAmount) < addAmount)
        {
            itemsBackup.CopyTo(_items, 0);
            countsBackup.CopyTo(_counts, 0);
            durabilityBackup.CopyTo(_durability, 0);
            return false;
        }

        Publish();
        if (add != null && addAmount > 0)
            GameEventBus.Publish(GameEventType.ItemObtained, MetaProgress.GetItemKey(add), addAmount);
        return true;
    }

    /// <summary>
    /// Забирает до amount штук из ячейки (индекс с нуля). Возвращает, сколько забрано.
    /// </summary>
    public int RemoveFromSlot(int index, int amount)
    {
        if (index < 0 || index >= _items.Length || _items[index] == null || amount <= 0) return 0;

        int taken = System.Math.Min(_counts[index], amount);
        _counts[index] -= taken;
        if (_counts[index] == 0) ClearSlot(index);

        Publish();
        return taken;
    }

    /// <summary>
    /// Забирает до amount штук ресурса из всех ячеек. Возвращает, сколько забрано.
    /// </summary>
    public int Remove(ResourceItem item, int amount)
    {
        int taken = System.Math.Min(GetCount(item), amount);
        if (taken <= 0) return 0;

        RemoveInternal(item, taken);
        Publish();
        return taken;
    }

    /// <summary>
    /// Первый ресурс в инвентаре, подходящий по названию (см. ResourceItem.MatchesName), или null.
    /// </summary>
    public ResourceItem FindItem(string itemName)
    {
        foreach (ResourceItem item in _items)
            if (item != null && item.MatchesName(itemName))
                return item;
        return null;
    }

    /// <summary>
    /// Ячейка с исправным инструментом нужного типа (самым изношенным — сначала доламываем старый),
    /// или -1, если такого нет.
    /// </summary>
    public int FindToolSlot(ToolType type)
    {
        int best = -1;
        for (int i = 0; i < _items.Length; i++)
        {
            if (!(_items[i] is ToolItem tool) || tool.toolType != type || _durability[i] <= 0) continue;
            if (best < 0 || _durability[i] < _durability[best]) best = i;
        }
        return best;
    }

    /// <summary>
    /// Ячейка с лучшим исправным оружием: наибольший урон, при равном — самое изношенное.
    /// -1, если оружия нет (бой — кулаками, см. CombatService).
    /// </summary>
    public int FindWeaponSlot()
    {
        int best = -1;
        for (int i = 0; i < _items.Length; i++)
        {
            if (!(_items[i] is WeaponItem weapon) || _durability[i] <= 0) continue;
            if (best < 0) { best = i; continue; }

            int bestDamage = ((WeaponItem)_items[best]).Damage;
            if (weapon.Damage > bestDamage || (weapon.Damage == bestDamage && _durability[i] < _durability[best]))
                best = i;
        }
        return best;
    }

    /// <summary>
    /// Износ инструмента в ячейке. Прочность кончилась — инструмент ломается и исчезает.
    /// Возвращает true, если инструмент сломался.
    /// </summary>
    public bool WearTool(int index, int amount)
    {
        if (index < 0 || index >= _items.Length || !(_items[index] is ToolItem)) return false;

        _durability[index] -= System.Math.Max(0, amount);
        bool broken = _durability[index] <= 0;
        if (broken) ClearSlot(index);

        Publish();
        return broken;
    }

    public int GetCount(ResourceItem item)
    {
        if (item == null) return 0;

        int total = 0;
        for (int i = 0; i < _items.Length; i++)
            if (_items[i] == item)
                total += _counts[i];
        return total;
    }

    public InventorySlot GetSlot(int index)
    {
        return new InventorySlot(_items[index], _counts[index], _durability[index]);
    }

    public void Publish()
    {
        List<InventorySlot> view = new List<InventorySlot>(_items.Length);
        for (int i = 0; i < _items.Length; i++)
            view.Add(GetSlot(i));

        UIEventBus.PublishInventoryChanged(view);
    }

    // ─── Helpers ──────────────────────────────────────────────

    private int AddInternal(ResourceItem item, int amount)
    {
        if (item == null || amount <= 0) return 0;

        int left = amount;
        int stackSize = GetStackSize(item);

        for (int i = 0; i < _items.Length && left > 0; i++)
        {
            if (_items[i] != item) continue;
            int put = System.Math.Min(stackSize - _counts[i], left);
            if (put <= 0) continue;
            _counts[i] += put;
            left -= put;
        }

        for (int i = 0; i < _items.Length && left > 0; i++)
        {
            if (_items[i] != null) continue;
            int put = System.Math.Min(stackSize, left);
            _items[i] = item;
            _counts[i] = put;
            // Новый инструмент — с полной прочностью
            _durability[i] = item is ToolItem tool ? tool.MaxDurability : 0;
            left -= put;
        }

        return amount - left;
    }

    /// <summary>
    /// Забирает ресурс начиная с последних ячеек; опустевшие ячейки освобождаются.
    /// </summary>
    private void RemoveInternal(ResourceItem item, int amount)
    {
        for (int i = _items.Length - 1; i >= 0 && amount > 0; i--)
        {
            if (_items[i] != item) continue;
            int take = System.Math.Min(_counts[i], amount);
            _counts[i] -= take;
            amount -= take;
            if (_counts[i] == 0) ClearSlot(i);
        }
    }

    private void ClearSlot(int index)
    {
        _items[index] = null;
        _counts[index] = 0;
        _durability[index] = 0;
    }

    private int GetFreeSpace(ResourceItem item)
    {
        int stackSize = GetStackSize(item);
        int free = 0;

        for (int i = 0; i < _items.Length; i++)
        {
            if (_items[i] == null) free += stackSize;
            else if (_items[i] == item) free += stackSize - _counts[i];
        }

        return free;
    }

    /// <summary>
    /// Инструменты не складываются — у каждого своя прочность.
    /// </summary>
    private static int GetStackSize(ResourceItem item)
    {
        return item is ToolItem ? 1 : System.Math.Max(1, item.maxStackSize);
    }
}
