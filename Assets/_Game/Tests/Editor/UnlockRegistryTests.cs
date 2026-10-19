using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Открытия команд и конструкций: стартовый набор, unlockAll, сохранение в MetaProgress, событие OnUnlocked.
/// MetaProgress пишет во временную папку — сохранение игрока не трогаем.
/// </summary>
public class UnlockRegistryTests
{
    private TestHelpers.TempSaveFolder _saves;
    private readonly List<Object> _created = new List<Object>();
    private readonly List<(UnlockKind Kind, string Id)> _events = new List<(UnlockKind, string)>();

    [SetUp]
    public void SetUp()
    {
        _saves = new TestHelpers.TempSaveFolder();
        _events.Clear();
    }

    [TearDown]
    public void TearDown()
    {
        foreach (Object obj in _created)
            if (obj != null) Object.DestroyImmediate(obj);
        _created.Clear();
        _saves.Dispose();
    }

    private UnlockConfig Config(string[] commands, string[] constructs, bool unlockAll = false)
    {
        UnlockConfig config = ScriptableObject.CreateInstance<UnlockConfig>();
        _created.Add(config);
        config.startingCommands = new List<string>(commands);
        config.startingConstructs = new List<string>(constructs);
        config.unlockAll = unlockAll;
        return config;
    }

    private UnlockRegistry Registry(UnlockConfig config, MetaProgress progress = null)
    {
        var registry = new UnlockRegistry(config, progress ?? _saves.Progress());
        registry.OnUnlocked += RecordUnlock;
        return registry;
    }

    private void RecordUnlock(UnlockKind kind, string id) => _events.Add((kind, id));

    // ─── Стартовый набор ──────────────────────────────────────

    [Test]
    public void Constructor_StartingSet_OnlyConfigEntriesUnlocked()
    {
        UnlockRegistry registry = Registry(Config(new[] { "move" }, new[] { LuaConstructs.Local }));

        Assert.IsFalse(registry.UnlockAll);
        Assert.IsTrue(registry.IsCommandUnlocked("move"));
        Assert.IsFalse(registry.IsCommandUnlocked("mine"));
        Assert.IsTrue(registry.IsConstructUnlocked(LuaConstructs.Local));
        Assert.IsFalse(registry.IsConstructUnlocked(LuaConstructs.For));
        Assert.IsFalse(registry.IsConstructUnlocked(LuaConstructs.Classes));
    }

    [Test]
    public void Constructor_NamesWithSpacesAndBlankRows_TrimmedAndSkipped()
    {
        UnlockRegistry registry = Registry(Config(new[] { "  move ", "", "   ", null }, new[] { " for " }));

        Assert.IsTrue(registry.IsCommandUnlocked("move"));
        Assert.IsTrue(registry.IsConstructUnlocked(LuaConstructs.For));
        CollectionAssert.AreEquivalent(new[] { "move" }, registry.StartingCommands);
    }

    [Test]
    public void IsCommandUnlocked_IsCaseSensitive()
    {
        // Имена команд в Lua регистрозависимы — "Move" это другая (несуществующая) команда
        UnlockRegistry registry = Registry(Config(new[] { "move" }, new string[0]));
        Assert.IsFalse(registry.IsCommandUnlocked("Move"));
    }

    [Test]
    public void IsUnlocked_NullOrEmpty_False()
    {
        UnlockRegistry registry = Registry(Config(new string[0], new string[0], unlockAll: true));

        Assert.IsFalse(registry.IsCommandUnlocked(null));
        Assert.IsFalse(registry.IsCommandUnlocked(""));
        Assert.IsFalse(registry.IsConstructUnlocked(null));
        Assert.IsFalse(registry.IsConstructUnlocked(""));
    }

    [Test]
    public void Constructor_DefaultConfigValues_LocalAndIfOpenLoopsLocked()
    {
        UnlockConfig config = ScriptableObject.CreateInstance<UnlockConfig>();
        _created.Add(config);
        UnlockRegistry registry = Registry(config);

        Assert.IsTrue(registry.IsConstructUnlocked(LuaConstructs.Local));
        Assert.IsTrue(registry.IsConstructUnlocked(LuaConstructs.If));
        Assert.IsFalse(registry.IsConstructUnlocked(LuaConstructs.For));
        Assert.IsFalse(registry.IsConstructUnlocked(LuaConstructs.While));
        Assert.IsTrue(registry.IsCommandUnlocked("obtain"));
    }

    [Test]
    public void Constructor_UnknownStartingConstruct_LogsWarning()
    {
        LogAssert.Expect(LogType.Warning, new Regex("unknown construct id 'forr'"));
        Registry(Config(new string[0], new[] { "forr" }));
    }

    [Test]
    public void ProjectUnlockConfig_StartingConstructs_AreKnownIds()
    {
        UnlockConfig config = TestHelpers.Load<UnlockConfig>("Assets/_Game/ScriptableObjects/DefaultUnlockConfig.asset");
        foreach (string id in config.startingConstructs)
            Assert.IsTrue(LuaConstructs.IsKnown(id?.Trim()), $"unknown construct id '{id}' in DefaultUnlockConfig");
    }

    // ─── unlockAll и отсутствие конфига ───────────────────────

    [Test]
    public void Constructor_UnlockAll_EverythingOpenAndSaveUntouched()
    {
        UnlockRegistry registry = Registry(Config(new string[0], new string[0], unlockAll: true));

        Assert.IsTrue(registry.UnlockAll);
        Assert.IsTrue(registry.IsCommandUnlocked("mine"));
        Assert.IsTrue(registry.IsCommandUnlocked("anyFutureCommand"));
        foreach (LuaConstructs.Info info in LuaConstructs.All)
            Assert.IsTrue(registry.IsConstructUnlocked(info.Id), info.Id);

        Assert.IsFalse(File.Exists(_saves.PathOf("meta_progress.json")), "unlockAll must not write the save");
    }

    [Test]
    public void Constructor_NullConfig_EverythingOpenWithWarning()
    {
        LogAssert.Expect(LogType.Warning, new Regex("UnlockConfig is not assigned"));

        UnlockRegistry registry = Registry(null);

        Assert.IsTrue(registry.UnlockAll);
        Assert.IsTrue(registry.IsCommandUnlocked("move"));
        Assert.IsTrue(registry.IsConstructUnlocked(LuaConstructs.Function));
        Assert.IsTrue(registry.IsConstructUnlocked(LuaConstructs.Classes));
        Assert.AreEqual(0, registry.StartingCommands.Count);
    }

    // ─── UnlockCommand ────────────────────────────────────────

    [Test]
    public void UnlockCommand_FirstTime_TrueEventOnceAndSaved()
    {
        UnlockRegistry registry = Registry(Config(new string[0], new string[0]));

        Assert.IsTrue(registry.UnlockCommand("mine"));
        Assert.IsFalse(registry.UnlockCommand("mine"), "second unlock is not new");

        Assert.IsTrue(registry.IsCommandUnlocked("mine"));
        CollectionAssert.AreEqual(new[] { (UnlockKind.Command, "mine") }, _events);

        // Новый прогресс из того же файла видит открытие
        Assert.IsTrue(_saves.Progress().IsCommandUnlocked("mine"));
    }

    [Test]
    public void UnlockCommand_StartingCommand_FalseNoEvent()
    {
        UnlockRegistry registry = Registry(Config(new[] { "move" }, new string[0]));

        Assert.IsFalse(registry.UnlockCommand("move"));
        CollectionAssert.IsEmpty(_events);
    }

    [Test]
    public void UnlockCommand_AlreadyInSave_FalseNoEvent()
    {
        MetaProgress saved = _saves.Progress();
        saved.UnlockCommand("mine");
        Assert.IsTrue(saved.Save());

        UnlockRegistry registry = Registry(Config(new string[0], new string[0]));

        Assert.IsTrue(registry.IsCommandUnlocked("mine"), "unlock loaded from the save file");
        Assert.IsFalse(registry.UnlockCommand("mine"));
        CollectionAssert.IsEmpty(_events);
    }

    [Test]
    public void UnlockCommand_NullOrEmpty_FalseNoEvent()
    {
        UnlockRegistry registry = Registry(Config(new string[0], new string[0]));

        Assert.IsFalse(registry.UnlockCommand(null));
        Assert.IsFalse(registry.UnlockCommand(""));
        CollectionAssert.IsEmpty(_events);
    }

    [Test]
    public void UnlockCommand_UnderUnlockAll_StillSavedAndEventFiresOnce()
    {
        UnlockRegistry registry = Registry(Config(new string[0], new string[0], unlockAll: true));

        Assert.IsTrue(registry.UnlockCommand("mine"));
        Assert.IsFalse(registry.UnlockCommand("mine"));
        Assert.AreEqual(1, _events.Count);
        Assert.IsTrue(_saves.Progress().IsCommandUnlocked("mine"));
    }

    // ─── UnlockConstruct ──────────────────────────────────────

    [Test]
    public void UnlockConstruct_FirstTime_TrueEventOnceAndSaved()
    {
        UnlockRegistry registry = Registry(Config(new string[0], new[] { LuaConstructs.Local }));

        Assert.IsFalse(registry.IsConstructUnlocked(LuaConstructs.For));
        Assert.IsTrue(registry.UnlockConstruct(LuaConstructs.For));
        Assert.IsFalse(registry.UnlockConstruct(LuaConstructs.For));

        Assert.IsTrue(registry.IsConstructUnlocked(LuaConstructs.For));
        CollectionAssert.AreEqual(new[] { (UnlockKind.Construct, LuaConstructs.For) }, _events);
        Assert.IsTrue(_saves.Progress().IsConstructUnlocked(LuaConstructs.For));
    }

    [Test]
    public void UnlockConstruct_StartingConstruct_FalseNoEvent()
    {
        UnlockRegistry registry = Registry(Config(new string[0], new[] { LuaConstructs.Local }));

        Assert.IsFalse(registry.UnlockConstruct(LuaConstructs.Local));
        CollectionAssert.IsEmpty(_events);
    }

    [Test]
    public void UnlockConstruct_UnknownId_FalseNoEventNotSaved()
    {
        UnlockRegistry registry = Registry(Config(new string[0], new string[0]));
        LogAssert.Expect(LogType.Warning, new Regex("unknown construct id 'goto'"));

        Assert.IsFalse(registry.UnlockConstruct("goto"));

        Assert.IsFalse(registry.IsConstructUnlocked("goto"));
        CollectionAssert.IsEmpty(_events);
        Assert.IsFalse(File.Exists(_saves.PathOf("meta_progress.json")));
    }

    [Test]
    public void UnlockConstruct_KeywordInsteadOfId_False()
    {
        // `else` — ключевое слово конструкции if, а не id: открывать нужно "if"
        UnlockRegistry registry = Registry(Config(new string[0], new string[0]));
        LogAssert.Expect(LogType.Warning, new Regex("unknown construct id 'else'"));

        Assert.IsFalse(registry.UnlockConstruct("else"));
        Assert.IsFalse(registry.IsConstructUnlocked(LuaConstructs.If));
    }

    [Test]
    public void UnlockConstruct_NullOrEmpty_False()
    {
        UnlockRegistry registry = Registry(Config(new string[0], new string[0]));

        Assert.IsFalse(registry.UnlockConstruct(null));
        Assert.IsFalse(registry.UnlockConstruct(""));
        CollectionAssert.IsEmpty(_events);
    }

    [Test]
    public void Unlocks_SurviveReload_NewRegistrySeesThem()
    {
        UnlockConfig config = Config(new string[0], new string[0]);
        UnlockRegistry first = Registry(config);
        first.UnlockCommand("mine");
        first.UnlockConstruct(LuaConstructs.While);
        first.UnlockConstruct(LuaConstructs.Classes);

        UnlockRegistry second = Registry(config, _saves.Progress());

        Assert.IsTrue(second.IsCommandUnlocked("mine"));
        Assert.IsTrue(second.IsConstructUnlocked(LuaConstructs.While));
        Assert.IsTrue(second.IsConstructUnlocked(LuaConstructs.Classes));
        Assert.IsFalse(second.IsConstructUnlocked(LuaConstructs.For));
    }

    // ─── Рецепты ──────────────────────────────────────────────

    private CraftRecipe Recipe(string resultId, bool locked)
    {
        MaterialResource result = TestHelpers.Material(resultId);
        _created.Add(result);
        return new CraftRecipe { result = result, locked = locked };
    }

    [Test]
    public void IsRecipeAvailable_NotLockedRecipe_AlwaysAvailable()
    {
        UnlockRegistry registry = Registry(Config(new string[0], new string[0]));

        Assert.IsTrue(registry.IsRecipeAvailable(Recipe("axe", locked: false)));
        Assert.IsFalse(registry.IsRecipeUnlocked("axe"), "available without being unlocked");
    }

    [Test]
    public void IsRecipeAvailable_LockedRecipe_AvailableOnlyAfterUnlock()
    {
        UnlockRegistry registry = Registry(Config(new string[0], new string[0]));
        registry.SetKnownRecipes(new[] { "bed", "axe" });
        CraftRecipe bed = Recipe("bed", locked: true);

        Assert.IsFalse(registry.IsRecipeAvailable(bed));

        Assert.IsTrue(registry.UnlockRecipe("bed"));
        Assert.IsFalse(registry.UnlockRecipe("bed"), "second unlock is not new");

        Assert.IsTrue(registry.IsRecipeAvailable(bed));
        CollectionAssert.AreEqual(new[] { (UnlockKind.Recipe, "bed") }, _events);
        Assert.IsTrue(_saves.Progress().IsRecipeUnlocked("bed"), "saved immediately");
    }

    [Test]
    public void IsRecipeAvailable_LockedRecipeUnlockedInSave_AvailableAfterReload()
    {
        UnlockConfig config = Config(new string[0], new string[0]);
        Registry(config).UnlockRecipe("bed");

        UnlockRegistry second = Registry(config, _saves.Progress());

        Assert.IsTrue(second.IsRecipeAvailable(Recipe("bed", locked: true)));
        Assert.IsFalse(second.IsRecipeAvailable(Recipe("pickaxe", locked: true)));
    }

    [Test]
    public void IsRecipeAvailable_UnlockAll_LockedRecipeAvailable()
    {
        UnlockRegistry registry = Registry(Config(new string[0], new string[0], unlockAll: true));
        Assert.IsTrue(registry.IsRecipeAvailable(Recipe("bed", locked: true)));
    }

    [Test]
    public void IsRecipeAvailable_NullRecipe_False()
    {
        UnlockRegistry registry = Registry(Config(new string[0], new string[0]));
        Assert.IsFalse(registry.IsRecipeAvailable(null));
    }

    [Test]
    public void UnlockRecipe_UnknownKey_DeferredSavedWithoutEvent()
    {
        UnlockRegistry registry = Registry(Config(new string[0], new string[0]));
        registry.SetKnownRecipes(new[] { "bed" });

        Assert.IsFalse(registry.UnlockRecipe("sword"));

        CollectionAssert.IsEmpty(_events);
        Assert.IsTrue(_saves.Progress().IsRecipeUnlocked("sword"), "deferred unlock is saved");

        // Рецепт появился в книге — он уже открыт, повторное открытие не новое
        registry.SetKnownRecipes(new[] { "bed", "sword" });
        Assert.IsTrue(registry.IsRecipeAvailable(Recipe("sword", locked: true)));
        Assert.IsFalse(registry.UnlockRecipe("sword"));
        CollectionAssert.IsEmpty(_events);
    }

    [Test]
    public void UnlockRecipe_NullOrEmpty_FalseNoEvent()
    {
        UnlockRegistry registry = Registry(Config(new string[0], new string[0]));

        Assert.IsFalse(registry.UnlockRecipe(null));
        Assert.IsFalse(registry.UnlockRecipe(""));
        CollectionAssert.IsEmpty(_events);
    }

    // ─── LuaConstructs ────────────────────────────────────────

    [Test]
    public void LuaConstructs_AllIdsKnownAndUnique()
    {
        var ids = new HashSet<string>();
        foreach (LuaConstructs.Info info in LuaConstructs.All)
        {
            Assert.IsTrue(ids.Add(info.Id), $"duplicate construct id '{info.Id}'");
            Assert.IsTrue(LuaConstructs.IsKnown(info.Id));
            Assert.IsFalse(string.IsNullOrEmpty(info.DisplayName), info.Id);
        }

        Assert.IsFalse(LuaConstructs.IsKnown(null));
        Assert.IsFalse(LuaConstructs.IsKnown("goto"));
    }
}
