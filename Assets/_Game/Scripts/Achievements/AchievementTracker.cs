using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Прогресс одного достижения для UI: сколько набрано из скольких.
/// У EfficientGather Current — набранное в текущем запуске кода (между запусками 0).
/// </summary>
public readonly struct AchievementProgress
{
    public readonly int Current;
    public readonly int Target;
    public readonly bool Completed;

    public float Fraction => Target > 0 ? Mathf.Clamp01((float)Current / Target) : 0f;

    public AchievementProgress(int current, int target, bool completed)
    {
        Current = current;
        Target = target;
        Completed = completed;
    }
}

/// <summary>
/// Достижения: слушает <see cref="GameEventBus"/>, считает прогресс условий и выдаёт награды.
/// Не знает о конкретных командах и системах — только о событиях.
///   • Counter / FirstUse — счётчик в MetaProgress (между забегами, в том числе после поражения);
///     на диск пишется в конце каждого запуска кода, в конце дня и при выгрузке сцены (Dispose),
///     а не на каждое событие.
///   • EfficientGather — счётчик текущего запуска кода; проверяется в момент, когда набран порог:
///     потрачено с начала запуска (PlayerEnergy.TotalSpent) ≤ maxEnergy → выполнено. Считаются только
///     события с FromThisRun (подбор лута, нарубленного в прошлом запуске, не считается).
/// События с Recycled (подбор выброшенного, стартовые предметы) не считаются.
/// Выполненное: MetaProgress.CompleteAchievement, награды через UnlockRegistry, Save, OnAchievementCompleted.
/// Работает и в лагере, и в забеге. Создаётся в Level; Level вызывает Dispose в OnDestroy (шина статическая).
/// </summary>
public class AchievementTracker : IDisposable
{
    /// <summary>Достижение выполнено (награды уже выданы и сохранены) — для уведомления и панели.</summary>
    public event Action<AchievementDefinition> OnAchievementCompleted;

    /// <summary>Изменился прогресс незавершённого достижения — для живого обновления панели.</summary>
    public event Action<AchievementDefinition> OnProgressChanged;

    private readonly MetaProgress _progress;
    private readonly UnlockRegistry _unlocks;
    private readonly PlayerEnergy _energy;
    private readonly List<AchievementDefinition> _definitions = new List<AchievementDefinition>();
    private readonly List<AchievementDefinition> _completedThisScene = new List<AchievementDefinition>();
    // EfficientGather: набранное в текущем запуске кода
    private readonly Dictionary<AchievementDefinition, int> _runCounts = new Dictionary<AchievementDefinition, int>();

    private bool _codeRunning;
    private float _runStartSpent;
    private bool _dirty;
    private bool _disposed;

    /// <summary>Все действующие достижения в порядке каталога (без пустых элементов и дублей id).</summary>
    public IReadOnlyList<AchievementDefinition> Definitions => _definitions;

    /// <summary>Выполненные с загрузки сцены, то есть в этом забеге (или в этом заходе в лагерь).</summary>
    public IReadOnlyList<AchievementDefinition> CompletedThisScene => _completedThisScene;

    public int CompletedCount
    {
        get
        {
            int count = 0;
            foreach (AchievementDefinition definition in _definitions)
                if (IsCompleted(definition)) count++;
            return count;
        }
    }

    public AchievementTracker(
        AchievementCatalog catalog, MetaProgress progress, UnlockRegistry unlocks, PlayerEnergy energy)
    {
        Debug.Assert(progress != null, "AchievementTracker: MetaProgress is null");
        Debug.Assert(unlocks != null, "AchievementTracker: UnlockRegistry is null");
        Debug.Assert(energy != null, "AchievementTracker: PlayerEnergy is null");

        _progress = progress;
        _unlocks = unlocks;
        _energy = energy;

        CollectDefinitions(catalog);
        ValidateRewards();

        GameEventBus.OnGameEvent += HandleGameEvent;
        GameEventBus.OnRunStarted += HandleRunStarted;
        GameEventBus.OnRunFinished += HandleRunFinished;
    }

    // ─── Public API ───────────────────────────────────────────

    public bool IsCompleted(AchievementDefinition definition) =>
        definition != null && _progress.HasAchievement(definition.Id);

    public AchievementProgress GetProgress(AchievementDefinition definition)
    {
        if (definition == null || definition.Condition == null) return new AchievementProgress(0, 1, false);

        int target = definition.Condition.Target;
        if (IsCompleted(definition)) return new AchievementProgress(target, target, true);

        int current = definition.Condition.type == AchievementConditionType.EfficientGather
            ? (_runCounts.TryGetValue(definition, out int run) ? run : 0)
            : _progress.GetAchievementProgress(definition.Id);
        return new AchievementProgress(Mathf.Min(current, target), target, false);
    }

    public AchievementDefinition Find(string id)
    {
        foreach (AchievementDefinition definition in _definitions)
            if (definition.Id == id) return definition;
        return null;
    }

    /// <summary>
    /// Выполнить достижение сразу (отладочное окно CodeRogue/Debug) — тем же путём, что и по условию:
    /// награды, сохранение, OnAchievementCompleted. False — id неизвестен или уже выполнено.
    /// </summary>
    public bool DebugComplete(string id)
    {
        AchievementDefinition definition = Find(id);
        if (definition == null || IsCompleted(definition)) return false;

        Complete(definition);
        return true;
    }

    /// <summary>Отписка от шины и запись несохранённого прогресса. Вызывает владелец (Level.OnDestroy).</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        GameEventBus.OnGameEvent -= HandleGameEvent;
        GameEventBus.OnRunStarted -= HandleRunStarted;
        GameEventBus.OnRunFinished -= HandleRunFinished;

        SaveIfDirty();
    }

    // ─── Handlers ─────────────────────────────────────────────

    private void HandleRunStarted()
    {
        _codeRunning = true;
        _runStartSpent = _energy.TotalSpent;
        ResetRunCounts();
    }

    private void HandleRunFinished(CodeRunReport report)
    {
        _codeRunning = false;
        ResetRunCounts();
        SaveIfDirty();
    }

    private void HandleGameEvent(GameEvent gameEvent)
    {
        if (gameEvent.Recycled || gameEvent.Amount <= 0) return;

        // Выдача наград не публикует игровых событий, но перебираем копию на случай будущих изменений
        foreach (AchievementDefinition definition in _definitions.ToArray())
        {
            AchievementCondition condition = definition.Condition;
            if (condition == null || !condition.Matches(gameEvent) || IsCompleted(definition)) continue;

            if (condition.type == AchievementConditionType.EfficientGather)
                AdvanceEfficient(definition, gameEvent);
            else
                AdvanceCounter(definition, gameEvent.Amount);
        }

        // Конец дня — естественная точка сохранения прогресса
        if (gameEvent.Type == GameEventType.DayPassed)
            SaveIfDirty();
    }

    // ─── Conditions ───────────────────────────────────────────

    private void AdvanceCounter(AchievementDefinition definition, int amount)
    {
        int value = _progress.GetAchievementProgress(definition.Id) + amount;
        if (value >= definition.Condition.Target)
        {
            Complete(definition);
            return;
        }

        _progress.SetAchievementProgress(definition.Id, value);
        _dirty = true;
        OnProgressChanged?.Invoke(definition);
    }

    /// <summary>
    /// Окно измерения — один запуск кода; вне запуска (событий от кода нет) не считается.
    /// </summary>
    private void AdvanceEfficient(AchievementDefinition definition, GameEvent gameEvent)
    {
        if (!_codeRunning || !gameEvent.FromThisRun) return;
        int amount = gameEvent.Amount;

        _runCounts.TryGetValue(definition, out int count);
        int before = count;
        count += amount;
        _runCounts[definition] = count;

        int target = definition.Condition.Target;
        // Проверяем один раз — в момент, когда порог набран: энергия дальше только растёт
        if (before < target && count >= target)
        {
            float spent = _energy.TotalSpent - _runStartSpent;
            if (spent <= definition.Condition.maxEnergy + 0.0001f)
            {
                Complete(definition);
                return;
            }
        }

        OnProgressChanged?.Invoke(definition);
    }

    private void Complete(AchievementDefinition definition)
    {
        if (!_progress.CompleteAchievement(definition.Id)) return;

        foreach (AchievementReward reward in definition.Rewards)
            Grant(reward);

        _completedThisScene.Add(definition);
        _runCounts.Remove(definition);

        _progress.Save();
        _dirty = false;

        // На экране — тост AchievementToasts (CodeEditorManager слушает OnAchievementCompleted); здесь только лог
        Debug.Log($"AchievementTracker: achievement '{definition.Id}' completed");
        OnAchievementCompleted?.Invoke(definition);
    }

    // ─── Rewards ──────────────────────────────────────────────

    /// <summary>
    /// Открытия во время запуска кода песочница применяет после его окончания.
    /// Id, которых в игре ещё нет, сохраняются отложенными (см. UnlockRegistry).
    /// </summary>
    private void Grant(AchievementReward reward)
    {
        if (reward == null || string.IsNullOrEmpty(reward.Id)) return;

        switch (reward.kind)
        {
            case AchievementRewardKind.Command: _unlocks.UnlockCommand(reward.Id); break;
            case AchievementRewardKind.Construct: _unlocks.UnlockConstruct(reward.Id); break;
            case AchievementRewardKind.Recipe: _unlocks.UnlockRecipe(reward.Id); break;
            case AchievementRewardKind.HubBuilding: _unlocks.UnlockHubBuilding(reward.Id); break;
            case AchievementRewardKind.MapEvent: _unlocks.UnlockMapEvent(reward.Id); break;
        }
    }

    /// <summary>
    /// Награды уже выполненных достижений, добавленные в ассеты позже. Вызывает Level после
    /// инициализации UI, чтобы окно «Unlocked» увидело новые открытия.
    /// Повторная выдача ничего не меняет (открытия идемпотентны).
    /// </summary>
    public void GrantRewardsOfCompleted()
    {
        foreach (AchievementDefinition definition in _definitions)
        {
            if (!IsCompleted(definition)) continue;
            foreach (AchievementReward reward in definition.Rewards)
                Grant(reward);
        }
    }

    // ─── Helpers ──────────────────────────────────────────────

    private void CollectDefinitions(AchievementCatalog catalog)
    {
        if (catalog == null)
        {
            Debug.LogWarning("AchievementTracker: AchievementCatalog is not assigned — no achievements");
            return;
        }

        foreach (string problem in catalog.Validate())
            Debug.LogWarning($"AchievementTracker: {problem}", catalog);

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (AchievementDefinition definition in catalog.Achievements)
            if (definition != null && definition.Condition != null && ids.Add(definition.Id))
                _definitions.Add(definition);
    }

    /// <summary>
    /// Команды и рецепты, которых нет в игре, — не ошибка (награды будущих этапов): только сообщение.
    /// </summary>
    private void ValidateRewards()
    {
        foreach (AchievementDefinition definition in _definitions)
        {
            foreach (AchievementReward reward in definition.Rewards)
            {
                if (reward == null || string.IsNullOrEmpty(reward.Id)) continue;

                bool known = reward.kind switch
                {
                    AchievementRewardKind.Command => _unlocks.IsKnownCommand(reward.Id),
                    AchievementRewardKind.Recipe => _unlocks.IsKnownRecipe(reward.Id),
                    _ => true
                };

                if (!known)
                    Debug.Log($"AchievementTracker: '{definition.Id}' rewards {reward.kind} '{reward.Id}', " +
                              "which is not in the game yet (deferred until it appears)");
            }
        }
    }

    private void ResetRunCounts()
    {
        if (_runCounts.Count == 0) return;

        var changed = new List<AchievementDefinition>(_runCounts.Keys);
        _runCounts.Clear();
        foreach (AchievementDefinition definition in changed)
            OnProgressChanged?.Invoke(definition);
    }

    private void SaveIfDirty()
    {
        if (!_dirty) return;
        _dirty = false;
        _progress.Save();
    }
}
