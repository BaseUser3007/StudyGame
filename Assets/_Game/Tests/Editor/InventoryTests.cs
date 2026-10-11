using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// Инвентарь: стопки, инструменты с прочностью, атомарный обмен (крафт).
/// </summary>
public class InventoryTests
{
    private PlayerInventory _inventory;
    private MaterialResource _stone;
    private MaterialResource _stick;

    [SetUp]
    public void SetUp()
    {
        _inventory = new PlayerInventory(10);
        _stone = TestHelpers.Material("stone", 20);
        _stick = TestHelpers.Material("stick", 20);
    }

    [Test]
    public void Add_FillsStacksThenFreeSlots()
    {
        Assert.AreEqual(25, _inventory.Add(_stone, 25));

        Assert.AreEqual(20, _inventory.GetSlot(0).Count);
        Assert.AreEqual(5, _inventory.GetSlot(1).Count);
        Assert.AreEqual(25, _inventory.GetCount(_stone));
    }

    [Test]
    public void Add_WhenFull_AddsOnlyWhatFits()
    {
        for (int i = 0; i < 10; i++)
            _inventory.Add(TestHelpers.Material("item" + i, 1), 1);

        Assert.IsFalse(_inventory.CanAdd(_stone, 1));
        Assert.AreEqual(0, _inventory.Add(_stone, 1));
    }

    [Test]
    public void Tools_DoNotStack_AndStartWithFullDurability()
    {
        ToolItem axe = TestHelpers.Tool("axe", ToolType.Axe, maxDurability: 3);
        _inventory.Add(axe, 2);

        Assert.AreEqual(1, _inventory.GetSlot(0).Count);
        Assert.AreEqual(1, _inventory.GetSlot(1).Count);
        Assert.AreEqual(3, _inventory.GetSlot(0).Durability);
        Assert.AreEqual(3, _inventory.GetSlot(0).MaxDurability);
    }

    [Test]
    public void WearTool_BreaksAtZero()
    {
        ToolItem axe = TestHelpers.Tool("axe", ToolType.Axe, maxDurability: 2);
        _inventory.Add(axe, 1);
        int slot = _inventory.FindToolSlot(ToolType.Axe);

        Assert.IsFalse(_inventory.WearTool(slot, 1));
        Assert.AreEqual(1, _inventory.GetSlot(slot).Durability);

        Assert.IsTrue(_inventory.WearTool(slot, 1), "tool breaks at zero durability");
        Assert.IsTrue(_inventory.GetSlot(slot).IsEmpty);
        Assert.AreEqual(-1, _inventory.FindToolSlot(ToolType.Axe));
    }

    [Test]
    public void FindToolSlot_PicksMostWorn_AndIgnoresOtherTypes()
    {
        _inventory.Add(TestHelpers.Tool("pickaxe", ToolType.Pickaxe), 1);
        ToolItem axe = TestHelpers.Tool("axe", ToolType.Axe, maxDurability: 5);
        _inventory.Add(axe, 2);
        _inventory.WearTool(2, 3);

        Assert.AreEqual(2, _inventory.FindToolSlot(ToolType.Axe));
    }

    [Test]
    public void TryExchange_SwapsIngredientsForResult()
    {
        MaterialResource flint = TestHelpers.Material("flint");
        _inventory.Add(_stone, 3);

        bool ok = _inventory.TryExchange(Ingredients((_stone, 2)), flint, 1);

        Assert.IsTrue(ok);
        Assert.AreEqual(1, _inventory.GetCount(_stone));
        Assert.AreEqual(1, _inventory.GetCount(flint));
    }

    [Test]
    public void TryExchange_MergesDuplicateIngredients()
    {
        // Stone ×2 и Stone ×2 = нужно 4, а есть 3 — нельзя
        _inventory.Add(_stone, 3);

        bool ok = _inventory.TryExchange(Ingredients((_stone, 2), (_stone, 2)), _stick, 1);

        Assert.IsFalse(ok);
        Assert.AreEqual(3, _inventory.GetCount(_stone));
    }

    [Test]
    public void TryExchange_WhenResultDoesNotFit_LeavesInventoryUnchanged()
    {
        // Все 10 ячеек заняты разными предметами; ингредиент берётся частично — свободной ячейки не появится
        _inventory.Add(_stone, 5);
        for (int i = 1; i < 10; i++)
            _inventory.Add(TestHelpers.Material("item" + i), 1);

        bool ok = _inventory.TryExchange(Ingredients((_stone, 1)), TestHelpers.Material("result"), 1);

        Assert.IsFalse(ok);
        Assert.AreEqual(5, _inventory.GetCount(_stone), "ingredients restored after rollback");
    }

    [Test]
    public void RemoveFromSlot_AndRemoveByItem()
    {
        _inventory.Add(_stone, 25);

        Assert.AreEqual(20, _inventory.RemoveFromSlot(0, 100));
        Assert.AreEqual(3, _inventory.Remove(_stone, 3));
        Assert.AreEqual(2, _inventory.GetCount(_stone));
        Assert.AreSame(_stone, _inventory.FindItem("STONE"), "name lookup is case-insensitive");
    }

    private static List<RecipeIngredient> Ingredients(params (ResourceItem item, int amount)[] list)
    {
        var result = new List<RecipeIngredient>();
        foreach (var (item, amount) in list)
            result.Add(new RecipeIngredient { item = item, amount = amount });
        return result;
    }
}
