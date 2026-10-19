using System.IO;
using NUnit.Framework;

/// <summary>
/// MetaProgress: загрузка сохранений старого формата (без полей этапа 2), прогресс достижений,
/// круговая запись. Файлы — во временной папке.
/// </summary>
public class MetaProgressTests
{
    private TestHelpers.TempSaveFolder _saves;

    [SetUp]
    public void SetUp() => _saves = new TestHelpers.TempSaveFolder();

    [TearDown]
    public void TearDown() => _saves.Dispose();

    // Сохранение этапа 1: нет unlockedRecipes, unlockedHubBuildings, achievementProgress
    private const string Stage1Json = @"{
    ""unlockedCommands"": [""mine""],
    ""unlockedConstructs"": [""for""],
    ""achievements"": [],
    ""unlockedEvents"": [],
    ""tutorialFlags"": [],
    ""materials"": [{ ""itemId"": ""log"", ""displayName"": ""Log"", ""count"": 4 }],
    ""hubBuildings"": [],
    ""hintsEnabled"": false,
    ""runsFinished"": 2,
    ""defeats"": 1
}";

    private MetaProgress LoadJson(string json)
    {
        File.WriteAllText(_saves.PathOf("meta_progress.json"), json);
        return _saves.Progress();
    }

    [Test]
    public void Load_Stage1JsonWithoutNewFields_KeepsOldDataAndNewListsWork()
    {
        MetaProgress progress = LoadJson(Stage1Json);

        // Старые данные на месте
        Assert.IsTrue(progress.IsCommandUnlocked("mine"));
        Assert.IsTrue(progress.IsConstructUnlocked("for"));
        Assert.AreEqual(4, progress.GetMaterialCount("log"));
        Assert.IsFalse(progress.HintsEnabled);
        Assert.AreEqual(2, progress.RunsFinished);

        // Новые списки не null: чтение и запись без исключений
        Assert.DoesNotThrow(() =>
        {
            Assert.IsFalse(progress.IsRecipeUnlocked("bed"));
            Assert.IsFalse(progress.IsHubBuildingUnlocked("storage"));
            Assert.AreEqual(0, progress.GetAchievementProgress("logs"));
            Assert.IsTrue(progress.UnlockRecipe("bed"));
            Assert.IsTrue(progress.UnlockHubBuilding("storage"));
            progress.SetAchievementProgress("logs", 3);
            Assert.IsTrue(progress.CompleteAchievement("haul"));
        });
        Assert.IsTrue(progress.Save());

        MetaProgress reloaded = _saves.Progress();
        Assert.IsTrue(reloaded.IsRecipeUnlocked("bed"));
        Assert.IsTrue(reloaded.IsHubBuildingUnlocked("storage"));
        Assert.AreEqual(3, reloaded.GetAchievementProgress("logs"));
        Assert.IsTrue(reloaded.HasAchievement("haul"));
        Assert.IsTrue(reloaded.IsCommandUnlocked("mine"));
    }

    [Test]
    public void Load_EmptyJsonObject_AllListsUsable()
    {
        MetaProgress progress = LoadJson("{}");

        Assert.DoesNotThrow(() =>
        {
            progress.UnlockCommand("mine");
            progress.UnlockConstruct("for");
            progress.UnlockRecipe("bed");
            progress.UnlockHubBuilding("storage");
            progress.UnlockEvent("maze");
            progress.SetTutorialFlag("intro");
            progress.SetAchievementProgress("logs", 1);
            progress.CompleteAchievement("haul");
        });
        Assert.AreEqual(0, progress.Materials.Count);
        Assert.AreEqual(0, progress.HubBuildings.Count);
        Assert.AreEqual(1, progress.CompletedAchievements.Count);
    }

    [Test]
    public void Load_JunkAchievementProgressEntries_Removed()
    {
        MetaProgress progress = LoadJson(@"{ ""achievementProgress"": [
            { ""achievementId"": """", ""value"": 3 },
            { ""achievementId"": ""zero"", ""value"": 0 },
            { ""achievementId"": ""negative"", ""value"": -1 },
            { ""achievementId"": ""logs"", ""value"": 2 } ] }");

        Assert.AreEqual(2, progress.GetAchievementProgress("logs"));
        Assert.AreEqual(0, progress.GetAchievementProgress("zero"));
        Assert.AreEqual(0, progress.GetAchievementProgress("negative"));
    }

    [Test]
    public void SetAchievementProgress_ZeroOrNegative_RemovesEntry()
    {
        MetaProgress progress = _saves.Progress();
        progress.SetAchievementProgress("logs", 2);
        progress.SetAchievementProgress("logs", 0);
        progress.Save();

        Assert.AreEqual(0, _saves.Progress().GetAchievementProgress("logs"));
        StringAssert.DoesNotContain("logs", File.ReadAllText(progress.FilePath));
    }

    [Test]
    public void CompleteAchievement_SecondTime_FalseAndEntryRemoved()
    {
        MetaProgress progress = _saves.Progress();
        progress.SetAchievementProgress("logs", 4);

        Assert.IsTrue(progress.CompleteAchievement("logs"));
        Assert.IsFalse(progress.CompleteAchievement("logs"));
        Assert.AreEqual(0, progress.GetAchievementProgress("logs"));
        Assert.IsTrue(progress.HasAchievement("logs"));
    }

    // ─── Сброс прогресса (этап 8) ─────────────────────────────

    /// <summary>Прогресс со всем, что сбрасывается: открытия, достижения, склад, постройки, туториал.</summary>
    private MetaProgress FilledProgress()
    {
        MetaProgress progress = _saves.Progress();
        progress.UnlockCommand("mine");
        progress.UnlockConstruct("for");
        progress.UnlockRecipe("bed");
        progress.UnlockHubBuilding("storage");
        progress.UnlockEvent("caravan");
        progress.CompleteAchievement("haul");
        progress.SetAchievementProgress("logs", 3);
        progress.SetTutorialFlag("intro");
        progress.HintsEnabled = false;
        progress.AddHubBuilding("storage", new UnityEngine.Vector2Int(1, 2));
        progress.RecordRunFinished(defeated: true);

        MaterialResource log = TestHelpers.Material("log");
        progress.AddMaterial(log, 5);
        UnityEngine.Object.DestroyImmediate(log);

        Assert.IsTrue(progress.Save());
        return progress;
    }

    private static void AssertEmpty(MetaProgress progress)
    {
        Assert.IsFalse(progress.IsCommandUnlocked("mine"));
        Assert.IsFalse(progress.IsConstructUnlocked("for"));
        Assert.IsFalse(progress.IsRecipeUnlocked("bed"));
        Assert.IsFalse(progress.IsHubBuildingUnlocked("storage"));
        Assert.IsFalse(progress.IsEventUnlocked("caravan"));
        Assert.IsFalse(progress.HasAchievement("haul"));
        Assert.AreEqual(0, progress.CompletedAchievements.Count);
        Assert.AreEqual(0, progress.GetAchievementProgress("logs"));
        Assert.IsFalse(progress.HasTutorialFlag("intro"));
        Assert.IsTrue(progress.HintsEnabled, "hints are on again after a reset");
        Assert.AreEqual(0, progress.Materials.Count);
        Assert.AreEqual(0, progress.GetMaterialCount("log"));
        Assert.AreEqual(0, progress.HubBuildings.Count);
        Assert.AreEqual(0, progress.RunsFinished);
        Assert.AreEqual(0, progress.Defeats);
    }

    [Test]
    public void ResetAll_ClearsEverythingInMemoryAndOnDisk_KeepsBackup()
    {
        MetaProgress progress = FilledProgress();
        string before = File.ReadAllText(progress.FilePath);

        Assert.IsTrue(progress.ResetAll());

        AssertEmpty(progress);
        AssertEmpty(_saves.Progress());

        // Прежнее сохранение отложено копией
        string backup = progress.FilePath + MetaProgress.ResetBackupSuffix;
        Assert.IsTrue(File.Exists(backup));
        Assert.AreEqual(before, File.ReadAllText(backup));
        Assert.IsTrue(new MetaProgress(backup).IsCommandUnlocked("mine"));
    }

    [Test]
    public void ResetAll_ThenNewProgress_IsSavedNormally()
    {
        MetaProgress progress = FilledProgress();
        progress.ResetAll();

        Assert.IsTrue(progress.UnlockCommand("mine"), "after a reset unlocks are new again");
        Assert.IsTrue(progress.Save());
        Assert.IsTrue(_saves.Progress().IsCommandUnlocked("mine"));
        Assert.IsFalse(_saves.Progress().HasAchievement("haul"));
    }

    [Test]
    public void ResetAll_WithoutFile_WritesEmptyProgressWithoutBackup()
    {
        MetaProgress progress = _saves.Progress();

        Assert.IsTrue(progress.ResetAll());

        Assert.IsTrue(File.Exists(progress.FilePath));
        Assert.IsFalse(File.Exists(progress.FilePath + MetaProgress.ResetBackupSuffix));
        AssertEmpty(_saves.Progress());
    }

    [Test]
    public void ResetAll_DoesNotTouchOtherSaveFiles()
    {
        // Библиотека кода (code_library.json) — код игрока, сброс прогресса её не стирает
        string library = _saves.PathOf("code_library.json");
        File.WriteAllText(library, "{\"entries\":[]}");

        FilledProgress().ResetAll();

        Assert.IsTrue(File.Exists(library));
        Assert.AreEqual("{\"entries\":[]}", File.ReadAllText(library));
    }

    [Test]
    public void ResetFile_MovesSaveToBackup_NextLoadIsEmpty()
    {
        MetaProgress progress = FilledProgress();
        File.WriteAllText(progress.FilePath + ".tmp", "partial");

        Assert.IsTrue(MetaProgress.ResetFile(progress.FilePath));

        Assert.IsFalse(File.Exists(progress.FilePath));
        Assert.IsFalse(File.Exists(progress.FilePath + ".tmp"));
        Assert.IsTrue(File.Exists(progress.FilePath + MetaProgress.ResetBackupSuffix));
        AssertEmpty(_saves.Progress());

        // Второй сброс — файла нет, ничего не делает, старая копия цела
        Assert.IsFalse(MetaProgress.ResetFile(progress.FilePath));
        Assert.IsTrue(new MetaProgress(progress.FilePath + MetaProgress.ResetBackupSuffix).HasAchievement("haul"));
    }
}
