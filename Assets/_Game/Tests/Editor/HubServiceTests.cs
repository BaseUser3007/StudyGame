using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Хаб (этап 4): стоимость и списание со склада, границы и пересечения на сетке, открытие через
/// UnlockRegistry, однократность, сохранение/загрузка расстановки, бонусы построек в RunModifiers,
/// событие HubBuildingBuilt. Числа — GameDesign.md §10.3. MetaProgress пишет во временную папку.
/// </summary>
public class HubServiceTests
{
    private TestHelpers.TempSaveFolder _saves;
    private readonly List<Object> _created = new List<Object>();
    private readonly List<GameEvent> _events = new List<GameEvent>();

    private MaterialResource _log;
    private MaterialResource _stick;
    private MaterialResource _stone;
    private MaterialResource _crystal;
    private MaterialResource _gold;

    private HubBuildingDefinition _storage;
    private HubBuildingDefinition _shrine;
    private HubBuildingDefinition _watchtower;
    private HubBuildingDefinition _healerHut;
    private HubBuildingCatalog _catalog;

    [SetUp]
    public void SetUp()
    {
        ResetBus();
        GameEventBus.OnGameEvent += HandleGameEvent;
        _saves = new TestHelpers.TempSaveFolder();
        _events.Clear();

        _log = Track(TestHelpers.Material("log"));
        _stick = Track(TestHelpers.Material("stick"));
        _stone = Track(TestHelpers.Material("stone"));
        _crystal = Track(TestHelpers.Material("crystal"));
        _gold = Track(TestHelpers.Material("gold"));

        _storage = Define("storage", new Vector2Int(2, 2),
            new[] { Stack(_log, 6), Stack(_stick, 4) }, Effect(HubEffectType.InventorySlots, 2));
        _shrine = Define("shrine", new Vector2Int(2, 2),
            new[] { Stack(_log, 4), Stack(_stone, 6) }, Effect(HubEffectType.EnergyLimit, 10));
        _watchtower = Define("watchtower", new Vector2Int(1, 2),
            new[] { Stack(_log, 4), Stack(_stone, 4), Stack(_crystal, 2) }, Effect(HubEffectType.ThreatDays, 1));
        _healerHut = Define("healer_hut", new Vector2Int(2, 2),
            new[] { Stack(_log, 4), Stack(_stick, 4), Stack(_gold, 4) }, Effect(HubEffectType.MaxHealth, 25));
        _catalog = Catalog(_storage, _shrine, _watchtower, _healerHut);
    }

    [TearDown]
    public void TearDown()
    {
        GameEventBus.OnGameEvent -= HandleGameEvent;
        ResetBus();

        foreach (Object obj in _created)
            if (obj != null) Object.DestroyImmediate(obj);
        _created.Clear();
        _saves.Dispose();
    }

    // ─── Заготовки ────────────────────────────────────────────

    private void HandleGameEvent(GameEvent gameEvent) => _events.Add(gameEvent);

    private static void ResetBus()
    {
        MethodInfo reset = typeof(GameEventBus).GetMethod("ResetStatics", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(reset, "GameEventBus.ResetStatics not found");
        reset.Invoke(null, null);
    }

    private static void SetPrivate(object target, string field, object value)
    {
        FieldInfo info = target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(info, $"{target.GetType().Name}.{field} not found");
        info.SetValue(target, value);
    }

    private T Track<T>(T obj) where T : Object
    {
        _created.Add(obj);
        return obj;
    }

    private static ItemStack Stack(ResourceItem item, int amount) => new ItemStack { item = item, amount = amount };

    private static HubBuildingEffect Effect(HubEffectType type, float amount) =>
        new HubBuildingEffect { type = type, amount = amount };

    private HubBuildingDefinition Define(string id, Vector2Int size, ItemStack[] cost, params HubBuildingEffect[] effects)
    {
        HubBuildingDefinition definition = Track(ScriptableObject.CreateInstance<HubBuildingDefinition>());
        definition.name = id;
        SetPrivate(definition, "id", id);
        SetPrivate(definition, "title", id);
        SetPrivate(definition, "size", size);
        SetPrivate(definition, "cost", new List<ItemStack>(cost));
        SetPrivate(definition, "effects", new List<HubBuildingEffect>(effects));
        return definition;
    }

    private static HubBuildingLevel Level(ItemStack[] cost, params HubBuildingEffect[] effects) =>
        new HubBuildingLevel { cost = new List<ItemStack>(cost), effects = new List<HubBuildingEffect>(effects) };

    /// <summary>Добавляет постройке уровни 2, 3, … (T14).</summary>
    private static void WithLevels(HubBuildingDefinition definition, params HubBuildingLevel[] levels) =>
        SetPrivate(definition, "levels", new List<HubBuildingLevel>(levels));

    /// <summary>Склад: уровень 2 — 10 log + 2 crystal, +1 слот и +5 энергии; уровень 3 — 3 gold, +2 слота.</summary>
    private void StorageWithLevels() =>
        WithLevels(_storage,
            Level(new[] { Stack(_log, 10), Stack(_crystal, 2) },
                Effect(HubEffectType.InventorySlots, 1), Effect(HubEffectType.EnergyLimit, 5)),
            Level(new[] { Stack(_gold, 3) }, Effect(HubEffectType.InventorySlots, 2)));

    private HubBuildingCatalog Catalog(params HubBuildingDefinition[] buildings)
    {
        HubBuildingCatalog catalog = Track(ScriptableObject.CreateInstance<HubBuildingCatalog>());
        SetPrivate(catalog, "gridSize", new Vector2Int(8, 6));
        SetPrivate(catalog, "buildings", new List<HubBuildingDefinition>(buildings));
        return catalog;
    }

    private UnlockRegistry Registry(MetaProgress progress)
    {
        UnlockConfig config = Track(ScriptableObject.CreateInstance<UnlockConfig>());
        config.startingCommands = new List<string>();
        config.startingConstructs = new List<string>();
        config.unlockAll = false;

        var registry = new UnlockRegistry(config, progress);
        registry.SetKnownHubBuildings(_catalog.GetIds());
        return registry;
    }

    /// <summary>Хаб, в котором все постройки открыты, со складом.</summary>
    private HubService Hub(MetaProgress progress, params (ResourceItem item, int count)[] stock)
    {
        foreach ((ResourceItem item, int count) in stock)
            progress.AddMaterial(item, count);
        return new HubService(_catalog, progress, unlocks: null);
    }

    private PlayerConfig Config()
    {
        PlayerConfig config = Track(ScriptableObject.CreateInstance<PlayerConfig>());
        config.baseEnergyLimit = 100f;
        config.maxHealth = 100f;
        config.inventorySlots = 10;
        config.runsPerDay = 3;
        config.threatDay = 3;
        config.startingItems = new List<ItemStack>();
        return config;
    }

    // ─── Стоимость ────────────────────────────────────────────

    [Test]
    public void CanAfford_RequiresEveryCostLine()
    {
        HubService hub = Hub(_saves.Progress(), (_log, 6), (_stick, 3));
        Assert.IsFalse(hub.CanAfford(_storage), "3 sticks of 4");
        Assert.AreEqual(HubBuildResult.NotAffordable, hub.CheckBuild(_storage, Vector2Int.zero));

        hub.Progress.AddMaterial(_stick, 1);
        Assert.IsTrue(hub.CanAfford(_storage));
        Assert.AreEqual(HubBuildResult.Ok, hub.CheckBuild(_storage, Vector2Int.zero));
    }

    [Test]
    public void TryBuild_TakesExactCostFromStock()
    {
        HubService hub = Hub(_saves.Progress(), (_log, 10), (_stick, 4), (_stone, 1));

        Assert.AreEqual(HubBuildResult.Ok, hub.TryBuild(_storage, new Vector2Int(0, 0)));
        Assert.AreEqual(4, hub.GetStored("log"));
        Assert.AreEqual(0, hub.GetStored("stick"));
        Assert.AreEqual(1, hub.GetStored("stone"), "materials outside the cost are untouched");
        Assert.IsTrue(hub.IsBuilt(_storage));
    }

    [Test]
    public void TryBuild_NotAffordable_ChangesNothing()
    {
        HubService hub = Hub(_saves.Progress(), (_log, 4), (_stone, 5));

        Assert.AreEqual(HubBuildResult.NotAffordable, hub.TryBuild(_shrine, Vector2Int.zero));
        Assert.AreEqual(4, hub.GetStored("log"));
        Assert.AreEqual(5, hub.GetStored("stone"));
        Assert.IsFalse(hub.IsBuilt(_shrine));
        CollectionAssert.IsEmpty(_events);
    }

    [Test]
    public void GetCost_MergesDuplicatesAndSkipsEmptyRows()
    {
        HubBuildingDefinition odd = Define("odd", Vector2Int.one,
            new[] { Stack(_log, 2), Stack(null, 3), Stack(_log, 1), Stack(_stone, 0) });

        List<HubBuildingCost> cost = odd.GetCost();
        Assert.AreEqual(1, cost.Count);
        Assert.AreEqual("log", cost[0].Key);
        Assert.AreEqual(3, cost[0].Amount);
    }

    // ─── Сетка ────────────────────────────────────────────────

    [Test]
    public void Bounds_FootprintMustFitTheGrid()
    {
        HubService hub = Hub(_saves.Progress(), (_log, 100), (_stick, 100));

        Assert.AreEqual(new Vector2Int(8, 6), hub.GridSize);
        Assert.IsTrue(hub.IsInBounds(_storage, new Vector2Int(6, 4)), "top-right corner 2×2");
        Assert.IsFalse(hub.IsInBounds(_storage, new Vector2Int(7, 4)));
        Assert.IsFalse(hub.IsInBounds(_storage, new Vector2Int(6, 5)));
        Assert.IsFalse(hub.IsInBounds(_storage, new Vector2Int(-1, 0)));
        Assert.AreEqual(HubBuildResult.OutOfBounds, hub.TryBuild(_storage, new Vector2Int(7, 5)));
        Assert.AreEqual(100, hub.GetStored("log"), "refused build takes nothing");
    }

    [Test]
    public void Overlap_OccupiedCellsAreRefused()
    {
        HubService hub = Hub(_saves.Progress(), (_log, 100), (_stick, 100), (_stone, 100), (_crystal, 10));
        Assert.AreEqual(HubBuildResult.Ok, hub.TryBuild(_storage, new Vector2Int(0, 0)));

        // storage занимает (0..1, 0..1)
        Assert.AreEqual(HubBuildResult.Occupied, hub.CheckBuild(_shrine, new Vector2Int(1, 1)));
        Assert.AreEqual(HubBuildResult.Occupied, hub.CheckBuild(_watchtower, new Vector2Int(1, 0)));
        Assert.IsTrue(hub.CanPlaceAt(_shrine, new Vector2Int(2, 0)), "touching edges is fine");
        Assert.IsTrue(hub.CanPlaceAt(_watchtower, new Vector2Int(0, 2)));

        Assert.AreEqual("storage", hub.GetPlacementAt(new Vector2Int(1, 1)).Id);
        Assert.IsNull(hub.GetPlacementAt(new Vector2Int(2, 2)).Id);
    }

    [Test]
    public void EachBuildingOnlyOnce()
    {
        HubService hub = Hub(_saves.Progress(), (_log, 100), (_stick, 100));
        Assert.AreEqual(HubBuildResult.Ok, hub.TryBuild(_storage, new Vector2Int(0, 0)));
        Assert.AreEqual(HubBuildResult.AlreadyBuilt, hub.TryBuild(_storage, new Vector2Int(4, 0)));
        Assert.AreEqual(94, hub.GetStored("log"));
        Assert.AreEqual(1, hub.GetPlaced().Count);
    }

    // ─── Открытие ─────────────────────────────────────────────

    [Test]
    public void Locked_UntilUnlockedThroughRegistry()
    {
        MetaProgress progress = _saves.Progress();
        progress.AddMaterial(_log, 10);
        progress.AddMaterial(_stick, 10);
        UnlockRegistry registry = Registry(progress);
        var unlocked = new List<string>();
        registry.OnUnlocked += (kind, id) => { if (kind == UnlockKind.HubBuilding) unlocked.Add(id); };

        var hub = new HubService(_catalog, progress, registry);
        Assert.AreEqual(HubBuildResult.Locked, hub.CheckAvailability(_storage));
        Assert.AreEqual(HubBuildResult.Locked, hub.TryBuild(_storage, Vector2Int.zero));

        // Каталог известен реестру — открытие с событием (для всплывашки)
        Assert.IsTrue(registry.UnlockHubBuilding("storage"));
        CollectionAssert.AreEqual(new[] { "storage" }, unlocked);
        Assert.AreEqual(HubBuildResult.Ok, hub.TryBuild(_storage, Vector2Int.zero));
    }

    [Test]
    public void UnknownDefinition_IsRefused()
    {
        HubBuildingDefinition stranger = Define("stranger", Vector2Int.one, new[] { Stack(_log, 1) });
        HubService hub = Hub(_saves.Progress(), (_log, 10));
        Assert.AreEqual(HubBuildResult.Unknown, hub.TryBuild(stranger, Vector2Int.zero));
        Assert.AreEqual(HubBuildResult.Unknown, hub.TryBuild(null, Vector2Int.zero));
    }

    // ─── Сохранение ───────────────────────────────────────────

    [Test]
    public void TryBuild_SavesLayoutAndStock()
    {
        HubService hub = Hub(_saves.Progress(), (_log, 8), (_stone, 6));
        Assert.AreEqual(HubBuildResult.Ok, hub.TryBuild(_shrine, new Vector2Int(3, 2)));

        // Новый MetaProgress читает файл — TryBuild сохранил сам, без отдельного Save
        MetaProgress reloaded = _saves.Progress();
        Assert.IsTrue(reloaded.HasHubBuilding("shrine"));
        PlacedHubBuilding placed = reloaded.FindHubBuilding("shrine");
        Assert.AreEqual(3, placed.x);
        Assert.AreEqual(2, placed.y);
        Assert.AreEqual(4, reloaded.GetMaterialCount("log"));
        Assert.AreEqual(0, reloaded.GetMaterialCount("stone"));

        var reloadedHub = new HubService(_catalog, reloaded, unlocks: null);
        List<HubPlacement> layout = reloadedHub.GetPlaced();
        Assert.AreEqual(1, layout.Count);
        Assert.AreSame(_shrine, layout[0].Definition);
        Assert.AreEqual(new RectInt(3, 2, 2, 2), layout[0].Area);
        Assert.IsFalse(reloadedHub.CanPlaceAt(_storage, new Vector2Int(4, 3)));
    }

    [Test]
    public void Load_LayoutFromFile_DuplicatesAndEmptyIdsDropped()
    {
        File.WriteAllText(_saves.PathOf("meta_progress.json"), @"{
    ""hubBuildings"": [
        { ""buildingId"": ""storage"", ""x"": 1, ""y"": 2 },
        { ""buildingId"": ""storage"", ""x"": 5, ""y"": 0 },
        { ""buildingId"": """", ""x"": 0, ""y"": 0 },
        { ""buildingId"": ""old_building"", ""x"": 7, ""y"": 5 }
    ]
}");
        MetaProgress progress = _saves.Progress();

        Assert.AreEqual(2, progress.HubBuildings.Count);
        Assert.AreEqual(1, progress.FindHubBuilding("storage").x, "first entry wins");

        var hub = new HubService(_catalog, progress, unlocks: null);
        // Постройка не из каталога занимает свою клетку
        Assert.AreEqual("old_building", hub.GetPlacementAt(new Vector2Int(7, 5)).Id);
        Assert.IsNull(hub.GetPlacementAt(new Vector2Int(7, 5)).Definition);
    }

    // ─── RunModifiers ─────────────────────────────────────────

    [Test]
    public void RunModifiers_NoBuildings_BaseValues()
    {
        RunModifiers modifiers = RunModifiers.Create(Config(), _saves.Progress(), _catalog);
        Assert.AreEqual(100f, modifiers.BaseEnergyLimit);
        Assert.AreEqual(100f, modifiers.MaxHealth);
        Assert.AreEqual(10, modifiers.InventorySlots);
        Assert.AreEqual(3, modifiers.ThreatDay);
        Assert.AreEqual(3, modifiers.RunsPerDay);
    }

    [Test]
    public void RunModifiers_AllFourBuildings_ApplyTheirBonuses()
    {
        MetaProgress progress = _saves.Progress();
        progress.AddHubBuilding("storage", new Vector2Int(0, 0));
        progress.AddHubBuilding("shrine", new Vector2Int(2, 0));
        progress.AddHubBuilding("watchtower", new Vector2Int(4, 0));
        progress.AddHubBuilding("healer_hut", new Vector2Int(5, 0));

        RunModifiers modifiers = RunModifiers.Create(Config(), progress, _catalog);
        Assert.AreEqual(12, modifiers.InventorySlots);
        Assert.AreEqual(110f, modifiers.BaseEnergyLimit);
        Assert.AreEqual(4, modifiers.ThreatDay);
        Assert.AreEqual(125f, modifiers.MaxHealth);
        Assert.AreEqual(3, modifiers.RunsPerDay);
    }

    [Test]
    public void RunModifiers_WithoutCatalogOrUnknownIds_NoBonuses()
    {
        MetaProgress progress = _saves.Progress();
        progress.AddHubBuilding("storage", Vector2Int.zero);
        progress.AddHubBuilding("old_building", new Vector2Int(4, 4));

        Assert.AreEqual(10, RunModifiers.Create(Config(), progress).InventorySlots, "no catalog — base only");

        RunModifiers modifiers = RunModifiers.Create(Config(), progress, _catalog);
        Assert.AreEqual(12, modifiers.InventorySlots, "unknown id is skipped, storage still applies");
    }

    [Test]
    public void RunModifiers_RunsPerDayEffect()
    {
        HubBuildingDefinition clock = Define("clock", Vector2Int.one, new[] { Stack(_log, 1) },
            Effect(HubEffectType.RunsPerDay, 1), Effect(HubEffectType.EnergyLimit, 5));
        _catalog = Catalog(clock);
        MetaProgress progress = _saves.Progress();
        progress.AddHubBuilding("clock", Vector2Int.zero);

        RunModifiers modifiers = RunModifiers.Create(Config(), progress, _catalog);
        Assert.AreEqual(4, modifiers.RunsPerDay);
        Assert.AreEqual(105f, modifiers.BaseEnergyLimit, "several effects of one building add up");
    }

    // ─── Событие ──────────────────────────────────────────────

    [Test]
    public void TryBuild_PublishesHubBuildingBuiltAndOnBuilt()
    {
        HubService hub = Hub(_saves.Progress(), (_log, 6), (_stick, 4));
        var built = new List<(HubBuildingDefinition, Vector2Int)>();
        hub.OnBuilt += (definition, cell) => built.Add((definition, cell));

        Assert.AreEqual(HubBuildResult.Ok, hub.TryBuild(_storage, new Vector2Int(2, 3)));

        Assert.AreEqual(1, _events.Count);
        Assert.AreEqual(GameEventType.HubBuildingBuilt, _events[0].Type);
        Assert.AreEqual("storage", _events[0].Id);
        Assert.AreEqual(1, _events[0].Amount);
        Assert.AreEqual(1, built.Count);
        Assert.AreSame(_storage, built[0].Item1);
        Assert.AreEqual(new Vector2Int(2, 3), built[0].Item2);
    }

    [Test]
    public void HubBuilder_Achievement_CompletesOnFirstBuilding()
    {
        MetaProgress progress = _saves.Progress();
        progress.AddMaterial(_log, 6);
        progress.AddMaterial(_stick, 4);
        UnlockRegistry registry = Registry(progress);
        registry.UnlockHubBuilding("storage");

        AchievementDefinition builder = Track(ScriptableObject.CreateInstance<AchievementDefinition>());
        builder.name = "hub_builder";
        SetPrivate(builder, "id", "hub_builder");
        SetPrivate(builder, "title", "hub_builder");
        SetPrivate(builder, "condition", new AchievementCondition
        {
            type = AchievementConditionType.FirstUse, gameEvent = GameEventType.HubBuildingBuilt
        });
        SetPrivate(builder, "rewards", new List<AchievementReward>());
        AchievementCatalog achievements = Track(ScriptableObject.CreateInstance<AchievementCatalog>());
        SetPrivate(achievements, "achievements", new List<AchievementDefinition> { builder });

        var tracker = new AchievementTracker(achievements, progress, registry, new PlayerEnergy(100f));
        try
        {
            var hub = new HubService(_catalog, progress, registry);
            Assert.AreEqual(HubBuildResult.Ok, hub.TryBuild(_storage, Vector2Int.zero));
            Assert.IsTrue(tracker.IsCompleted(builder));
            Assert.IsTrue(_saves.Progress().HasAchievement("hub_builder"), "completion is saved");
        }
        finally
        {
            tracker.Dispose();
        }
    }

    // ─── Уровни (T14) ─────────────────────────────────────────

    [Test]
    public void Levels_CostAndCumulativeEffects()
    {
        Assert.AreEqual(1, _shrine.MaxLevel, "no levels — single-level building");
        CollectionAssert.IsEmpty(_shrine.GetCost(2));

        StorageWithLevels();
        Assert.AreEqual(3, _storage.MaxLevel);
        Assert.AreEqual(2, _storage.GetCost(1).Count, "level 1 = the building cost");
        List<HubBuildingCost> cost2 = _storage.GetCost(2);
        Assert.AreEqual(2, cost2.Count);
        Assert.AreEqual("log", cost2[0].Key);
        Assert.AreEqual(10, cost2[0].Amount);
        Assert.AreEqual("crystal", cost2[1].Key);
        CollectionAssert.IsEmpty(_storage.GetCost(4));

        // Эффекты уровней — прибавки, итог складывается по типам
        List<HubBuildingEffect> level3 = _storage.GetEffects(3);
        Assert.AreEqual(2, level3.Count);
        Assert.AreEqual(HubEffectType.InventorySlots, level3[0].type);
        Assert.AreEqual(5f, level3[0].amount, "2 + 1 + 2 slots");
        Assert.AreEqual(HubEffectType.EnergyLimit, level3[1].type);
        Assert.AreEqual(5f, level3[1].amount);
        Assert.AreEqual(2f, _storage.GetEffects(1)[0].amount);
        Assert.AreEqual(5f, _storage.GetEffects(99)[0].amount, "level is clamped to MaxLevel");
        Assert.AreEqual(2f, ((List<HubBuildingEffect>)_storage.Effects)[0].amount, "totals don't touch the asset");
    }

    [Test]
    public void CheckUpgrade_NotBuilt_MaxLevel_SingleLevel()
    {
        StorageWithLevels();
        HubService hub = Hub(_saves.Progress(), (_log, 100), (_stick, 100), (_stone, 100), (_crystal, 10), (_gold, 10));

        Assert.AreEqual(HubUpgradeResult.NotBuilt, hub.CheckUpgrade(_storage));
        Assert.AreEqual(0, hub.GetLevel(_storage));
        Assert.AreEqual(HubUpgradeResult.Unknown, hub.CheckUpgrade(null));

        Assert.AreEqual(HubBuildResult.Ok, hub.TryBuild(_storage, Vector2Int.zero));
        Assert.AreEqual(1, hub.GetLevel(_storage));
        Assert.IsTrue(hub.CanUpgrade(_storage));
        Assert.AreEqual(HubUpgradeResult.Ok, hub.TryUpgrade(_storage));
        Assert.AreEqual(HubUpgradeResult.Ok, hub.TryUpgrade(_storage));
        Assert.AreEqual(3, hub.GetLevel(_storage));
        Assert.AreEqual(HubUpgradeResult.MaxLevel, hub.TryUpgrade(_storage));

        Assert.AreEqual(HubBuildResult.Ok, hub.TryBuild(_shrine, new Vector2Int(2, 0)));
        Assert.AreEqual(HubUpgradeResult.MaxLevel, hub.CheckUpgrade(_shrine), "single-level building");
    }

    [Test]
    public void TryUpgrade_NotAffordable_ChangesNothing()
    {
        StorageWithLevels();
        MetaProgress progress = _saves.Progress();
        progress.AddHubBuilding("storage", Vector2Int.zero);
        HubService hub = Hub(progress, (_log, 10), (_crystal, 1));
        _events.Clear();
        int upgraded = 0;
        hub.OnUpgraded += (definition, level) => upgraded++;

        Assert.AreEqual(HubUpgradeResult.NotAffordable, hub.CheckUpgrade(_storage), "1 crystal of 2");
        Assert.AreEqual(HubUpgradeResult.NotAffordable, hub.TryUpgrade(_storage));
        Assert.AreEqual(10, hub.GetStored("log"));
        Assert.AreEqual(1, hub.GetStored("crystal"));
        Assert.AreEqual(1, hub.GetLevel(_storage));
        Assert.AreEqual(0, upgraded);
        CollectionAssert.IsEmpty(_events);
    }

    [Test]
    public void TryUpgrade_TakesCost_SavesLevel_PublishesEvent()
    {
        StorageWithLevels();
        HubService hub = Hub(_saves.Progress(), (_log, 20), (_stick, 4), (_crystal, 3));
        Assert.AreEqual(HubBuildResult.Ok, hub.TryBuild(_storage, new Vector2Int(1, 1)));
        _events.Clear();
        var upgraded = new List<(HubBuildingDefinition, int)>();
        hub.OnUpgraded += (definition, level) => upgraded.Add((definition, level));

        Assert.AreEqual(HubUpgradeResult.Ok, hub.TryUpgrade(_storage));

        Assert.AreEqual(4, hub.GetStored("log"), "20 − 6 (build) − 10 (level 2)");
        Assert.AreEqual(1, hub.GetStored("crystal"));
        Assert.AreEqual(1, _events.Count);
        Assert.AreEqual(GameEventType.HubBuildingUpgraded, _events[0].Type);
        Assert.AreEqual("storage", _events[0].Id);
        Assert.AreEqual(2, _events[0].Amount, "Amount = new level");
        Assert.AreEqual(1, upgraded.Count);
        Assert.AreSame(_storage, upgraded[0].Item1);
        Assert.AreEqual(2, upgraded[0].Item2);

        // Перезапуск: новый MetaProgress читает файл — уровень, место и склад сохранены
        MetaProgress reloaded = _saves.Progress();
        PlacedHubBuilding placed = reloaded.FindHubBuilding("storage");
        Assert.AreEqual(2, placed.Level);
        Assert.AreEqual(1, placed.x);
        Assert.AreEqual(1, placed.y);
        Assert.AreEqual(4, reloaded.GetMaterialCount("log"));
        Assert.AreEqual(1, reloaded.GetMaterialCount("crystal"));
        Assert.AreEqual(2, new HubService(_catalog, reloaded, unlocks: null).GetPlaced()[0].Level);
    }

    [Test]
    public void RunModifiers_UseEffectsOfCurrentLevel()
    {
        StorageWithLevels();
        MetaProgress progress = _saves.Progress();
        progress.AddHubBuilding("storage", Vector2Int.zero);
        progress.AddHubBuilding("shrine", new Vector2Int(2, 0));

        RunModifiers level1 = RunModifiers.Create(Config(), progress, _catalog);
        Assert.AreEqual(12, level1.InventorySlots);
        Assert.AreEqual(110f, level1.BaseEnergyLimit, "shrine only");

        progress.FindHubBuilding("storage").level = 2;
        RunModifiers level2 = RunModifiers.Create(Config(), progress, _catalog);
        Assert.AreEqual(13, level2.InventorySlots);
        Assert.AreEqual(115f, level2.BaseEnergyLimit, "shrine +10, storage level 2 +5");

        progress.FindHubBuilding("storage").level = 3;
        RunModifiers level3 = RunModifiers.Create(Config(), progress, _catalog);
        Assert.AreEqual(15, level3.InventorySlots);
        Assert.AreEqual(115f, level3.BaseEnergyLimit);

        // Уровень выше максимума (ассет укоротили) — эффекты максимального
        progress.FindHubBuilding("storage").level = 7;
        Assert.AreEqual(15, RunModifiers.Create(Config(), progress, _catalog).InventorySlots);
    }

    [Test]
    public void Load_OldSaveWithoutLevel_IsLevelOne()
    {
        StorageWithLevels();
        File.WriteAllText(_saves.PathOf("meta_progress.json"), @"{
    ""hubBuildings"": [
        { ""buildingId"": ""storage"", ""x"": 1, ""y"": 2 },
        { ""buildingId"": ""shrine"", ""x"": 4, ""y"": 0, ""level"": 0 }
    ]
}");
        MetaProgress progress = _saves.Progress();
        Assert.AreEqual(1, progress.FindHubBuilding("storage").Level, "missing field");
        Assert.AreEqual(1, progress.FindHubBuilding("shrine").Level, "0 is treated as 1");

        HubService hub = Hub(progress, (_log, 10), (_crystal, 2));
        Assert.AreEqual(1, hub.GetLevel(_storage));
        Assert.AreEqual(HubUpgradeResult.Ok, hub.CheckUpgrade(_storage), "old building can be upgraded to level 2");

        RunModifiers modifiers = RunModifiers.Create(Config(), progress, _catalog);
        Assert.AreEqual(12, modifiers.InventorySlots, "level 1 effects only");
        Assert.AreEqual(110f, modifiers.BaseEnergyLimit);
    }

    // ─── Соседство (T15) ──────────────────────────────────────

    private static HubAdjacencyBonus Bonus(string neighbourId, HubEffectType effect, float amount) =>
        new HubAdjacencyBonus { neighbourId = neighbourId, effect = effect, amount = amount };

    private static void WithAdjacency(HubBuildingDefinition definition, params HubAdjacencyBonus[] bonuses) =>
        SetPrivate(definition, "adjacencyBonuses", new List<HubAdjacencyBonus>(bonuses));

    private static HubPlacement At(HubBuildingDefinition definition, int x, int y) =>
        new HubPlacement(definition.Id, definition, new Vector2Int(x, y));

    [Test]
    public void AreSideAdjacent_SharedEdgeOnly()
    {
        var a = new RectInt(0, 0, 2, 2);
        Assert.IsTrue(HubService.AreSideAdjacent(a, new RectInt(2, 0, 2, 2)), "right side, full edge");
        Assert.IsTrue(HubService.AreSideAdjacent(a, new RectInt(-1, 0, 1, 2)), "left side");
        Assert.IsTrue(HubService.AreSideAdjacent(a, new RectInt(0, 2, 2, 2)), "top side");
        Assert.IsTrue(HubService.AreSideAdjacent(a, new RectInt(1, -2, 2, 2)), "bottom, partial edge (1 cell)");
        Assert.IsTrue(HubService.AreSideAdjacent(a, new RectInt(2, 1, 1, 2)), "right, partial edge");

        Assert.IsFalse(HubService.AreSideAdjacent(a, new RectInt(2, 2, 2, 2)), "diagonal corner only");
        Assert.IsFalse(HubService.AreSideAdjacent(a, new RectInt(-1, -1, 1, 1)), "diagonal corner only (bottom-left)");
        Assert.IsFalse(HubService.AreSideAdjacent(a, new RectInt(3, 0, 2, 2)), "one cell gap");
        Assert.IsFalse(HubService.AreSideAdjacent(a, new RectInt(1, 1, 2, 2)), "overlapping is not adjacency");
    }

    [Test]
    public void Adjacency_SideGivesBonus_DiagonalDoesNot()
    {
        WithAdjacency(_healerHut, Bonus("shrine", HubEffectType.MaxHealth, 10));

        List<HubAdjacencyMatch> side = HubService.GetAdjacencyBonuses(
            new[] { At(_shrine, 0, 0), At(_healerHut, 2, 1) });
        Assert.AreEqual(1, side.Count);
        Assert.AreEqual("healer_hut", side[0].Owner.Id, "the entry belongs to the healer's hut");
        Assert.AreEqual("shrine", side[0].Neighbour.Id);
        Assert.AreEqual(HubEffectType.MaxHealth, side[0].Effect);
        Assert.AreEqual(10f, side[0].Amount);

        CollectionAssert.IsEmpty(HubService.GetAdjacencyBonuses(new[] { At(_shrine, 0, 0), At(_healerHut, 2, 2) }),
            "diagonal");
        CollectionAssert.IsEmpty(HubService.GetAdjacencyBonuses(new[] { At(_shrine, 0, 0), At(_healerHut, 3, 0) }),
            "gap");
        CollectionAssert.IsEmpty(HubService.GetAdjacencyBonuses(new[] { At(_healerHut, 2, 0) }), "alone");
    }

    [Test]
    public void Adjacency_MultipleNeighbours_EachEntryOnce()
    {
        // storage (2..3, 0..1) получает бонусы от shrine слева и watchtower справа; shrine — свой от storage
        WithAdjacency(_storage,
            Bonus("shrine", HubEffectType.EnergyLimit, 5),
            Bonus("watchtower", HubEffectType.InventorySlots, 1));
        WithAdjacency(_shrine, Bonus("storage", HubEffectType.MaxHealth, 7));

        List<HubAdjacencyMatch> matches = HubService.GetAdjacencyBonuses(
            new[] { At(_shrine, 0, 0), At(_storage, 2, 0), At(_watchtower, 4, 0) });
        Assert.AreEqual(3, matches.Count);
        Assert.IsTrue(matches.Exists(m => m.Owner.Id == "storage" && m.Neighbour.Id == "shrine"));
        Assert.IsTrue(matches.Exists(m => m.Owner.Id == "storage" && m.Neighbour.Id == "watchtower"));
        Assert.IsTrue(matches.Exists(m => m.Owner.Id == "shrine" && m.Neighbour.Id == "storage"));

        // Правило «запись — один раз»: два соседа с одним id (в сохранении невозможно, в чистой логике — да)
        List<HubAdjacencyMatch> twice = HubService.GetAdjacencyBonuses(
            new[] { At(_storage, 2, 0), At(_shrine, 0, 0), new HubPlacement("shrine", _shrine, new Vector2Int(4, 0)) });
        Assert.AreEqual(1, twice.FindAll(m => m.Owner.Id == "storage" && m.Neighbour.Id == "shrine").Count);
    }

    [Test]
    public void Adjacency_UnknownBuildingsAndBadEntries_Ignored()
    {
        WithAdjacency(_storage, Bonus("old_building", HubEffectType.EnergyLimit, 5), null,
            Bonus("", HubEffectType.EnergyLimit, 5), Bonus("storage", HubEffectType.EnergyLimit, 5));

        var unknown = new HubPlacement("old_building", null, new Vector2Int(2, 0));
        CollectionAssert.IsEmpty(HubService.GetAdjacencyBonuses(new[] { At(_storage, 0, 0), unknown }),
            "a building missing from the catalog neither gives nor gets bonuses; self/empty ids never apply");
        CollectionAssert.IsEmpty(HubService.GetAdjacencyBonuses(null));
    }

    [Test]
    public void Adjacency_NoCatalog_NoBonuses()
    {
        WithAdjacency(_storage, Bonus("shrine", HubEffectType.EnergyLimit, 5));
        MetaProgress progress = _saves.Progress();
        progress.AddHubBuilding("shrine", new Vector2Int(0, 0));
        progress.AddHubBuilding("storage", new Vector2Int(2, 0));

        var hub = new HubService(null, progress, unlocks: null);
        CollectionAssert.IsEmpty(hub.GetAdjacencyBonuses());
        CollectionAssert.IsEmpty(HubService.GetAdjacencyBonuses(HubService.CollectPlacements(progress, null)));
        Assert.AreEqual(100f, RunModifiers.Create(Config(), progress).BaseEnergyLimit, "no catalog — base only");
    }

    [Test]
    public void PreviewAdjacency_ReceivedAndGivenBonuses()
    {
        // healer_hut получает от shrine; storage даёт бонус, когда рядом healer_hut
        WithAdjacency(_healerHut, Bonus("shrine", HubEffectType.MaxHealth, 10));
        WithAdjacency(_storage, Bonus("healer_hut", HubEffectType.InventorySlots, 1));
        MetaProgress progress = _saves.Progress();
        progress.AddHubBuilding("shrine", new Vector2Int(0, 0));
        progress.AddHubBuilding("storage", new Vector2Int(4, 0));
        HubService hub = Hub(progress);

        // (2,0): слева shrine, справа storage — оба бонуса
        List<HubAdjacencyMatch> both = hub.PreviewAdjacencyBonuses(_healerHut, new Vector2Int(2, 0));
        Assert.AreEqual(2, both.Count);
        HubAdjacencyMatch received = both.Find(m => m.Owner.Id == "healer_hut");
        HubAdjacencyMatch given = both.Find(m => m.Owner.Id == "storage");
        Assert.AreEqual("shrine", received.Other("healer_hut").Id);
        Assert.AreEqual("storage", given.Other("healer_hut").Id, "highlight the existing building");
        Assert.AreEqual(HubEffectType.InventorySlots, given.Effect);

        // (2,2): касается обоих только углом
        CollectionAssert.IsEmpty(hub.PreviewAdjacencyBonuses(_healerHut, new Vector2Int(2, 2)));
        // Занято / вне сетки — пусто
        CollectionAssert.IsEmpty(hub.PreviewAdjacencyBonuses(_healerHut, new Vector2Int(1, 0)));
        CollectionAssert.IsEmpty(hub.PreviewAdjacencyBonuses(_healerHut, new Vector2Int(7, 0)));
        CollectionAssert.IsEmpty(hub.PreviewAdjacencyBonuses(null, Vector2Int.zero));

        // Предпросмотр ничего не меняет
        Assert.AreEqual(2, hub.GetPlaced().Count);
        CollectionAssert.IsEmpty(hub.GetAdjacencyBonuses());
    }

    [Test]
    public void PossibleAdjacency_OwnAndOthersEntries()
    {
        WithAdjacency(_healerHut, Bonus("shrine", HubEffectType.MaxHealth, 10));
        WithAdjacency(_shrine, Bonus("healer_hut", HubEffectType.EnergyLimit, 5));
        WithAdjacency(_storage, Bonus("watchtower", HubEffectType.InventorySlots, 1));
        HubService hub = Hub(_saves.Progress());

        var forShrine = hub.GetPossibleAdjacencyBonuses(_shrine);
        Assert.AreEqual(2, forShrine.Count, "own entry + the healer's hut entry naming the shrine");
        Assert.IsTrue(forShrine.Exists(p => p.Owner == _shrine && p.OtherId == "healer_hut"));
        Assert.IsTrue(forShrine.Exists(p => p.Owner == _healerHut && p.OtherId == "healer_hut"));
        Assert.AreEqual(1, hub.GetPossibleAdjacencyBonuses(_watchtower).Count);
    }

    [Test]
    public void RunModifiers_AdjacencyBonusesApplied()
    {
        WithAdjacency(_storage, Bonus("watchtower", HubEffectType.InventorySlots, 1));
        WithAdjacency(_healerHut, Bonus("shrine", HubEffectType.MaxHealth, 10));
        WithAdjacency(_shrine, Bonus("healer_hut", HubEffectType.EnergyLimit, 5));
        MetaProgress progress = _saves.Progress();
        progress.AddHubBuilding("storage", new Vector2Int(0, 0));
        progress.AddHubBuilding("watchtower", new Vector2Int(2, 0));
        progress.AddHubBuilding("shrine", new Vector2Int(3, 0));
        progress.AddHubBuilding("healer_hut", new Vector2Int(5, 2)); // к shrine только углом

        RunModifiers modifiers = RunModifiers.Create(Config(), progress, _catalog);
        Assert.AreEqual(13, modifiers.InventorySlots, "10 + storage 2 + next to watchtower 1");
        Assert.AreEqual(110f, modifiers.BaseEnergyLimit, "diagonal healer's hut — no shrine bonus");
        Assert.AreEqual(125f, modifiers.MaxHealth, "diagonal — no healer bonus");
        Assert.AreEqual(4, modifiers.ThreatDay);

        // Хижина вплотную справа от святилища — оба взаимных бонуса
        PlacedHubBuilding healer = progress.FindHubBuilding("healer_hut");
        healer.x = 5;
        healer.y = 1;
        modifiers = RunModifiers.Create(Config(), progress, _catalog);
        Assert.AreEqual(115f, modifiers.BaseEnergyLimit);
        Assert.AreEqual(135f, modifiers.MaxHealth);

        Assert.AreEqual(10, RunModifiers.Create(Config(), progress).InventorySlots, "no catalog — base only");
    }
}
