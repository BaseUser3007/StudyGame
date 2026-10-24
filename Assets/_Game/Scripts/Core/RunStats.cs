using System;

/// <summary>
/// Статистика забега для экрана итогов (RunSummary): запуски кода, потраченная энергия, запуски
/// с ошибкой, побеждённые враги, решённые головоломки, скрафченные предметы. Считает только события
/// GameEventBus с момента создания — создаётся в Level на каждой загрузке сцены, так что забег
/// начинается с нуля. Подписан на статическую шину: Dispose обязателен (Level.OnDestroy).
/// </summary>
public class RunStats : IDisposable
{
    private bool _disposed;

    public RunStats()
    {
        GameEventBus.OnRunFinished += HandleRunFinished;
        GameEventBus.OnGameEvent += HandleGameEvent;
    }

    // ─── Public API ───────────────────────────────────────────

    /// <summary>Завершённых запусков кода (любой итог, включая ошибки компиляции и Stop).</summary>
    public int CodeRuns { get; private set; }
    /// <summary>Энергии потрачено за все запуски (CodeRunReport.EnergySpent: конструкции, команды, добыча, крафт).</summary>
    public float EnergySpent { get; private set; }
    /// <summary>Запусков, закончившихся ошибкой в коде (Tag RunFinished = "error").</summary>
    public int Errors { get; private set; }
    public int EnemiesKilled { get; private set; }
    public int PuzzlesSolved { get; private set; }
    /// <summary>Скрафчено предметов (сумма Amount событий Crafted).</summary>
    public int ItemsCrafted { get; private set; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GameEventBus.OnRunFinished -= HandleRunFinished;
        GameEventBus.OnGameEvent -= HandleGameEvent;
    }

    // ─── Handlers ─────────────────────────────────────────────

    private void HandleRunFinished(CodeRunReport report)
    {
        CodeRuns++;
        if (report.EnergySpent > 0f)
            EnergySpent += report.EnergySpent;
        if (report.Outcome == GameEventBus.RunTagError)
            Errors++;
    }

    // Запуски считаются по OnRunFinished (там отчёт с энергией), событие RunFinished здесь не нужно
    private void HandleGameEvent(GameEvent gameEvent)
    {
        switch (gameEvent.Type)
        {
            case GameEventType.EnemyKilled:
                EnemiesKilled += Math.Max(1, gameEvent.Amount);
                break;
            case GameEventType.PuzzleSolved:
                PuzzlesSolved += Math.Max(1, gameEvent.Amount);
                break;
            case GameEventType.Crafted:
                if (gameEvent.Amount > 0)
                    ItemsCrafted += gameEvent.Amount;
                break;
        }
    }
}
