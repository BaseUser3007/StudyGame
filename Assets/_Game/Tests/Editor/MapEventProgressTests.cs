using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

/// <summary>
/// События на карте по числу выполненных достижений: пороги (MapEventProgress), открытие с командами
/// через UnlockRegistry, выдача из AchievementTracker при выполнении и при старте (старое сохранение),
/// однократный OnUnlocked и поведение без конфига. MetaProgress пишет во временную папку.
/// </summary>
public class MapEventProgressTests
{
    private TestHelpers.TempSaveFolder _saves;
    private readonly List<Object> _created = new List<Object>();
    private readonly List<AchievementTracker> _trackers = new List<AchievementTracker>();
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
        _config.mapEventThresholds = DesignThresholds();
        _config.unlockAll = false;
        _created.Add(_config);
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

    /// <summary>Пороги, как в DefaultUnlockConfig: code_task 6 (+ puzzle, solve), maze 11, plates 15.</summary>
    private static List<MapEventThreshold> DesignThresholds() => new List<MapEventThreshold>
    {
        new MapEventThreshold { eventId = PuzzleTypes.CodeTask, achievementsRequired = 6,
            commands = new List<string> { "puzzle", "solve" } },
        new MapEventThreshold { eventId = PuzzleTypes.Maze, achievementsRequired = 11 },
        new MapEventThreshold { eventId = PuzzleTypes.Plates, achievementsRequired = 15 }
    };

    /// <summary>count достижений «собрать a_i» без наград (Counter, 1 событие Harvested с Id = id).</summary>
    private AchievementCatalog Catalog(int count)
    {
        var definitions = new List<AchievementDefinition>();
        for (int i = 0; i < count; i++)
        {
            AchievementDefinition definition = ScriptableObject.CreateInstance<AchievementDefinition>();
            _created.Add(definition);
            string id = "a" + i;
            definition.name = id;
            SetPrivate(definition, "id", id);
            SetPrivate(definition, "title", id);
            SetPrivate(definition, "condition", new AchievementCondition
            {
                type = AchievementConditionType.Counter, gameEvent = GameEventType.Harvested, filter = id, amount = 1
            });
            SetPrivate(definition, "rewards", new List<AchievementReward>());
            definitions.Add(definition);
        }

        AchievementCatalog catalog = ScriptableObject.CreateInstance<AchievementCatalog>();
        _created.Add(catalog);
        SetPrivate(catalog, "achievements", definitions);
        return catalog;
    }

    private UnlockRegistry Registry(MetaProgress progress, UnlockConfig config)
    {
        var registry = new UnlockRegistry(config, progress);
        registry.SetKnownCommands(new[] { "puzzle", "solve", "mine" });
        registry.SetKnownMapEvents(PuzzleTypes.All);
        registry.OnUnlocked += RecordUnlock;
        return registry;
    }

    private UnlockRegistry Registry(MetaProgress progress) => Registry(progress, _config);

    private AchievementTracker Tracker(AchievementCatalog catalog, MetaProgress progress, UnlockRegistry registry)
    {
        var tracker = new AchievementTracker(catalog, progress, registry, _energy);
        _trackers.Add(tracker);
        return tracker;
    }

    private void RecordUnlock(UnlockKind kind, string id) => _unlocks.Add((kind, id));

    private static void Complete(int index) =>
        GameEventBus.Publish(new GameEvent(GameEventType.Harvested, "a" + index, 1, null, false));

    private static string[] Ids(IEnumerable<MapEventThreshold> thresholds) =>
        thresholds.Select(t => t.EventId).ToArray();

    // ─── Пороги (чистая логика) ───────────────────────────────

    [Test]
    public void GetDue_ByCompletedCount()
    {
        List<MapEventThreshold> thresholds = DesignThresholds();

        CollectionAssert.IsEmpty(MapEventProgress.GetDue(thresholds, 0));
        CollectionAssert.IsEmpty(MapEventProgress.GetDue(thresholds, 5));
        CollectionAssert.AreEqual(new[] { "code_task" }, Ids(MapEventProgress.GetDue(thresholds, 6)));
        CollectionAssert.AreEqual(new[] { "code_task" }, Ids(MapEventProgress.GetDue(thresholds, 10)));
        CollectionAssert.AreEqual(new[] { "code_task", "maze" }, Ids(MapEventProgress.GetDue(thresholds, 11)));
        CollectionAssert.AreEqual(new[] { "code_task", "maze" }, Ids(MapEventProgress.GetDue(thresholds, 14)));
        CollectionAssert.AreEqual(new[] { "code_task", "maze", "plates" }, Ids(MapEventProgress.GetDue(thresholds, 15)));
        CollectionAssert.AreEqual(new[] { "code_task", "maze", "plates" }, Ids(MapEventProgress.GetDue(thresholds, 18)));
    }

    [Test]
    public void GetNext_NearestNotReached_NullWhenAllReached()
    {
        List<MapEventThreshold> thresholds = DesignThresholds();

        Assert.AreEqual("code_task", MapEventProgress.GetNext(thresholds, 0).EventId);
        Assert.AreEqual("code_task", MapEventProgress.GetNext(thresholds, 5).EventId);
        Assert.AreEqual("maze", MapEventProgress.GetNext(thresholds, 6).EventId);
        Assert.AreEqual("plates", MapEventProgress.GetNext(thresholds, 11).EventId);
        Assert.IsNull(MapEventProgress.GetNext(thresholds, 15));
        Assert.IsNull(MapEventProgress.GetNext(null, 0));
    }

    [Test]
    public void Normalize_SortsSkipsEmptyAndKeepsLowestDuplicate()
    {
        var thresholds = new List<MapEventThreshold>
        {
            new MapEventThreshold { eventId = "maze", achievementsRequired = 11 },
            null,
            new MapEventThreshold { eventId = "  ", achievementsRequired = 1 },
            new MapEventThreshold { eventId = " code_task ", achievementsRequired = 6 },
            new MapEventThreshold { eventId = "maze", achievementsRequired = 3 }
        };

        List<MapEventThreshold> normalized = MapEventProgress.Normalize(thresholds);

        CollectionAssert.AreEqual(new[] { "maze", "code_task" }, Ids(normalized));
        Assert.AreEqual(3, normalized[0].achievementsRequired);
        CollectionAssert.IsEmpty(MapEventProgress.Normalize(null));
    }

    // ─── UnlockRegistry ───────────────────────────────────────

    [Test]
    public void Registry_Five_UnlocksNothing()
    {
        MetaProgress progress = _saves.Progress();
        UnlockRegistry registry = Registry(progress);

        Assert.AreEqual(0, registry.UnlockMapEventsForAchievements(5));

        CollectionAssert.IsEmpty(_unlocks);
        foreach (string id in PuzzleTypes.All)
            Assert.IsFalse(registry.IsMapEventUnlocked(id), id);
        Assert.IsFalse(registry.IsCommandUnlocked("puzzle"));
        Assert.IsFalse(registry.IsCommandUnlocked("solve"));
    }

    [Test]
    public void Registry_Six_UnlocksCodeTaskWithItsCommands()
    {
        MetaProgress progress = _saves.Progress();
        UnlockRegistry registry = Registry(progress);

        Assert.AreEqual(1, registry.UnlockMapEventsForAchievements(6));

        CollectionAssert.AreEqual(new[]
        {
            (UnlockKind.MapEvent, "code_task"), (UnlockKind.Command, "puzzle"), (UnlockKind.Command, "solve")
        }, _unlocks);
        Assert.IsTrue(registry.IsMapEventUnlocked("code_task"));
        Assert.IsFalse(registry.IsMapEventUnlocked("maze"));
        Assert.IsFalse(registry.IsMapEventUnlocked("plates"));

        MetaProgress reloaded = _saves.Progress();
        Assert.IsTrue(reloaded.IsEventUnlocked("code_task"), "saved immediately");
        Assert.IsTrue(reloaded.IsCommandUnlocked("puzzle"));
        Assert.IsTrue(reloaded.IsCommandUnlocked("solve"));
    }

    [Test]
    public void Registry_ElevenAndFifteen_UnlockMazeThenPlatesWithoutCommands()
    {
        MetaProgress progress = _saves.Progress();
        UnlockRegistry registry = Registry(progress);
        registry.UnlockMapEventsForAchievements(6);
        _unlocks.Clear();

        Assert.AreEqual(1, registry.UnlockMapEventsForAchievements(11));
        CollectionAssert.AreEqual(new[] { (UnlockKind.MapEvent, "maze") }, _unlocks);
        Assert.IsFalse(registry.IsMapEventUnlocked("plates"));

        _unlocks.Clear();
        Assert.AreEqual(1, registry.UnlockMapEventsForAchievements(15));
        CollectionAssert.AreEqual(new[] { (UnlockKind.MapEvent, "plates") }, _unlocks);
    }

    [Test]
    public void Registry_Repeated_FiresOnUnlockedOnce()
    {
        MetaProgress progress = _saves.Progress();
        UnlockRegistry registry = Registry(progress);

        registry.UnlockMapEventsForAchievements(15);
        int first = _unlocks.Count;
        Assert.AreEqual(5, first, "3 events + 2 commands");

        Assert.AreEqual(0, registry.UnlockMapEventsForAchievements(15));
        Assert.AreEqual(0, registry.UnlockMapEventsForAchievements(18));
        Assert.AreEqual(first, _unlocks.Count);
    }

    [Test]
    public void Registry_NoConfig_ThresholdsEmptyNothingSaved()
    {
        LogAssert.Expect(LogType.Warning, new Regex("UnlockConfig is not assigned"));
        MetaProgress progress = _saves.Progress();
        UnlockRegistry registry = Registry(progress, null);

        Assert.AreEqual(0, registry.MapEventThresholds.Count);
        Assert.AreEqual(0, registry.UnlockMapEventsForAchievements(18));
        CollectionAssert.IsEmpty(_unlocks);
        Assert.IsFalse(_saves.Progress().IsEventUnlocked("code_task"), "events only via rewards / unlockAll");
        Assert.IsTrue(registry.IsMapEventUnlocked("code_task"), "no config = unlockAll");
    }

    [Test]
    public void Registry_EmptyThresholds_EventsOnlyViaRewards()
    {
        _config.mapEventThresholds = new List<MapEventThreshold>();
        MetaProgress progress = _saves.Progress();
        UnlockRegistry registry = Registry(progress);

        Assert.AreEqual(0, registry.UnlockMapEventsForAchievements(18));
        CollectionAssert.IsEmpty(_unlocks);
        Assert.IsFalse(registry.IsMapEventUnlocked("code_task"));

        // Награда достижения по-прежнему открывает событие
        Assert.IsTrue(registry.UnlockMapEvent("maze"));
        Assert.IsTrue(registry.IsMapEventUnlocked("maze"));
    }

    // ─── AchievementTracker ───────────────────────────────────

    [Test]
    public void Tracker_SixthCompletion_UnlocksCodeTask()
    {
        MetaProgress progress = _saves.Progress();
        UnlockRegistry registry = Registry(progress);
        AchievementTracker tracker = Tracker(Catalog(18), progress, registry);

        for (int i = 0; i < 5; i++) Complete(i);
        Assert.AreEqual(5, tracker.CompletedCount);
        CollectionAssert.IsEmpty(_unlocks);
        Assert.IsTrue(tracker.TryGetNextMapEvent(out string next, out int required));
        Assert.AreEqual("code_task", next);
        Assert.AreEqual(6, required);

        Complete(5);

        CollectionAssert.AreEqual(new[]
        {
            (UnlockKind.MapEvent, "code_task"), (UnlockKind.Command, "puzzle"), (UnlockKind.Command, "solve")
        }, _unlocks);
        Assert.IsTrue(tracker.TryGetNextMapEvent(out next, out required));
        Assert.AreEqual("maze", next);
        Assert.AreEqual(11, required);

        for (int i = 6; i < 15; i++) Complete(i);
        Assert.IsTrue(registry.IsMapEventUnlocked("maze"));
        Assert.IsTrue(registry.IsMapEventUnlocked("plates"));
        Assert.IsFalse(tracker.TryGetNextMapEvent(out _, out _));
        Assert.AreEqual(5, _unlocks.Count, "every event and command fired once");
    }

    [Test]
    public void Tracker_OldSaveWithElevenCompleted_GrantedOnceAtStart()
    {
        MetaProgress saved = _saves.Progress();
        for (int i = 0; i < 11; i++) saved.CompleteAchievement("a" + i);
        saved.Save();

        MetaProgress progress = _saves.Progress();
        UnlockRegistry registry = Registry(progress);
        AchievementTracker tracker = Tracker(Catalog(18), progress, registry);
        CollectionAssert.IsEmpty(_unlocks, "constructor grants nothing");

        tracker.GrantRewardsOfCompleted();

        CollectionAssert.AreEqual(new[]
        {
            (UnlockKind.MapEvent, "code_task"), (UnlockKind.Command, "puzzle"), (UnlockKind.Command, "solve"),
            (UnlockKind.MapEvent, "maze")
        }, _unlocks);
        Assert.IsFalse(registry.IsMapEventUnlocked("plates"));

        // Повторная выдача (следующая загрузка сцены) — без новых событий
        tracker.GrantRewardsOfCompleted();
        UnlockRegistry next = Registry(_saves.Progress());
        Tracker(Catalog(18), _saves.Progress(), next).GrantRewardsOfCompleted();
        Assert.AreEqual(4, _unlocks.Count);
        Assert.IsTrue(_saves.Progress().IsEventUnlocked("maze"));
    }

    [Test]
    public void Tracker_NoThresholds_NoMapEventLine()
    {
        _config.mapEventThresholds = new List<MapEventThreshold>();
        MetaProgress progress = _saves.Progress();
        AchievementTracker tracker = Tracker(Catalog(6), progress, Registry(progress));

        for (int i = 0; i < 6; i++) Complete(i);

        CollectionAssert.IsEmpty(_unlocks);
        Assert.IsFalse(tracker.HasMapEventThresholds);
        Assert.IsNull(AchievementText.FormatMapEvents(tracker));
    }

    [Test]
    public void FormatMapEvents_NextAndAllUnlocked()
    {
        GameLanguage language = Loc.Language;
        Loc.SetLanguage(GameLanguage.English);
        try
        {
            MetaProgress progress = _saves.Progress();
            AchievementTracker tracker = Tracker(Catalog(18), progress, Registry(progress));

            StringAssert.Contains("0/18", AchievementText.FormatMapEvents(tracker));
            StringAssert.Contains("Obelisks", AchievementText.FormatMapEvents(tracker));

            for (int i = 0; i < 15; i++) Complete(i);
            Assert.AreEqual("Achievements: 15/18 · all map events unlocked", AchievementText.FormatMapEvents(tracker));
        }
        finally
        {
            Loc.SetLanguage(language);
        }
    }
}
