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

    // ─── Достижения ───────────────────────────────────────────

    private const string AchievementCatalogPath = ScriptableObjects + "/Achievements/AchievementCatalog.asset";

    // Награды-рецепты будущих этапов: рецептов в книге ещё нет, открытие откладывается (GameDesign §6.1)
    private static readonly string[] DeferredRecipeRewards = { "sword", "campfire", "cooked_meat" };

    private const string UnlockConfigPath = ScriptableObjects + "/DefaultUnlockConfig.asset";

    // Достижения без наград: раньше давали события на карте, теперь только идут в счёт порогов событий
    private static readonly string[] AchievementsWithoutRewards = { "hub_builder", "code_solver" };

    private static AchievementCatalog Catalog() => TestHelpers.Load<AchievementCatalog>(AchievementCatalogPath);

    [Test]
    public void AchievementCatalog_Has18UniqueNonNullAchievements()
    {
        AchievementCatalog catalog = Catalog();

        // 17 из §6.1 + first_bug (прогрессия v2, §6.2)
        Assert.AreEqual(18, catalog.Achievements.Count);
        var ids = new HashSet<string>();
        for (int i = 0; i < catalog.Achievements.Count; i++)
        {
            AchievementDefinition definition = catalog.Achievements[i];
            Assert.IsNotNull(definition, $"element #{i + 1} is empty");
            Assert.IsTrue(ids.Add(definition.Id), $"duplicate achievement id '{definition.Id}'");
            Assert.IsNotNull(definition.Condition, $"{definition.Id}: no condition");
            if (AchievementsWithoutRewards.Contains(definition.Id))
                Assert.IsEmpty(definition.Rewards,
                    $"{definition.Id}: map events come by the achievement count, not as rewards");
            else
                Assert.IsNotEmpty(definition.Rewards, $"{definition.Id}: no rewards");
        }
    }

    [Test]
    public void AchievementCatalog_NoMapEventRewards()
    {
        // События на карте открываются числом выполненных достижений (DefaultUnlockConfig.mapEventThresholds)
        foreach (AchievementDefinition definition in Catalog().Achievements)
            foreach (AchievementReward reward in definition.Rewards)
                Assert.AreNotEqual(AchievementRewardKind.MapEvent, reward.kind,
                    $"{definition.Id}: map event '{reward.Id}' as a reward");
    }

    [Test]
    public void UnlockConfig_MapEventThresholds_CoverAllPuzzleTypesWithinCatalog()
    {
        UnlockConfig config = TestHelpers.Load<UnlockConfig>(UnlockConfigPath);
        int achievements = Catalog().Achievements.Count;

        var expected = new Dictionary<string, int>
        {
            { PuzzleTypes.CodeTask, 6 }, { PuzzleTypes.Maze, 11 }, { PuzzleTypes.Plates, 15 }
        };
        Assert.AreEqual(expected.Count, config.mapEventThresholds.Count, "one row per map event");

        var ids = new HashSet<string>();
        foreach (MapEventThreshold threshold in config.mapEventThresholds)
        {
            Assert.IsNotNull(threshold, "empty row");
            Assert.IsTrue(ids.Add(threshold.EventId), $"duplicate map event '{threshold.EventId}'");
            CollectionAssert.Contains(PuzzleTypes.All, threshold.EventId, $"unknown map event '{threshold.EventId}'");
            Assert.AreEqual(expected[threshold.EventId], threshold.achievementsRequired, threshold.EventId);
            Assert.LessOrEqual(threshold.achievementsRequired, achievements,
                $"{threshold.EventId}: needs more achievements than the catalog has");
        }

        CollectionAssert.AreEquivalent(PuzzleTypes.All, ids);
    }

    [Test]
    public void UnlockConfig_EnterPortal_IsStartingCommand()
    {
        // Кнопки Exit Run нет: без enterPortal() из забега не выйти
        UnlockConfig config = TestHelpers.Load<UnlockConfig>(UnlockConfigPath);
        CollectionAssert.Contains(config.startingCommands, "enterPortal",
            "enterPortal must be in DefaultUnlockConfig.startingCommands — it is the only way to leave a run");
    }

    [Test]
    public void UnlockConfig_PuzzleCommands_ComeWithCodeTaskOnly()
    {
        UnlockConfig config = TestHelpers.Load<UnlockConfig>(UnlockConfigPath);
        string[] puzzleCommands = { "puzzle", "solve" };

        MapEventThreshold codeTask = config.mapEventThresholds.First(t => t.EventId == PuzzleTypes.CodeTask);
        CollectionAssert.AreEquivalent(puzzleCommands, codeTask.commands);
        foreach (MapEventThreshold other in config.mapEventThresholds.Where(t => t.EventId != PuzzleTypes.CodeTask))
            CollectionAssert.IsEmpty(other.commands, $"{other.EventId}: no commands expected");

        foreach (string command in puzzleCommands)
        {
            CollectionAssert.DoesNotContain(config.startingCommands, command, $"'{command}' must not be a starting command");
            Assert.IsFalse(Catalog().Achievements.SelectMany(a => a.Rewards)
                    .Any(r => r.kind == AchievementRewardKind.Command && r.Id == command),
                $"'{command}' comes with the code_task map event, not as an achievement reward");
        }
    }

    [Test]
    public void AchievementCatalog_Validate_NoProblems()
    {
        CollectionAssert.IsEmpty(Catalog().Validate());
    }

    [Test]
    public void AchievementCatalog_MatchesDesignIds()
    {
        string[] expected =
        {
            "first_harvest", "first_axe", "workbench_built", "copy_paste", "explorer", "lumberjack",
            "sweet_dreams", "efficient_logger", "loop_lover", "function_fan", "threat_survivor",
            "enemy_spotted", "first_blood", "first_meal", "hub_builder", "code_solver", "maze_runner",
            "first_bug"
        };
        CollectionAssert.AreEquivalent(expected, Catalog().Achievements.Select(a => a.Id));
    }

    [Test]
    public void AchievementCatalog_ConstructRewards_AreKnownConstructs()
    {
        foreach (AchievementDefinition definition in Catalog().Achievements)
            foreach (AchievementReward reward in definition.Rewards)
            {
                Assert.IsFalse(string.IsNullOrEmpty(reward.Id), $"{definition.Id}: empty reward id");
                if (reward.kind == AchievementRewardKind.Construct)
                    Assert.IsTrue(LuaConstructs.IsKnown(reward.Id), $"{definition.Id}: unknown construct '{reward.Id}'");
            }
    }

    [Test]
    public void AchievementCatalog_RecipeRewards_ExistAndAreLockedOrDeferred()
    {
        RecipeBook book = TestHelpers.Load<RecipeBook>(RecipeBookPath);

        foreach (AchievementDefinition definition in Catalog().Achievements)
            foreach (AchievementReward reward in definition.Rewards)
            {
                if (reward.kind != AchievementRewardKind.Recipe) continue;

                CraftRecipe recipe = book.Recipes.FirstOrDefault(r => r.Key == reward.Id);
                if (recipe == null)
                {
                    CollectionAssert.Contains(DeferredRecipeRewards, reward.Id,
                        $"{definition.Id}: recipe '{reward.Id}' is not in the RecipeBook (typo?)");
                    continue;
                }

                Assert.IsTrue(recipe.locked, $"{definition.Id}: rewarded recipe '{reward.Id}' must be locked, " +
                                             "otherwise the reward unlocks nothing");
            }
    }

    [Test]
    public void RecipeBook_LockedRecipes_AreRewardedByAnAchievement()
    {
        RecipeBook book = TestHelpers.Load<RecipeBook>(RecipeBookPath);
        var rewarded = new HashSet<string>(Catalog().Achievements
            .SelectMany(a => a.Rewards)
            .Where(r => r.kind == AchievementRewardKind.Recipe)
            .Select(r => r.Id));

        foreach (CraftRecipe recipe in book.Recipes.Where(r => r.locked))
            Assert.IsTrue(rewarded.Contains(recipe.Key), $"locked recipe '{recipe.Key}' can never be unlocked");
    }

    [Test]
    public void AchievementCatalog_CraftedFilters_AreRecipeResults()
    {
        RecipeBook book = TestHelpers.Load<RecipeBook>(RecipeBookPath);

        foreach (AchievementDefinition definition in Catalog().Achievements)
        {
            if (definition.Condition.gameEvent != GameEventType.Crafted) continue;
            string filter = definition.Condition.filter?.Trim();
            Assert.IsFalse(string.IsNullOrEmpty(filter), $"{definition.Id}: Crafted without filter");
            Assert.IsTrue(book.Recipes.Any(r => r.Key == filter),
                $"{definition.Id}: no recipe produces item '{filter}'");
        }
    }

    [Test]
    public void AchievementCatalog_BuiltFilters_AreStructureItemIds()
    {
        var structureKeys = new HashSet<string>(TestHelpers.LoadAll<StructureItem>(ScriptableObjects)
            .Select(MetaProgress.GetItemKey));

        foreach (AchievementDefinition definition in Catalog().Achievements)
        {
            if (definition.Condition.gameEvent != GameEventType.Built) continue;
            string filter = definition.Condition.filter?.Trim();
            Assert.IsFalse(string.IsNullOrEmpty(filter), $"{definition.Id}: Built without filter");
            Assert.IsTrue(structureKeys.Contains(filter), $"{definition.Id}: no StructureItem with itemId '{filter}'");
        }
    }

    [Test]
    public void AchievementCatalog_ItemFilters_AreExistingItemIds()
    {
        // Harvested / ItemObtained с фильтром (кроме типа объекта в Tag) — ключ существующего предмета
        var itemKeys = new HashSet<string>(TestHelpers.LoadAll<ResourceItem>(ScriptableObjects)
            .Select(MetaProgress.GetItemKey));

        foreach (AchievementDefinition definition in Catalog().Achievements)
        {
            GameEventType type = definition.Condition.gameEvent;
            string filter = definition.Condition.filter?.Trim();
            if (type != GameEventType.ItemObtained && type != GameEventType.Harvested) continue;
            if (string.IsNullOrEmpty(filter)) continue;
            Assert.IsTrue(itemKeys.Contains(filter), $"{definition.Id}: no item with itemId '{filter}'");
        }
    }
}
