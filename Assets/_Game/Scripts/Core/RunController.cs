using System.Collections.Generic;

/// <summary>
/// Режим игры на этой загрузке сцены: лагерь (экран хаба поверх карты) или забег.
/// В забеге — выход через портал (Lua-команда enterPortal → RequestExitThroughPortal; кнопки Exit Run нет)
/// и поражение при 0 здоровья: показывает итоги, переносит материалы на склад лагеря (при поражении — ничего) и
/// сохраняет мета-прогресс сразу, до перехода, чтобы итог нельзя было отменить закрытием игры.
/// Выход через портал во время выполнения кода — после конца запуска.
/// </summary>
public class RunController : IRunExitService
{
    private readonly GameMode _mode;
    private readonly RunScreens _screens;
    private readonly LuaSandbox _sandbox;
    private readonly PlayerInventory _inventory;
    private readonly PlayerHealth _health;
    private readonly DayCycle _dayCycle;
    private readonly MetaProgress _progress;
    private readonly AchievementTracker _achievements;
    private readonly RunStats _stats;

    private bool _finished;
    // Здоровье кончилось во время выполнения кода: итог — после остановки запуска
    private bool _defeatPending;
    // Выход через портал запрошен во время выполнения кода: итог — после остановки запуска
    private bool _portalExitPending;

    public bool IsExitPending => _portalExitPending;

    public RunController(
        GameMode mode, RunScreens screens, LuaSandbox sandbox, PlayerInventory inventory,
        PlayerHealth health, DayCycle dayCycle, MetaProgress progress, AchievementTracker achievements,
        RunStats stats = null)
    {
        _mode = mode;
        _screens = screens;
        _sandbox = sandbox;
        _inventory = inventory;
        _health = health;
        _dayCycle = dayCycle;
        _progress = progress;
        _achievements = achievements;
        _stats = stats;

        // Всё это живёт до перезагрузки сцены вместе с контроллером — отписка не нужна
        _screens.OnStartRunRequested += GameSession.StartRun;
        _screens.OnReturnToHubRequested += GameSession.ReturnToHub;
        _screens.OnResetProgressRequested += HandleResetProgressRequested;
        _health.OnDied += HandleDied;
    }

    public void Start()
    {
        if (_mode == GameMode.Hub)
            _screens.ShowHub(_progress);
    }

    // ─── Handlers ─────────────────────────────────────────────

    /// <summary>Reset progress в лагере (подтверждён в UI) — только в лагере и не во время кода.</summary>
    private void HandleResetProgressRequested()
    {
        if (_mode != GameMode.Hub) return;

        if (_sandbox.IsRunning)
        {
            UIEventBus.PublishNotification(Loc.T("Can't reset progress while the code is running"), LogEntryType.Warning);
            return;
        }

        GameSession.ResetProgress();
    }

    // ─── Portal ───────────────────────────────────────────────

    /// <summary>
    /// Выход через портал (enterPortal), без подтверждения. Во время выполнения кода — после конца
    /// запуска (finally запуска ещё публикует события, выполненные ими достижения попадут в итоги).
    /// Повторный запрос, пока выход ждёт, — true без изменений.
    /// </summary>
    public bool RequestExitThroughPortal()
    {
        if (_finished || _defeatPending || _mode != GameMode.Run) return false;
        if (_portalExitPending) return true;

        if (!_sandbox.IsRunning)
        {
            Finish(defeated: false);
            return true;
        }

        _portalExitPending = true;
        _sandbox.OnRunningChanged += HandleRunningChangedAfterPortal;
        return true;
    }

    private void HandleRunningChangedAfterPortal(bool running)
    {
        if (running) return;

        _sandbox.OnRunningChanged -= HandleRunningChangedAfterPortal;
        _portalExitPending = false;
        // Игрок погиб, пока доигрывал запуск, — итог даёт поражение
        if (_defeatPending) return;
        Finish(defeated: false);
    }

    // ─── Defeat ───────────────────────────────────────────────

    private void HandleDied()
    {
        if (_finished || _defeatPending) return;

        if (!_sandbox.IsRunning)
        {
            Finish(defeated: true);
            return;
        }

        // finally остановленного запуска ещё публикует события (ConstructUsed, RunFinished) —
        // выполненные ими достижения должны попасть в итоги, поэтому ждём конца запуска
        _defeatPending = true;
        _sandbox.OnRunningChanged += HandleRunningChangedAfterDeath;
        _sandbox.Stop();
    }

    private void HandleRunningChangedAfterDeath(bool running)
    {
        if (running) return;

        _sandbox.OnRunningChanged -= HandleRunningChangedAfterDeath;
        Finish(defeated: true);
    }

    // ─── Finish ───────────────────────────────────────────────

    private void Finish(bool defeated)
    {
        if (_finished) return;
        _finished = true;

        RunSummary summary = BuildSummary(defeated);

        foreach (ItemStack stack in summary.Carried)
            _progress.AddMaterial(stack.item, stack.amount);
        _progress.RecordRunFinished(defeated);
        _progress.Save();

        _screens.ShowSummary(summary);
    }

    /// <summary>
    /// Раскладывает инвентарь: материалы — в лагерь, остальное остаётся. При поражении остаётся всё.
    /// Одинаковые предметы из разных ячеек складываются.
    /// </summary>
    private RunSummary BuildSummary(bool defeated)
    {
        var summary = new RunSummary { Defeated = defeated, Days = _dayCycle.Day };
        if (_achievements != null)
            summary.Achievements.AddRange(_achievements.CompletedThisScene);
        // Статистика — и при поражении (запуск, остановленный смертью, уже посчитан: итог строится после его конца)
        if (_stats != null)
        {
            summary.CodeRuns = _stats.CodeRuns;
            summary.EnergySpent = _stats.EnergySpent;
            summary.CodeErrors = _stats.Errors;
            summary.EnemiesKilled = _stats.EnemiesKilled;
            summary.PuzzlesSolved = _stats.PuzzlesSolved;
            summary.ItemsCrafted = _stats.ItemsCrafted;
        }
        var carried = new Dictionary<ResourceItem, int>();
        var left = new Dictionary<ResourceItem, int>();

        for (int i = 0; i < _inventory.SlotCount; i++)
        {
            InventorySlot slot = _inventory.GetSlot(i);
            if (slot.IsEmpty) continue;

            Dictionary<ResourceItem, int> target = !defeated && slot.Item.IsMaterial ? carried : left;
            target.TryGetValue(slot.Item, out int count);
            target[slot.Item] = count + slot.Count;
        }

        foreach (KeyValuePair<ResourceItem, int> pair in carried)
            summary.Carried.Add(new ItemStack { item = pair.Key, amount = pair.Value });
        foreach (KeyValuePair<ResourceItem, int> pair in left)
            summary.Left.Add(new ItemStack { item = pair.Key, amount = pair.Value });

        return summary;
    }
}
