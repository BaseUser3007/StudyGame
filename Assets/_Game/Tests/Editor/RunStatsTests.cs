using System.Reflection;
using NUnit.Framework;

/// <summary>
/// Статистика забега (T05): события GameEventBus → счётчики RunStats, отписка в Dispose.
/// GameEventBus статический: подписчики сбрасываются до и после каждого теста.
/// </summary>
public class RunStatsTests
{
    private RunStats _stats;

    [SetUp]
    public void SetUp()
    {
        ResetBus();
        _stats = new RunStats();
    }

    [TearDown]
    public void TearDown()
    {
        _stats?.Dispose();
        _stats = null;
        ResetBus();
    }

    // ─── Заготовки ────────────────────────────────────────────

    /// <summary>Сброс подписчиков статической шины (тот же метод, что вызывается при входе в Play Mode).</summary>
    private static void ResetBus()
    {
        MethodInfo reset = typeof(GameEventBus).GetMethod("ResetStatics", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(reset, "GameEventBus.ResetStatics not found");
        reset.Invoke(null, null);
    }

    private static void FinishRun(float energy, string outcome, bool started = true)
    {
        GameEventBus.PublishRunStarted();
        GameEventBus.PublishRunFinished(new CodeRunReport(
            outcome == GameEventBus.RunTagOk, started, energy, null, outcome));
    }

    // ─── Тесты ────────────────────────────────────────────────

    [Test]
    public void NewStats_AreZero()
    {
        Assert.AreEqual(0, _stats.CodeRuns);
        Assert.AreEqual(0f, _stats.EnergySpent);
        Assert.AreEqual(0, _stats.Errors);
        Assert.AreEqual(0, _stats.EnemiesKilled);
        Assert.AreEqual(0, _stats.PuzzlesSolved);
        Assert.AreEqual(0, _stats.ItemsCrafted);
    }

    [Test]
    public void RunFinished_CountsRunsEnergyAndOnlyErrorTag()
    {
        FinishRun(12.5f, GameEventBus.RunTagOk);
        FinishRun(3f, GameEventBus.RunTagError);
        FinishRun(0f, GameEventBus.RunTagError, started: false); // ошибка компиляции
        FinishRun(20f, GameEventBus.RunTagEnergy);
        FinishRun(1f, GameEventBus.RunTagStopped);
        FinishRun(0f, GameEventBus.RunTagLocked, started: false);

        Assert.AreEqual(6, _stats.CodeRuns);
        Assert.AreEqual(36.5f, _stats.EnergySpent, 0.001f);
        Assert.AreEqual(2, _stats.Errors);
    }

    [Test]
    public void GameplayEvents_CountKillsPuzzlesAndCraftedAmount()
    {
        GameEventBus.Publish(GameEventType.EnemyKilled, "goblin", 1, "guard");
        GameEventBus.Publish(GameEventType.EnemyKilled, "goblin", 1, "patrol");
        GameEventBus.Publish(GameEventType.PuzzleSolved, PuzzleTypes.Maze);
        GameEventBus.Publish(GameEventType.Crafted, "axe", 1);
        GameEventBus.Publish(GameEventType.Crafted, "plank", 4);

        Assert.AreEqual(2, _stats.EnemiesKilled);
        Assert.AreEqual(1, _stats.PuzzlesSolved);
        Assert.AreEqual(5, _stats.ItemsCrafted);
    }

    [Test]
    public void UnrelatedEvents_AreIgnored()
    {
        GameEventBus.Publish(GameEventType.Harvested, "stick", 3, "stick");
        GameEventBus.Publish(GameEventType.ItemObtained, "axe", 1);
        GameEventBus.Publish(GameEventType.EnemyFound, "goblin");
        GameEventBus.Publish(GameEventType.CommandUsed, "craft");
        // RunFinished без отчёта (только событие) не считается запуском — запуски идут по OnRunFinished
        GameEventBus.Publish(GameEventType.RunFinished, GameEventBus.RunFailedId, tag: GameEventBus.RunTagError);

        Assert.AreEqual(0, _stats.CodeRuns);
        Assert.AreEqual(0, _stats.Errors);
        Assert.AreEqual(0, _stats.EnemiesKilled);
        Assert.AreEqual(0, _stats.PuzzlesSolved);
        Assert.AreEqual(0, _stats.ItemsCrafted);
    }

    [Test]
    public void Dispose_Unsubscribes()
    {
        GameEventBus.Publish(GameEventType.EnemyKilled, "goblin");
        _stats.Dispose();
        _stats.Dispose(); // повторный вызов безопасен

        FinishRun(5f, GameEventBus.RunTagError);
        GameEventBus.Publish(GameEventType.EnemyKilled, "goblin");
        GameEventBus.Publish(GameEventType.Crafted, "axe", 1);

        Assert.AreEqual(0, _stats.CodeRuns);
        Assert.AreEqual(0f, _stats.EnergySpent);
        Assert.AreEqual(1, _stats.EnemiesKilled);
        Assert.AreEqual(0, _stats.ItemsCrafted);
    }

    [Test]
    public void NewInstance_StartsFromZero_AfterPreviousDisposed()
    {
        FinishRun(5f, GameEventBus.RunTagOk);
        GameEventBus.Publish(GameEventType.PuzzleSolved, PuzzleTypes.CodeTask, 1, "sum");
        _stats.Dispose();

        // Как после перезагрузки сцены: новый счётчик видит только новые события
        _stats = new RunStats();
        FinishRun(2f, GameEventBus.RunTagOk);

        Assert.AreEqual(1, _stats.CodeRuns);
        Assert.AreEqual(2f, _stats.EnergySpent, 0.001f);
        Assert.AreEqual(0, _stats.PuzzlesSolved);
    }
}
