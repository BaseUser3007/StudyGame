using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// Рецепты: итоговые требования (пустые строки, повторы) и условие станции.
/// </summary>
public class CraftingTests
{
    private MaterialResource _stone;
    private MaterialResource _stick;
    private CraftRecipe _recipe;

    [SetUp]
    public void SetUp()
    {
        _stone = TestHelpers.Material("stone");
        _stick = TestHelpers.Material("stick");
        _recipe = new CraftRecipe
        {
            result = TestHelpers.Material("thing"),
            ingredients = new List<RecipeIngredient>
            {
                new RecipeIngredient { item = _stone, amount = 2 },
                new RecipeIngredient { item = null, amount = 5 },
                new RecipeIngredient { item = _stone, amount = 1 },
                new RecipeIngredient { item = _stick, amount = 1 }
            }
        };
    }

    [Test]
    public void GetRequirements_SkipsEmptyRows_AndMergesDuplicates()
    {
        List<RecipeIngredient> requirements = _recipe.GetRequirements();

        Assert.AreEqual(2, requirements.Count);
        Assert.AreSame(_stone, requirements[0].item);
        Assert.AreEqual(3, requirements[0].amount);
        Assert.AreSame(_stick, requirements[1].item);
    }

    [Test]
    public void CanCraft_ChecksIngredients()
    {
        var counts = new Dictionary<ResourceItem, int> { { _stone, 3 }, { _stick, 1 } };
        Assert.IsTrue(_recipe.CanCraft(item => counts.TryGetValue(item, out int c) ? c : 0, null));

        counts[_stone] = 2;
        Assert.IsFalse(_recipe.CanCraft(item => counts.TryGetValue(item, out int c) ? c : 0, null));
    }

    [Test]
    public void CanCraft_WithStation_NeedsStationNearby()
    {
        _recipe.station = CraftingStation.Workbench;
        int Enough(ResourceItem item) => 10;

        Assert.IsFalse(_recipe.CanCraft(Enough, null));
        Assert.IsFalse(_recipe.CanCraft(Enough, station => false));
        Assert.IsTrue(_recipe.CanCraft(Enough, station => station == CraftingStation.Workbench));
    }

    [Test]
    public void Matches_ByDisplayNameOrItemId_CaseInsensitive()
    {
        Assert.IsTrue(_recipe.Matches("THING"));
        Assert.IsTrue(_recipe.Matches("  thing "));
        Assert.IsFalse(_recipe.Matches("stone"));
    }
}
