using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

/// <summary>
/// Достижения (этап 2): условия Counter / EfficientGather / ConstructUsed, однократное выполнение,
/// пропуск Recycled, сохранение прогресса в MetaProgress, выдача наград через UnlockRegistry,
/// отложенные награды и пустой каталог. MetaProgress пишет во временную папку.
/// GameEventBus статический: подписчики сбрасываются до и после каждого теста, трекеры — Dispose.
/// </summary>
public class AchievementTrackerTests
{
    private TestHelpers.TempSaveFolder _saves;
    private readonly List<Object> _created = new List<Object>();
    private readonly List<AchievementTracker> _trackers = new List<AchievementTracker>();
    private readonly List<AchievementDefinition> _completed = new List<AchievementDefinition>();
    private readonly List<(UnlockKind Kind, string Id)> _unlocks = new List<(UnlockKind, string)>();

    private PlayerEnergy _energy;
    private UnlockConfig _config;

    [SetUp]
    public void SetUp()
    {
        ResetBus();
        _saves = new TestHelpers.TempSaveFolder();
        _energy = new PlayerEnergy(1000f);
        _config = ScriptableObject.CreateInstance<UnlockConfig>();
        _config.startingCommands = new List<string>();
        _config.startingConstructs = new List<string>();
        _config.unlockAll = false;
        _created.Add(_config);
        _completed.Clear();
        _unlocks.Clear();
    }

    [TearDown]
    public void TearDown()
    {
        foreach (AchievementTracker tracker in _trackers)
            tracker.Dispose();
        _trackers.Clear();
        ResetBus();

        foreach (Object obj in _created)
            if (obj != null) Object.DestroyImmediate(obj);
        _created.Clear();
        _saves.Dispose();
    }

    // ─── Заготовки ────────────────────────────────────────────

    /// <summary>Сброс подписчиков статической шины (тот же метод, что вызывается при входе в Play Mode).</summary>
    private static void ResetBus()
    {
        MethodInfo reset = typeof(GameEventBus).GetMethod("ResetStatics", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(reset, "GameEventBus.ResetStatics not found");
        reset.Invoke(null, null);
    }

    private static object BusField(string name) =>
        typeof(GameEventBus).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);

    private static void SetPrivate(object target, string field, object value)
    {
        FieldInfo info = target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(info, $"{target.GetType().Name}.{field} not found");
        info.SetValue(target, value);
    }

    private static AchievementReward Reward(AchievementRewardKind kind, string id) =>
        new AchievementReward { kind = kind, id = id };

    private AchievementDefinition Define(
        string id, GameEventType gameEvent, string filter, int amount,
        AchievementConditionType type = AchievementConditionType.Counter, float maxEnergy = 10f,
        params AchievementReward[] rewards)
    {
        AchievementDefinition definition = ScriptableObject.CreateInstance<AchievementDefinition>();
        _created.Add(definition);
        definition.name = id;
        SetPrivate(definition, "id", id);
        SetPrivate(definition, "title", id);
        SetPrivate(definition, "condition", new AchievementCondition
        {
            type = type, gameEvent = gameEvent, filter = filter, amount = amount, maxEnergy = maxEnergy
        });
        SetPrivate(definition, "rewards", new List<AchievementReward>(rewards));
        return definition;
    }

    private AchievementCatalog Catalog(params AchievementDefinition[] definitions)
    {
        AchievementCatalog catalog = ScriptableObject.CreateInstance<AchievementCatalog>();
        _created.Add(catalog);
        SetPrivate(catalog, "achievements", new List<AchievementDefinition>(definitions));
        return catalog;
    }

    private UnlockRegistry Registry(MetaProgress progress)
    {
        var registry = new UnlockRegistry(_config, progress);
        registry.OnUnlocked += RecordUnlock;
        return registry;
    }

    private AchievementTracker Tracker(AchievementCatalog catalog, MetaProgress progress, UnlockRegistry registry = null)
    {
        var tracker = new AchievementTracker(catalog, progress, registry ?? Registry(progress), _energy);
        tracker.OnAchievementCompleted += RecordCompleted;
        _trackers.Add(tracker);
        return tracker;
    }

    private AchievementTracker Tracker(params AchievementDefinition[] definitions) =>
        Tracker(Catalog(definitions), _saves.Progress());

    private void RecordCompleted(AchievementDefinition definition) => _completed.Add(definition);
    private void RecordUnlock(UnlockKind kind, string id) => _unlocks.Add((kind, id));

    private static void Publish(GameEventType type, string id = null, int amount = 1, string tag = null, bool recycled = false)
        => GameEventBus.Publish(new GameEvent(type, id, amount, tag, recycled));

    private static void FinishRun(params string[] constructs)
        => GameEventBus.PublishRunFinished(new CodeRunReport(true, true, 0f, new HashSet<string>(constructs)));

    // ─── Counter: фильтр ──────────────────────────────────────

    [Test]
    public void Counter_FilterMatchesIdIgnoringCase_Counts()
    {
        AchievementDefinition logs = Define("logs", GameEventType.Harvested, "LOG", 5);
        AchievementTracker tracker = Tracker(logs);

        Publish(GameEventType.Harvested, "log");
        Publish(GameEventType.Harvested, "Log", 2);

        Assert.AreEqual(3, tracker.GetProgress(logs).Current);
        Assert.AreEqual(5, tracker.GetProgress(logs).Target);
        Assert.IsFalse(tracker.GetProgress(logs).Completed);
    }

    [Test]
    public void Counter_FilterMatchesTagIgnoringCase_Counts()
    {
        AchievementDefinition fromTrees = Define("from_trees", GameEventType.Harvested, " Tree ", 5);
        AchievementTracker tracker = Tracker(fromTrees);

        Publish(GameEventType.Harvested, "log", 1, "tree");

        Assert.AreEqual(1, tracker.GetProgress(fromTrees).Current);
    }

    [Test]
    public void Counter_FilterOrTypeMismatch_NotCounted()
    {
        AchievementDefinition logs = Define("logs", GameEventType.Harvested, "log", 5);
        AchievementTracker tracker = Tracker(logs);

        Publish(GameEventType.Harvested, "stick", 1, "stick");
        Publish(GameEventType.ItemObtained, "log");
        Publish(GameEventType.Harvested, "logs");

        Assert.AreEqual(0, tracker.GetProgress(logs).Current);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Counter_EmptyFilter_CountsAnyEventOfType(string filter)
    {
        AchievementDefinition any = Define("any", GameEventType.Harvested, filter, 10);
        AchievementTracker tracker = Tracker(any);

        Publish(GameEventType.Harvested, "log");
        Publish(GameEventType.Harvested, "stick", 1, "stick");
        Publish(GameEventType.Harvested);
        Publish(GameEventType.Mined, "tree");

        Assert.AreEqual(3, tracker.GetProgress(any).Current);
    }

    // ─── Counter: порог и однократность ───────────────────────

    [Test]
    public void Counter_ThresholdReached_CompletedExactlyOnce()
    {
        AchievementDefinition haul = Define("haul", GameEventType.Harvested, "", 3);
        AchievementTracker tracker = Tracker(haul);

        Publish(GameEventType.Harvested, "log");
        Publish(GameEventType.Harvested, "log");
        Assert.IsFalse(tracker.IsCompleted(haul));
        CollectionAssert.IsEmpty(_completed);

        Publish(GameEventType.Harvested, "log");
        Assert.IsTrue(tracker.IsCompleted(haul));

        // Повторные события не выполняют достижение снова
        Publish(GameEventType.Harvested, "log", 5);
        Publish(GameEventType.Harvested, "log");

        CollectionAssert.AreEqual(new[] { haul }, _completed);
        CollectionAssert.AreEqual(new[] { haul }, tracker.CompletedThisScene);
        Assert.AreEqual(1, tracker.CompletedCount);
        AchievementProgress progress = tracker.GetProgress(haul);
        Assert.AreEqual(3, progress.Current);
        Assert.IsTrue(progress.Completed);
    }

    [Test]
    public void Counter_SingleEventWithLargeAmount_CompletesAtOnce()
    {
        AchievementDefinition haul = Define("haul", GameEventType.Harvested, "", 3);
        AchievementTracker tracker = Tracker(haul);

        Publish(GameEventType.Harvested, "stone", 3);

        Assert.IsTrue(tracker.IsCompleted(haul));
    }

    [Test]
    public void Counter_RecycledEvents_Skipped()
    {
        AchievementDefinition haul = Define("haul", GameEventType.Harvested, "", 1);
        AchievementTracker tracker = Tracker(haul);

        Publish(GameEventType.Harvested, "log", 5, "log", recycled: true);

        Assert.IsFalse(tracker.IsCompleted(haul));
        Assert.AreEqual(0, tracker.GetProgress(haul).Current);
    }

    [TestCase(0)]
    [TestCase(-2)]
    public void Counter_NonPositiveAmount_Ignored(int amount)
    {
        AchievementDefinition haul = Define("haul", GameEventType.Harvested, "", 1);
        AchievementTracker tracker = Tracker(haul);

        Publish(GameEventType.Harvested, "log", amount);

        Assert.AreEqual(0, tracker.GetProgress(haul).Current);
    }

    [Test]
    public void FirstUse_TargetIsOneRegardlessOfAmount()
    {
        AchievementDefinition first = Define("first_for", GameEventType.ConstructUsed, "for", 99,
            AchievementConditionType.FirstUse);
        AchievementTracker tracker = Tracker(first);
        Assert.AreEqual(1, tracker.GetProgress(first).Target);

        GameEventBus.PublishRunStarted();
        FinishRun(LuaConstructs.For);

        Assert.IsTrue(tracker.IsCompleted(first));
    }

    // ─── Сохранение прогресса ─────────────────────────────────

    [Test]
    public void Counter_Progress_SavedOnRunFinishedAndSurvivesRecreation()
    {
        AchievementDefinition logs = Define("logs", GameEventType.Harvested, "log", 5);
        AchievementCatalog catalog = Catalog(logs);
        AchievementTracker first = Tracker(catalog, _saves.Progress());

        GameEventBus.PublishRunStarted();
        Publish(GameEventType.Harvested, "log", 2);
        FinishRun();
        first.Dispose();

        MetaProgress reloaded = _saves.Progress();
        Assert.AreEqual(2, reloaded.GetAchievementProgress("logs"));

        AchievementTracker second = Tracker(catalog, reloaded);
        Assert.AreEqual(2, second.GetProgress(logs).Current);

        Publish(GameEventType.Harvested, "log", 3);
        Assert.IsTrue(second.IsCompleted(logs));
    }

    [Test]
    public void Counter_Progress_NotWrittenPerEventButOnDispose()
    {
        AchievementDefinition logs = Define("logs", GameEventType.Harvested, "log", 5);
        AchievementTracker tracker = Tracker(logs);

        Publish(GameEventType.Harvested, "log");
        Assert.AreEqual(0, _saves.Progress().GetAchievementProgress("logs"), "progress is not saved on every event");

        tracker.Dispose();
        Assert.AreEqual(1, _saves.Progress().GetAchievementProgress("logs"), "Dispose writes unsaved progress");
    }

    [Test]
    public void Counter_Progress_SavedAtDayEnd()
    {
        AchievementDefinition logs = Define("logs", GameEventType.Harvested, "log", 5);
        Tracker(logs);

        Publish(GameEventType.Harvested, "log", 2);
        Publish(GameEventType.DayPassed);

        Assert.AreEqual(2, _saves.Progress().GetAchievementProgress("logs"));
    }

    [Test]
    public void Complete_SavesImmediatelyAndRemovesProgressEntry()
    {
        AchievementDefinition logs = Define("logs", GameEventType.Harvested, "log", 3);
        MetaProgress progress = _saves.Progress();
        AchievementTracker tracker = Tracker(Catalog(logs), progress);

        Publish(GameEventType.Harvested, "log", 2);
        FinishRun();
        Assert.AreEqual(2, _saves.Progress().GetAchievementProgress("logs"));

        Publish(GameEventType.Harvested, "log");

        Assert.AreEqual(0, progress.GetAchievementProgress("logs"), "entry removed in memory");
        MetaProgress reloaded = _saves.Progress();
        Assert.IsTrue(reloaded.HasAchievement("logs"), "completion saved without waiting for the run end");
        Assert.AreEqual(0, reloaded.GetAchievementProgress("logs"), "entry removed in the file");
        StringAssert.DoesNotContain("\"achievementId\": \"logs\"", System.IO.File.ReadAllText(progress.FilePath));
    }

    [Test]
    public void Completed_InSave_NewTrackerDoesNotCompleteAgain()
    {
        AchievementDefinition haul = Define("haul", GameEventType.Harvested, "", 1);
        MetaProgress saved = _saves.Progress();
        saved.CompleteAchievement("haul");
        saved.Save();

        AchievementTracker tracker = Tracker(Catalog(haul), _saves.Progress());
        Publish(GameEventType.Harvested, "log");

        Assert.IsTrue(tracker.IsCompleted(haul));
        CollectionAssert.IsEmpty(_completed);
        CollectionAssert.IsEmpty(tracker.CompletedThisScene);
    }

    [Test]
    public void Dispose_UnsubscribesFromBus_EventsNoLongerCounted()
    {
        AchievementDefinition haul = Define("haul", GameEventType.Harvested, "", 2);
        MetaProgress progress = _saves.Progress();
        AchievementTracker tracker = Tracker(Catalog(haul), progress);

        tracker.Dispose();
        tracker.Dispose(); // повторный вызов безопасен

        Assert.IsNull(BusField(nameof(GameEventBus.OnGameEvent)));
        Assert.IsNull(BusField(nameof(GameEventBus.OnRunStarted)));
        Assert.IsNull(BusField(nameof(GameEventBus.OnRunFinished)));

        Publish(GameEventType.Harvested, "log", 5);
        Assert.AreEqual(0, progress.GetAchievementProgress("haul"));
        Assert.IsFalse(progress.HasAchievement("haul"));
    }

    // ─── EfficientGather ──────────────────────────────────────

    private AchievementDefinition EfficientLogger(int amount = 4, float maxEnergy = 50f) =>
        Define("efficient_logger", GameEventType.Harvested, "log", amount, AchievementConditionType.EfficientGather, maxEnergy);

    [Test]
    public void EfficientGather_WithinBudgetInOneRun_Completes()
    {
        AchievementDefinition logger = EfficientLogger();
        AchievementTracker tracker = Tracker(logger);

        GameEventBus.PublishRunStarted();
        _energy.TrySpend(30f);
        Publish(GameEventType.Harvested, "log", 2);
        Assert.AreEqual(2, tracker.GetProgress(logger).Current);
        _energy.TrySpend(19f);
        Publish(GameEventType.Harvested, "log", 2);

        Assert.IsTrue(tracker.IsCompleted(logger));
        CollectionAssert.AreEqual(new[] { logger }, _completed);
    }

    [Test]
    public void EfficientGather_SpentExactlyMax_Completes()
    {
        AchievementDefinition logger = EfficientLogger();
        AchievementTracker tracker = Tracker(logger);

        GameEventBus.PublishRunStarted();
        _energy.TrySpend(50f);
        Publish(GameEventType.Harvested, "log", 4);

        Assert.IsTrue(tracker.IsCompleted(logger));
    }

    [Test]
    public void EfficientGather_OverBudget_NotCompletedEvenWithMoreItems()
    {
        AchievementDefinition logger = EfficientLogger();
        AchievementTracker tracker = Tracker(logger);

        GameEventBus.PublishRunStarted();
        _energy.TrySpend(51f);
        Publish(GameEventType.Harvested, "log", 4);
        Publish(GameEventType.Harvested, "log", 4);

        Assert.IsFalse(tracker.IsCompleted(logger));
        CollectionAssert.IsEmpty(_completed);
    }

    [Test]
    public void EfficientGather_EnergySpentBeforeRun_NotCounted()
    {
        AchievementDefinition logger = EfficientLogger();
        AchievementTracker tracker = Tracker(logger);

        _energy.TrySpend(200f);
        GameEventBus.PublishRunStarted();
        _energy.TrySpend(10f);
        Publish(GameEventType.Harvested, "log", 4);

        Assert.IsTrue(tracker.IsCompleted(logger));
    }

    [Test]
    public void EfficientGather_CountResetsBetweenRuns()
    {
        AchievementDefinition logger = EfficientLogger();
        AchievementTracker tracker = Tracker(logger);

        GameEventBus.PublishRunStarted();
        Publish(GameEventType.Harvested, "log", 3);
        FinishRun();
        Assert.AreEqual(0, tracker.GetProgress(logger).Current, "no progress between runs");

        GameEventBus.PublishRunStarted();
        Publish(GameEventType.Harvested, "log");
        Assert.IsFalse(tracker.IsCompleted(logger), "3 + 1 from different runs must not add up");
        Assert.AreEqual(1, tracker.GetProgress(logger).Current);

        Publish(GameEventType.Harvested, "log", 3);
        Assert.IsTrue(tracker.IsCompleted(logger));
    }

    [Test]
    public void EfficientGather_OverBudgetRun_NextRunCanStillComplete()
    {
        AchievementDefinition logger = EfficientLogger();
        AchievementTracker tracker = Tracker(logger);

        GameEventBus.PublishRunStarted();
        _energy.TrySpend(80f);
        Publish(GameEventType.Harvested, "log", 4);
        FinishRun();
        Assert.IsFalse(tracker.IsCompleted(logger));

        GameEventBus.PublishRunStarted();
        _energy.TrySpend(5f);
        Publish(GameEventType.Harvested, "log", 4);
        Assert.IsTrue(tracker.IsCompleted(logger));
    }

    [Test]
    public void EfficientGather_OutsideCodeRun_Ignored()
    {
        AchievementDefinition logger = EfficientLogger();
        AchievementTracker tracker = Tracker(logger);

        Publish(GameEventType.Harvested, "log", 10);

        Assert.IsFalse(tracker.IsCompleted(logger));
        Assert.AreEqual(0, tracker.GetProgress(logger).Current);
    }

    [Test]
    public void EfficientGather_RecycledItems_Skipped()
    {
        AchievementDefinition logger = EfficientLogger();
        AchievementTracker tracker = Tracker(logger);

        GameEventBus.PublishRunStarted();
        Publish(GameEventType.Harvested, "log", 4, "log", recycled: true);

        Assert.IsFalse(tracker.IsCompleted(logger));
    }

    [Test]
    public void EfficientGather_NotFromThisRun_Ignored()
    {
        AchievementDefinition logger = EfficientLogger();
        AchievementTracker tracker = Tracker(logger);

        // Подбор лута, нарубленного в прошлом запуске, — не заслуга этого запуска
        GameEventBus.PublishRunStarted();
        GameEventBus.Publish(new GameEvent(GameEventType.Harvested, "log", 4, "log", false, fromThisRun: false));

        Assert.IsFalse(tracker.IsCompleted(logger));
        Assert.AreEqual(0, tracker.GetProgress(logger).Current);

        GameEventBus.Publish(new GameEvent(GameEventType.Harvested, "log", 4, "log", false, fromThisRun: true));
        Assert.IsTrue(tracker.IsCompleted(logger));
    }

    [Test]
    public void Counter_NotFromThisRun_Counted()
    {
        AchievementDefinition logs = Define("logs", GameEventType.Harvested, "log", 5);
        AchievementTracker tracker = Tracker(logs);

        GameEventBus.PublishRunStarted();
        GameEventBus.Publish(new GameEvent(GameEventType.Harvested, "log", 2, "log", false, fromThisRun: false));
        FinishRun();
        GameEventBus.Publish(new GameEvent(GameEventType.Harvested, "log", 1, "log", false, fromThisRun: false));

        Assert.AreEqual(3, tracker.GetProgress(logs).Current);
    }

    [Test]
    public void ActiveRunId_SetDuringRunOnly_NewIdEachRun()
    {
        Assert.AreEqual(0, GameEventBus.ActiveRunId);

        GameEventBus.PublishRunStarted();
        int first = GameEventBus.ActiveRunId;
        Assert.AreNotEqual(0, first);
        Assert.IsTrue(GameEventBus.IsFromActiveRun(first));
        FinishRun();
        Assert.AreEqual(0, GameEventBus.ActiveRunId);
        Assert.IsFalse(GameEventBus.IsFromActiveRun(first), "object from a finished run");

        GameEventBus.PublishRunStarted();
        Assert.AreNotEqual(first, GameEventBus.ActiveRunId);
        Assert.IsFalse(GameEventBus.IsFromActiveRun(first), "object from the previous run");
        Assert.IsFalse(GameEventBus.IsFromActiveRun(0), "object spawned outside any run");
        FinishRun();
    }

    [Test]
    public void EfficientGather_NotSavedToMetaProgress()
    {
        AchievementDefinition logger = EfficientLogger();
        MetaProgress progress = _saves.Progress();
        Tracker(Catalog(logger), progress);

        GameEventBus.PublishRunStarted();
        Publish(GameEventType.Harvested, "log", 2);

        Assert.AreEqual(0, progress.GetAchievementProgress("efficient_logger"));
    }

    // ─── ConstructUsed ────────────────────────────────────────

    [Test]
    public void ConstructUsed_CountedOncePerRun()
    {
        AchievementDefinition loops = Define("loop_lover", GameEventType.ConstructUsed, "for", 5);
        AchievementTracker tracker = Tracker(loops);

        // Три цикла for в одном коде — одна конструкция в отчёте запуска
        _config.startingConstructs = new List<string> { LuaConstructs.For };
        var guard = new LuaConstructGuard(new UnlockRegistry(_config, _saves.Progress()));
        var used = new HashSet<string>();
        Assert.IsNull(guard.Check("for i = 1, 2 do end\nfor j = 1, 2 do\n for k = 1, 2 do end\nend", null, "Main", used));

        GameEventBus.PublishRunStarted();
        GameEventBus.PublishRunFinished(new CodeRunReport(true, true, 3f, used));
        Assert.AreEqual(1, tracker.GetProgress(loops).Current);

        GameEventBus.PublishRunStarted();
        FinishRun(LuaConstructs.For, LuaConstructs.Local);
        Assert.AreEqual(2, tracker.GetProgress(loops).Current);
    }

    [Test]
    public void ConstructUsed_RunThatDidNotStart_NotCounted()
    {
        AchievementDefinition loops = Define("loop_lover", GameEventType.ConstructUsed, "for", 5);
        AchievementTracker tracker = Tracker(loops);

        GameEventBus.PublishRunStarted();
        GameEventBus.PublishRunFinished(new CodeRunReport(false, false, 0f, new HashSet<string> { LuaConstructs.For }));

        Assert.AreEqual(0, tracker.GetProgress(loops).Current);
    }

    [Test]
    public void ConstructUsed_FailedButStartedRun_Counted()
    {
        AchievementDefinition loops = Define("loop_lover", GameEventType.ConstructUsed, "for", 5);
        AchievementTracker tracker = Tracker(loops);

        GameEventBus.PublishRunStarted();
        GameEventBus.PublishRunFinished(new CodeRunReport(false, true, 1f, new HashSet<string> { LuaConstructs.For }));

        Assert.AreEqual(1, tracker.GetProgress(loops).Current);
    }

    [Test]
    public void ConstructUsed_ReachingThreshold_CompletesAndSaves()
    {
        AchievementDefinition loops = Define("loop_lover", GameEventType.ConstructUsed, "for", 2,
            AchievementConditionType.Counter, 10f, Reward(AchievementRewardKind.Construct, LuaConstructs.Function));
        MetaProgress progress = _saves.Progress();
        UnlockRegistry registry = Registry(progress);
        AchievementTracker tracker = Tracker(Catalog(loops), progress, registry);

        for (int run = 0; run < 2; run++)
        {
            GameEventBus.PublishRunStarted();
            FinishRun(LuaConstructs.For);
        }

        Assert.IsTrue(tracker.IsCompleted(loops));
        Assert.IsTrue(registry.IsConstructUnlocked(LuaConstructs.Function));
        Assert.IsTrue(_saves.Progress().IsConstructUnlocked(LuaConstructs.Function));
    }

    // ─── Награды ──────────────────────────────────────────────

    [Test]
    public void Rewards_AllKinds_GrantedThroughUnlockRegistry()
    {
        AchievementDefinition all = Define("all", GameEventType.Harvested, "", 1, AchievementConditionType.Counter, 10f,
            Reward(AchievementRewardKind.Command, "mine"),
            Reward(AchievementRewardKind.Construct, LuaConstructs.For),
            Reward(AchievementRewardKind.Recipe, "bed"),
            Reward(AchievementRewardKind.HubBuilding, "storage"),
            Reward(AchievementRewardKind.MapEvent, "maze"));
        MetaProgress progress = _saves.Progress();
        UnlockRegistry registry = Registry(progress);
        registry.SetKnownCommands(new[] { "mine", "move" });
        registry.SetKnownRecipes(new[] { "bed" });
        registry.SetKnownHubBuildings(new[] { "storage" });
        registry.SetKnownMapEvents(new[] { "maze" });
        Tracker(Catalog(all), progress, registry);

        Publish(GameEventType.Harvested, "log");

        Assert.IsTrue(registry.IsCommandUnlocked("mine"));
        Assert.IsTrue(registry.IsConstructUnlocked(LuaConstructs.For));
        Assert.IsTrue(registry.IsRecipeUnlocked("bed"));
        Assert.IsTrue(registry.IsHubBuildingUnlocked("storage"));
        Assert.IsTrue(registry.IsMapEventUnlocked("maze"));
        CollectionAssert.AreEqual(new[]
        {
            (UnlockKind.Command, "mine"), (UnlockKind.Construct, LuaConstructs.For), (UnlockKind.Recipe, "bed"),
            (UnlockKind.HubBuilding, "storage"), (UnlockKind.MapEvent, "maze")
        }, _unlocks);

        MetaProgress reloaded = _saves.Progress();
        Assert.IsTrue(reloaded.IsCommandUnlocked("mine"));
        Assert.IsTrue(reloaded.IsConstructUnlocked(LuaConstructs.For));
        Assert.IsTrue(reloaded.IsRecipeUnlocked("bed"));
        Assert.IsTrue(reloaded.IsHubBuildingUnlocked("storage"));
        Assert.IsTrue(reloaded.IsEventUnlocked("maze"));
    }

    [Test]
    public void Rewards_UnknownCommandAndRecipe_DeferredWithoutEventOrException()
    {
        AchievementDefinition future = Define("future", GameEventType.Harvested, "", 1, AchievementConditionType.Counter, 10f,
            Reward(AchievementRewardKind.Command, "attack"),
            Reward(AchievementRewardKind.Recipe, "sword"),
            Reward(AchievementRewardKind.Recipe, "cooked_meat"));
        MetaProgress progress = _saves.Progress();
        UnlockRegistry registry = Registry(progress);
        registry.SetKnownCommands(new[] { "mine" });
        registry.SetKnownRecipes(new[] { "bed" });
        AchievementTracker tracker = Tracker(Catalog(future), progress, registry);

        Assert.DoesNotThrow(() => Publish(GameEventType.Harvested, "log"));

        Assert.IsTrue(tracker.IsCompleted(future));
        CollectionAssert.AreEqual(new[] { future }, _completed);
        CollectionAssert.IsEmpty(_unlocks, "deferred unlocks raise no OnUnlocked");

        MetaProgress reloaded = _saves.Progress();
        Assert.IsTrue(reloaded.IsCommandUnlocked("attack"));
        Assert.IsTrue(reloaded.IsRecipeUnlocked("sword"));
        Assert.IsTrue(reloaded.IsRecipeUnlocked("cooked_meat"));

        // Когда команда/рецепт появятся в игре, они уже открыты
        registry.SetKnownCommands(new[] { "mine", "attack" });
        registry.SetKnownRecipes(new[] { "bed", "sword" });
        Assert.IsTrue(registry.IsCommandUnlocked("attack"));
        Assert.IsTrue(registry.IsRecipeUnlocked("sword"));
    }

    [Test]
    public void Rewards_OfCompletedAchievement_RegrantedAtStart()
    {
        AchievementDefinition axe = Define("first_axe", GameEventType.Crafted, "axe", 1, AchievementConditionType.Counter, 10f,
            Reward(AchievementRewardKind.Command, "mine"));
        MetaProgress saved = _saves.Progress();
        saved.CompleteAchievement("first_axe");
        saved.Save();

        MetaProgress progress = _saves.Progress();
        UnlockRegistry registry = Registry(progress);
        Assert.IsFalse(registry.IsCommandUnlocked("mine"));

        AchievementTracker tracker = Tracker(Catalog(axe), progress, registry);
        Assert.IsFalse(registry.IsCommandUnlocked("mine"), "constructor no longer grants rewards");

        tracker.GrantRewardsOfCompleted();

        Assert.IsTrue(registry.IsCommandUnlocked("mine"), "reward added after completion is granted at start");
        CollectionAssert.AreEqual(new[] { (UnlockKind.Command, "mine") }, _unlocks);
        CollectionAssert.IsEmpty(_completed);

        // Повторная выдача идемпотентна
        tracker.GrantRewardsOfCompleted();
        Assert.AreEqual(1, _unlocks.Count);
    }

    [Test]
    public void GrantRewardsOfCompleted_NotCompleted_GrantsNothing()
    {
        AchievementDefinition axe = Define("first_axe", GameEventType.Crafted, "axe", 1, AchievementConditionType.Counter, 10f,
            Reward(AchievementRewardKind.Command, "mine"));
        MetaProgress progress = _saves.Progress();
        UnlockRegistry registry = Registry(progress);
        AchievementTracker tracker = Tracker(Catalog(axe), progress, registry);

        tracker.GrantRewardsOfCompleted();

        Assert.IsFalse(registry.IsCommandUnlocked("mine"));
        CollectionAssert.IsEmpty(_unlocks);
    }

    [Test]
    public void Rewards_HubBuildingAndMapEventWithoutCatalog_DeferredWithoutEvent()
    {
        AchievementDefinition later = Define("later", GameEventType.Harvested, "", 1, AchievementConditionType.Counter, 10f,
            Reward(AchievementRewardKind.HubBuilding, "storage"),
            Reward(AchievementRewardKind.MapEvent, "maze"));
        MetaProgress progress = _saves.Progress();
        UnlockRegistry registry = Registry(progress); // SetKnownHubBuildings/SetKnownMapEvents не вызваны
        AchievementTracker tracker = Tracker(Catalog(later), progress, registry);

        Assert.DoesNotThrow(() => Publish(GameEventType.Harvested, "log"));

        Assert.IsTrue(tracker.IsCompleted(later));
        CollectionAssert.IsEmpty(_unlocks, "deferred unlocks raise no OnUnlocked");
        Assert.IsTrue(registry.IsHubBuildingUnlocked("storage"));
        Assert.IsTrue(registry.IsMapEventUnlocked("maze"));
        MetaProgress reloaded = _saves.Progress();
        Assert.IsTrue(reloaded.IsHubBuildingUnlocked("storage"));
        Assert.IsTrue(reloaded.IsEventUnlocked("maze"));

        // Каталог появился — открытие уже есть, повторное не новое и без события
        registry.SetKnownHubBuildings(new[] { "storage" });
        registry.SetKnownMapEvents(new[] { "maze" });
        Assert.IsFalse(registry.UnlockHubBuilding("storage"));
        Assert.IsFalse(registry.UnlockMapEvent("maze"));
        CollectionAssert.IsEmpty(_unlocks);
    }

    [Test]
    public void Rewards_HubBuildingUnknownToCatalog_DeferredWithoutEvent()
    {
        MetaProgress progress = _saves.Progress();
        UnlockRegistry registry = Registry(progress);
        registry.SetKnownHubBuildings(new[] { "storage" });
        registry.SetKnownMapEvents(new[] { "maze" });

        Assert.IsFalse(registry.UnlockHubBuilding("watchtower"));
        Assert.IsFalse(registry.UnlockMapEvent("code_task"));
        Assert.IsTrue(registry.UnlockHubBuilding("storage"));

        CollectionAssert.AreEqual(new[] { (UnlockKind.HubBuilding, "storage") }, _unlocks);
        Assert.IsTrue(registry.IsHubBuildingUnlocked("watchtower"));
        Assert.IsTrue(registry.IsMapEventUnlocked("code_task"));
    }

    // ─── Каталог ──────────────────────────────────────────────

    [Test]
    public void Constructor_NullCatalog_NoAchievementsNoExceptions()
    {
        LogAssert.Expect(LogType.Warning, new Regex("AchievementCatalog is not assigned"));
        AchievementTracker tracker = Tracker(null, _saves.Progress());

        Assert.AreEqual(0, tracker.Definitions.Count);
        Assert.AreEqual(0, tracker.CompletedCount);
        Assert.IsNull(tracker.Find("first_harvest"));
        Assert.DoesNotThrow(() =>
        {
            GameEventBus.PublishRunStarted();
            Publish(GameEventType.Harvested, "log");
            FinishRun(LuaConstructs.For);
            Publish(GameEventType.DayPassed);
        });
        AchievementProgress progress = tracker.GetProgress(null);
        Assert.IsFalse(progress.Completed);
    }

    [Test]
    public void Constructor_CatalogWithNullsAndDuplicateIds_SkipsThem()
    {
        AchievementDefinition first = Define("dup", GameEventType.Harvested, "", 1);
        AchievementDefinition second = Define("dup", GameEventType.Mined, "", 1);
        AchievementDefinition other = Define("other", GameEventType.Mined, "", 1);

        AchievementTracker tracker = Tracker(Catalog(first, null, second, other), _saves.Progress());

        CollectionAssert.AreEqual(new[] { first, other }, tracker.Definitions);
        Assert.AreSame(first, tracker.Find("dup"));
    }
}
