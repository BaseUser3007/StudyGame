using System.Collections.Generic;

/// <summary>
/// Содержимое одной ячейки инвентаря (снимок для UI).
/// </summary>
public readonly struct InventorySlot
{
    public readonly ResourceItem Item;
    public readonly int Count;

    public bool IsEmpty => Item == null || Count <= 0;

    public InventorySlot(ResourceItem item, int count)
    {
        Item = item;
        Count = count;
    }
}

/// <summary>
/// Инвентарь игрока: фиксированное число ячеек, в каждой — стопка одного ресурса
/// размером до <see cref="ResourceItem.maxStackSize"/>.
/// </summary>
public class PlayerInventory
{
    private readonly ResourceItem[] _items;
    private readonly int[] _counts;

    public int SlotCount => _items.Length;

    public PlayerInventory(int slotCount)
    {
        _items = new ResourceItem[slotCount];
        _counts = new int[slotCount];
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
    public int Add(ResourceItem item, int amount)
    {
        int added = AddInternal(item, amount);
        if (added > 0) Publish();
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

        foreach (var pair in required)
            RemoveInternal(pair.Key, pair.Value);

        if (AddInternal(add, addAmount) < addAmount)
        {
            itemsBackup.CopyTo(_items, 0);
            countsBackup.CopyTo(_counts, 0);
            return false;
        }

        Publish();
        return true;
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
        return new InventorySlot(_items[index], _counts[index]);
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
            if (_counts[i] == 0) _items[i] = null;
        }
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

    private static int GetStackSize(ResourceItem item)
    {
        return System.Math.Max(1, item.maxStackSize);
    }
}
