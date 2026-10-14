using System.Collections.Generic;

/// <summary>
/// Режим игры на этой загрузке сцены: лагерь (экран хаба поверх карты) или забег.
/// В забеге — выход по кнопке Exit Run (с подтверждением) и поражение при 0 здоровья:
/// показывает итоги, переносит материалы на склад лагеря (при поражении — ничего) и
/// сохраняет мета-прогресс сразу, до перехода, чтобы итог нельзя было отменить закрытием игры.
/// </summary>
public class RunController
{
    private readonly GameMode _mode;
    private readonly RunScreens _screens;
    private readonly TopBar _topBar;
    private readonly LuaSandbox _sandbox;
    private readonly PlayerInventory _inventory;
    private readonly PlayerHealth _health;
    private readonly DayCycle _dayCycle;
    private readonly MetaProgress _progress;

    private bool _finished;

    public RunController(
        GameMode mode, RunScreens screens, TopBar topBar, LuaSandbox sandbox, PlayerInventory inventory,
        PlayerHealth health, DayCycle dayCycle, MetaProgress progress)
    {
        _mode = mode;
        _screens = screens;
        _topBar = topBar;
        _sandbox = sandbox;
        _inventory = inventory;
        _health = health;
        _dayCycle = dayCycle;
        _progress = progress;

        // Всё это живёт до перезагрузки сцены вместе с контроллером — отписка не нужна
        _screens.OnStartRunRequested += GameSession.StartRun;
        _screens.OnExitConfirmed += HandleExitConfirmed;
        _screens.OnReturnToHubRequested += GameSession.ReturnToHub;
        _topBar.OnExitRunRequested += HandleExitRunRequested;
        _health.OnDied += HandleDied;
    }

    public void Start()
    {
        if (_mode == GameMode.Hub)
            _screens.ShowHub(_progress);
    }

    // ─── Handlers ─────────────────────────────────────────────

    private void HandleExitRunRequested()
    {
        if (_finished || _mode != GameMode.Run) return;

        if (_sandbox.IsRunning)
        {
            UIEventBus.PublishNotification("Can't leave the run while the code is running", LogEntryType.Warning);
            return;
        }

        _screens.ShowExitConfirm();
    }

    private void HandleExitConfirmed()
    {
        // Пока висел диалог, код запустить нельзя (диалог закрывает экран), но проверим ещё раз
        if (_sandbox.IsRunning) return;
        Finish(defeated: false);
    }

    private void HandleDied()
    {
        if (_sandbox.IsRunning) _sandbox.Stop();
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
