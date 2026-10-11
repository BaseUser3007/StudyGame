using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Проверка ассетов проекта: рецепты как договорились, стоимость добычи, иконки,
/// связь предметов с объектами на земле, постройки и биомы.
/// </summary>
public class DataIntegrityTests
{
    private const string ScriptableObjects = "Assets/_Game/ScriptableObjects";
    private const string RecipeBookPath = ScriptableObjects + "/Recipes/RecipeBook.asset";

    // ─── Рецепты ──────────────────────────────────────────────

    [TestCase("flint", CraftingStation.None, "stone:2")]
    [TestCase("axe", CraftingStation.None, "stick:2", "flint:1")]
    [TestCase("workbench", CraftingStation.None, "stick:4", "log:4")]
    [TestCase("pickaxe", CraftingStation.Workbench, "flint:2", "stick:2")]
    [TestCase("bed", CraftingStation.Workbench, "log:3", "stick:2")]
    public void Recipe_MatchesDesign(string result, CraftingStation station, params string[] ingredients)
    {
        RecipeBook book = TestHelpers.Load<RecipeBook>(RecipeBookPath);
        CraftRecipe recipe = book.Find(result);

        Assert.IsNotNull(recipe, $"no recipe for {result}");
        Assert.AreEqual(station, recipe.station);

        var actual = recipe.GetRequirements().Select(r => $"{r.item.itemId}:{r.amount}").OrderBy(s => s);
        CollectionAssert.AreEqual(ingredients.OrderBy(s => s), actual);
    }

    [Test]
    public void AllRecipeItems_HaveIcons()
    {
        RecipeBook book = TestHelpers.Load<RecipeBook>(RecipeBookPath);

        foreach (CraftRecipe recipe in book.Recipes)
        {
            Assert.IsNotNull(recipe.result.Icon, $"{recipe.DisplayName}: no icon");
            foreach (RecipeIngredient ingredient in recipe.GetRequirements())
                Assert.IsNotNull(ingredient.item.Icon, $"{ingredient.item.name}: no icon");
        }
    }

    // ─── Добыча ───────────────────────────────────────────────

    [TestCase("Tree_Common", 12f, "log:2", "stick:2")]
    [TestCase("Boulder_Common", 13f, "stone:3", "flint:1")]
    public void MiningLoot_AndCost(string variantName, float cost, params string[] loot)
    {
        var variant = TestHelpers.Load<ResourceVariant>($"{ScriptableObjects}/Variants/{variantName}.asset");

        Assert.AreEqual(cost, variant.HarvestEnergyCost, 0.001f);
        var actual = variant.MiningLoot.Select(e => $"{e.item.itemId}:{e.amount}").OrderBy(s => s);
        CollectionAssert.AreEqual(loot.OrderBy(s => s), actual);
    }

    [Test]
    public void WorldPrefabs_DropTheSameItem_AndArePickedUpWithoutTools()
    {
        foreach (ResourceItem item in TestHelpers.LoadAll<ResourceItem>(ScriptableObjects))
        {
            if (item.worldPrefab == null) continue;

            Assert.IsFalse(ResourceHarvester.IsMinable(item.worldPrefab),
                $"{item.name}: dropped item must be picked up with harvest, not mined");
            Assert.IsNotNull(item.worldPrefab.Variant, $"{item.name}: world prefab has no variant");
            Assert.AreSame(item, item.worldPrefab.Variant.drop,
                $"{item.name}: picking up its world prefab gives {item.worldPrefab.Variant.drop}");
        }
    }

    [TestCase("log")]
    [TestCase("flint")]
    [TestCase("stone")]
    [TestCase("stick")]
    public void LootItems_CanBeDroppedOnTheGround(string itemId)
    {
        ResourceItem item = TestHelpers.LoadAll<ResourceItem>(ScriptableObjects).FirstOrDefault(i => i.itemId == itemId);
        Assert.IsNotNull(item, $"item {itemId} not found");
        Assert.IsNotNull(item.worldPrefab, $"{itemId}: mined loot needs a world prefab to lie on the ground");
    }

    // ─── Постройки ────────────────────────────────────────────

    [Test]
    public void Workbench_IsAStation()
    {
        var item = TestHelpers.Load<StructureItem>($"{ScriptableObjects}/Items/Workbench.asset");

        Assert.IsNotNull(item.prefab);
        Assert.AreEqual(CraftingStation.Workbench, item.prefab.Station);
        Assert.IsNotNull(item.Icon, "workbench icon comes from its prefab");
        Assert.Greater(ObjectFootprint.Get(item.prefab).width, 0f, "workbench needs a collider footprint");
    }

    [Test]
    public void Bed_IsASleepingPlace()
    {
        var item = TestHelpers.Load<StructureItem>($"{ScriptableObjects}/Items/Bed.asset");

        Assert.IsNotNull(item.prefab);
        Assert.IsTrue(item.prefab.IsSleepingPlace);
        Assert.AreEqual(CraftingStation.None, item.prefab.Station);
        Assert.IsNotNull(item.Icon);
    }

    // ─── Биомы ────────────────────────────────────────────────

    [Test]
    public void Biomes_HaveValidResources()
    {
        var config = TestHelpers.Load<BiomeConfig>($"{ScriptableObjects}/DefaultBiomeConfig.asset");
        Assert.IsNotEmpty(config.biomes);

        foreach (Biome biome in config.biomes)
        {
            Assert.Greater(biome.weight, 0f, $"{biome.name}: weight 0 hides the biome");
            foreach (BiomeResource resource in biome.resources)
            {
                Assert.IsNotNull(resource.prefab, $"{biome.name}: empty resource row");
                Assert.Greater(resource.density, 0f, $"{biome.name}/{resource.prefab.name}: density 0");
                Assert.IsNotNull(resource.prefab.Variant, $"{resource.prefab.name}: no variant");
            }
        }
    }
}
