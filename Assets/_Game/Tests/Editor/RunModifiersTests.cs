using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// RunModifiers.Create(config, progress, catalog): база из PlayerConfig без каталога/построек, каждый HubEffectType
/// и сумма нескольких построек, постройки не из каталога пропускаются, награды головоломок
/// (AddEnergyLimit/AddMaxHealth/DelayThreat) действуют до конца забега и не сохраняются, стартовые предметы.
/// MetaProgress — во временной папке.
/// </summary>
public class RunModifiersTests
{
    private TestHelpers.TempSaveFolder _saves;
    private readonly List<Object> _created = new List<Object>();
    private PlayerConfig _config;

    [SetUp]
    public void SetUp()
    {
        _saves = new TestHelpers.TempSaveFolder();
        _config = Track(ScriptableObject.CreateInstance<PlayerConfig>());
        _config.baseEnergyLimit = 100f;
        _config.maxHealth = 80f;
        _config.inventorySlots = 10;
        _config.runsPerDay = 3;
        _config.threatDay = 4;
        _config.sleepHealAmount = 15f;
        _config.startingItems = new List<ItemStack>();
    }

    [TearDown]
    public void TearDown()
    {
        foreach (Object obj in _created)
            if (obj != null) Object.DestroyImmediate(obj);
        _created.Clear();
        _saves.Dispose();
    }

    // ─── База ─────────────────────────────────────────────────

    [Test]
    public void Create_NoCatalog_BaseValuesFromConfig()
    {
        MetaProgress progress = _saves.Progress();
        progress.AddHubBuilding("shrine", Vector2Int.zero);

        AssertBase(RunModifiers.Create(_config, progress));
        AssertBase(RunModifiers.Create(_config, progress, catalog: null));
    }

    [Test]
    public void Create_CatalogWithoutBuildings_BaseValues()
    {
        HubBuildingCatalog catalog = Catalog(Define("shrine", Effect(HubEffectType.EnergyLimit, 10f)));

        AssertBase(RunModifiers.Create(_config, _saves.Progress(), catalog));
    }

    [Test]
    public void Create_NullProgress_BaseValues()
    {
        HubBuildingCatalog catalog = Catalog(Define("shrine", Effect(HubEffectType.EnergyLimit, 10f)));

        AssertBase(RunModifiers.Create(_config, null, catalog));
    }

    [Test]
    public void Create_InvalidConfigValues_ClampedToMinimums()
    {
        _config.baseEnergyLimit = -5f;
        _config.maxHealth = 0f;
        _config.inventorySlots = 0;
        _config.runsPerDay = 0;
        _config.threatDay = 0;
        _config.sleepHealAmount = -1f;

        RunModifiers modifiers = RunModifiers.Create(_config, _saves.Progress());

        Assert.AreEqual(0f, modifiers.BaseEnergyLimit);
        Assert.AreEqual(1f, modifiers.MaxHealth);
        Assert.AreEqual(1, modifiers.InventorySlots);
        Assert.AreEqual(1, modifiers.RunsPerDay);
        Assert.AreEqual(1, modifiers.ThreatDay);
        Assert.AreEqual(0f, modifiers.SleepHeal);
    }

    // ─── Эффекты построек ─────────────────────────────────────

    [TestCase(HubEffectType.EnergyLimit, 10f, 110f, 80f, 10, 3, 4)]
    [TestCase(HubEffectType.ThreatDays, 1f, 100f, 80f, 10, 3, 5)]
    [TestCase(HubEffectType.InventorySlots, 2f, 100f, 80f, 12, 3, 4)]
    [TestCase(HubEffectType.MaxHealth, 25f, 100f, 105f, 10, 3, 4)]
    [TestCase(HubEffectType.RunsPerDay, 1f, 100f, 80f, 10, 4, 4)]
    public void Create_SingleEffect_ChangesOnlyItsValue(HubEffectType type, float amount,
        float energy, float health, int slots, int runs, int threatDay)
    {
        RunModifiers modifiers = WithBuildings(Define("building", Effect(type, amount)));

        Assert.AreEqual(energy, modifiers.BaseEnergyLimit, 0.001f, "BaseEnergyLimit");
        Assert.AreEqual(health, modifiers.MaxHealth, 0.001f, "MaxHealth");
        Assert.AreEqual(slots, modifiers.InventorySlots, "InventorySlots");
        Assert.AreEqual(runs, modifiers.RunsPerDay, "RunsPerDay");
        Assert.AreEqual(threatDay, modifiers.ThreatDay, "ThreatDay");
        Assert.AreEqual(15f, modifiers.SleepHeal, 0.001f, "SleepHeal is not a hub effect");
    }

    [Test]
    public void Create_SeveralBuildings_EffectsAddUp()
    {
        RunModifiers modifiers = WithBuildings(
            Define("shrine", Effect(HubEffectType.EnergyLimit, 10f)),
            Define("altar", Effect(HubEffectType.EnergyLimit, 15f), Effect(HubEffectType.MaxHealth, 5f)),
            Define("storage", Effect(HubEffectType.InventorySlots, 2f)),
            Define("barn", Effect(HubEffectType.InventorySlots, 3f)),
            Define("watchtower", Effect(HubEffectType.ThreatDays, 1f)),
            Define("beacon", Effect(HubEffectType.ThreatDays, 2f)),
            Define("healer_hut", Effect(HubEffectType.MaxHealth, 25f)),
            Define("clock", Effect(HubEffectType.RunsPerDay, 1f)),
            Define("sundial", Effect(HubEffectType.RunsPerDay, 1f)));

        Assert.AreEqual(125f, modifiers.BaseEnergyLimit, 0.001f);
        Assert.AreEqual(110f, modifiers.MaxHealth, 0.001f);
        Assert.AreEqual(15, modifiers.InventorySlots);
        Assert.AreEqual(7, modifiers.ThreatDay);
        Assert.AreEqual(5, modifiers.RunsPerDay);
    }

    [Test]
    public void Create_SeveralEffectsOfOneBuilding_AllApply()
    {
        RunModifiers modifiers = WithBuildings(Define("castle",
            Effect(HubEffectType.EnergyLimit, 5f), Effect(HubEffectType.EnergyLimit, 5f),
            Effect(HubEffectType.InventorySlots, 1f), Effect(HubEffectType.RunsPerDay, 1f)));

        Assert.AreEqual(110f, modifiers.BaseEnergyLimit, 0.001f);
        Assert.AreEqual(11, modifiers.InventorySlots);
        Assert.AreEqual(4, modifiers.RunsPerDay);
    }

    [Test]
    public void Create_FractionalAmountForIntegerValues_Rounded()
    {
        RunModifiers modifiers = WithBuildings(Define("odd",
            Effect(HubEffectType.InventorySlots, 2.4f), Effect(HubEffectType.ThreatDays, 1.6f),
            Effect(HubEffectType.EnergyLimit, 2.5f)));

        Assert.AreEqual(12, modifiers.InventorySlots, "2.4 → 2");
        Assert.AreEqual(6, modifiers.ThreatDay, "1.6 → 2");
        Assert.AreEqual(102.5f, modifiers.BaseEnergyLimit, 0.001f, "energy is not rounded");
    }

    [Test]
    public void Create_NegativeEffects_ClampedToMinimums()
    {
        RunModifiers modifiers = WithBuildings(Define("cursed",
            Effect(HubEffectType.RunsPerDay, -10f), Effect(HubEffectType.InventorySlots, -20f),
            Effect(HubEffectType.ThreatDays, -10f), Effect(HubEffectType.MaxHealth, -500f),
            Effect(HubEffectType.EnergyLimit, -500f)));

        Assert.AreEqual(1, modifiers.RunsPerDay);
        Assert.AreEqual(1, modifiers.InventorySlots);
        Assert.AreEqual(1, modifiers.ThreatDay);
        Assert.AreEqual(1f, modifiers.MaxHealth, 0.001f);
        Assert.AreEqual(0f, modifiers.BaseEnergyLimit, 0.001f);
    }

    [Test]
    public void Create_SavedBuildingNotInCatalog_IgnoredWithoutException()
    {
        HubBuildingCatalog catalog = Catalog(Define("shrine", Effect(HubEffectType.EnergyLimit, 10f)), null);
        MetaProgress progress = _saves.Progress();
        progress.AddHubBuilding("old_building", new Vector2Int(5, 5));
        progress.AddHubBuilding("shrine", Vector2Int.zero);

        RunModifiers modifiers = null;
        Assert.DoesNotThrow(() => modifiers = RunModifiers.Create(_config, progress, catalog));

        Assert.AreEqual(110f, modifiers.BaseEnergyLimit, 0.001f, "the known building still applies");
        Assert.AreEqual(10, modifiers.InventorySlots);
    }

    [Test]
    public void Create_OnlyUnknownBuildings_BaseValues()
    {
        HubBuildingCatalog catalog = Catalog(Define("shrine", Effect(HubEffectType.EnergyLimit, 10f)));
        MetaProgress progress = _saves.Progress();
        progress.AddHubBuilding("ghost", Vector2Int.zero);

        AssertBase(RunModifiers.Create(_config, progress, catalog));
    }

    [Test]
    public void Create_DuplicateBuildingInSaveFile_AppliedOnce()
    {
        // Файл, правленный руками: одна постройка записана дважды, плюс неизвестная
        File.WriteAllText(_saves.PathOf("meta_progress.json"), @"{
    ""hubBuildings"": [
        { ""buildingId"": ""shrine"", ""x"": 0, ""y"": 0 },
        { ""buildingId"": ""shrine"", ""x"": 3, ""y"": 0 },
        { ""buildingId"": ""ghost"", ""x"": 5, ""y"": 5 }
    ]
}");
        MetaProgress progress = _saves.Progress();
        HubBuildingCatalog catalog = Catalog(Define("shrine", Effect(HubEffectType.EnergyLimit, 10f)));

        RunModifiers modifiers = RunModifiers.Create(_config, progress, catalog);

        Assert.AreEqual(110f, modifiers.BaseEnergyLimit, 0.001f);
    }

    [Test]
    public void Create_SavedAndReloadedProgress_SameBonuses()
    {
        HubBuildingCatalog catalog = Catalog(
            Define("storage", Effect(HubEffectType.InventorySlots, 2f)),
            Define("healer_hut", Effect(HubEffectType.MaxHealth, 25f)));
        MetaProgress progress = _saves.Progress();
        progress.AddHubBuilding("storage", Vector2Int.zero);
        progress.AddHubBuilding("healer_hut", new Vector2Int(2, 0));
        Assert.IsTrue(progress.Save());

        RunModifiers modifiers = RunModifiers.Create(_config, _saves.Progress(), catalog);

        Assert.AreEqual(12, modifiers.InventorySlots);
        Assert.AreEqual(105f, modifiers.MaxHealth, 0.001f);
    }

    // ─── Награды головоломок ──────────────────────────────────

    [Test]
    public void PuzzleRewards_AddToCurrentValues()
    {
        RunModifiers modifiers = WithBuildings(Define("shrine", Effect(HubEffectType.EnergyLimit, 10f)));

        modifiers.AddEnergyLimit(10f);
        modifiers.AddEnergyLimit(10f);
        modifiers.AddMaxHealth(20f);
        modifiers.DelayThreat(1);

        Assert.AreEqual(130f, modifiers.BaseEnergyLimit, 0.001f, "building + two rewards");
        Assert.AreEqual(100f, modifiers.MaxHealth, 0.001f);
        Assert.AreEqual(5, modifiers.ThreatDay);
    }

    [Test]
    public void PuzzleRewards_NegativeAmounts_Clamped()
    {
        RunModifiers modifiers = RunModifiers.Create(_config, _saves.Progress());

        modifiers.AddEnergyLimit(-1000f);
        modifiers.AddMaxHealth(-1000f);
        modifiers.DelayThreat(-100);

        Assert.AreEqual(0f, modifiers.BaseEnergyLimit);
        Assert.AreEqual(1f, modifiers.MaxHealth);
        Assert.AreEqual(1, modifiers.ThreatDay);
    }

    [Test]
    public void AddEnergyLimit_LastsForEveryFollowingDayOfTheRun()
    {
        RunModifiers modifiers = RunModifiers.Create(_config, _saves.Progress());
        var energy = new PlayerEnergy(modifiers.BaseEnergyLimit);
        var day = new DayCycle(_config, modifiers, energy, new NoBed());

        modifiers.AddEnergyLimit(10f);

        for (int i = 0; i < 3; i++)
        {
            Assert.IsTrue(day.TryEndDay());
            Assert.AreEqual(110f, energy.Max, 0.001f, $"day {day.Day}");
        }
    }

    [Test]
    public void DelayThreat_DayCycleSeesNewThreatDay()
    {
        RunModifiers modifiers = RunModifiers.Create(_config, _saves.Progress());
        var day = new DayCycle(_config, modifiers, new PlayerEnergy(100f), new NoBed());
        Assert.AreEqual(4, day.ThreatDay);
        Assert.AreEqual(3, day.DaysUntilThreat);

        modifiers.DelayThreat(2);

        Assert.AreEqual(6, day.ThreatDay);
        Assert.AreEqual(5, day.DaysUntilThreat);
        Assert.IsFalse(day.IsThreatActive);
    }

    [Test]
    public void PuzzleRewards_NotSaved_NextRunStartsFromBase()
    {
        HubBuildingCatalog catalog = Catalog(Define("shrine", Effect(HubEffectType.EnergyLimit, 10f)));
        MetaProgress progress = _saves.Progress();
        progress.AddHubBuilding("shrine", Vector2Int.zero);

        RunModifiers run = RunModifiers.Create(_config, progress, catalog);
        run.AddEnergyLimit(10f);
        run.AddMaxHealth(20f);
        run.DelayThreat(1);
        progress.Save();

        RunModifiers next = RunModifiers.Create(_config, _saves.Progress(), catalog);

        Assert.AreEqual(110f, next.BaseEnergyLimit, 0.001f, "only the building bonus remains");
        Assert.AreEqual(80f, next.MaxHealth, 0.001f);
        Assert.AreEqual(4, next.ThreatDay);
    }

    // ─── Стартовые предметы ───────────────────────────────────

    [Test]
    public void StartingItems_FromConfig_SkipsEmptyRows_KeepsOrder()
    {
        MaterialResource stick = Track(TestHelpers.Material("stick"));
        MaterialResource stone = Track(TestHelpers.Material("stone"));
        _config.startingItems = new List<ItemStack>
        {
            new ItemStack { item = stick, amount = 2 },
            new ItemStack { item = null, amount = 5 },
            new ItemStack { item = stone, amount = 0 },
            new ItemStack { item = stone, amount = 3 }
        };

        RunModifiers modifiers = RunModifiers.Create(_config, _saves.Progress());

        Assert.AreEqual(2, modifiers.StartingItems.Count);
        Assert.AreSame(stick, modifiers.StartingItems[0].item);
        Assert.AreEqual(2, modifiers.StartingItems[0].amount);
        Assert.AreSame(stone, modifiers.StartingItems[1].item);
        Assert.AreEqual(3, modifiers.StartingItems[1].amount);
    }

    [Test]
    public void StartingItems_EmptyConfig_EmptyList_BuildingsDoNotAddItems()
    {
        RunModifiers modifiers = WithBuildings(Define("storage", Effect(HubEffectType.InventorySlots, 2f)));

        Assert.IsNotNull(modifiers.StartingItems);
        Assert.AreEqual(0, modifiers.StartingItems.Count);
    }

    // ─── Заготовки ────────────────────────────────────────────

    private sealed class NoBed : ISleepingPlaceProvider
    {
        public bool IsSleepingPlaceNearPlayer() => false;
    }

    private void AssertBase(RunModifiers modifiers)
    {
        Assert.AreEqual(100f, modifiers.BaseEnergyLimit, 0.001f, "BaseEnergyLimit");
        Assert.AreEqual(80f, modifiers.MaxHealth, 0.001f, "MaxHealth");
        Assert.AreEqual(10, modifiers.InventorySlots, "InventorySlots");
        Assert.AreEqual(3, modifiers.RunsPerDay, "RunsPerDay");
        Assert.AreEqual(4, modifiers.ThreatDay, "ThreatDay");
        Assert.AreEqual(15f, modifiers.SleepHeal, 0.001f, "SleepHeal");
    }

    /// <summary>Все переданные постройки — в каталоге и построены (каждая в своей клетке).</summary>
    private RunModifiers WithBuildings(params HubBuildingDefinition[] buildings)
    {
        MetaProgress progress = _saves.Progress();
        for (int i = 0; i < buildings.Length; i++)
            Assert.IsTrue(progress.AddHubBuilding(buildings[i].Id, new Vector2Int(i, 0)));
        return RunModifiers.Create(_config, progress, Catalog(buildings));
    }

    private T Track<T>(T obj) where T : Object
    {
        _created.Add(obj);
        return obj;
    }

    private static void SetPrivate(object target, string field, object value)
    {
        FieldInfo info = target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(info, $"{target.GetType().Name}.{field} not found");
        info.SetValue(target, value);
    }

    private static HubBuildingEffect Effect(HubEffectType type, float amount) =>
        new HubBuildingEffect { type = type, amount = amount };

    private HubBuildingDefinition Define(string id, params HubBuildingEffect[] effects)
    {
        HubBuildingDefinition definition = Track(ScriptableObject.CreateInstance<HubBuildingDefinition>());
        definition.name = id;
        SetPrivate(definition, "id", id);
        SetPrivate(definition, "title", id);
        SetPrivate(definition, "size", Vector2Int.one);
        SetPrivate(definition, "cost", new List<ItemStack>());
        SetPrivate(definition, "effects", new List<HubBuildingEffect>(effects));
        return definition;
    }

    private HubBuildingCatalog Catalog(params HubBuildingDefinition[] buildings)
    {
        HubBuildingCatalog catalog = Track(ScriptableObject.CreateInstance<HubBuildingCatalog>());
        SetPrivate(catalog, "gridSize", new Vector2Int(8, 6));
        SetPrivate(catalog, "buildings", new List<HubBuildingDefinition>(buildings));
        return catalog;
    }
}
