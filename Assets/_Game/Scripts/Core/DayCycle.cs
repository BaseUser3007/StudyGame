using System;
using UnityEngine;

/// <summary>
/// Снимок состояния дня для UI.
/// </summary>
public readonly struct DayInfo
{
    public readonly int Day;
    public readonly int RunsUsed;
    public readonly int RunsPerDay;
    public readonly float MaxEnergy;
    /// <summary>Чем закончился прошлый день (штраф/бонус) — пусто в первый день.</summary>
    public readonly string Summary;
    /// <summary>День, с которого действует мировая угроза (патрули врагов).</summary>
    public readonly int ThreatDay;
    /// <summary>Сколько дней до угрозы: 1 — завтра, 0 — угроза уже идёт.</summary>
    public readonly int DaysUntilThreat;
    public readonly bool ThreatActive;

    public int RunsLeft => Mathf.Max(0, RunsPerDay - RunsUsed);

    public DayInfo(
        int day, int runsUsed, int runsPerDay, float maxEnergy, string summary,
        int threatDay = 0, int daysUntilThreat = 0, bool threatActive = false)
    {
        Day = day;
        RunsUsed = runsUsed;
        RunsPerDay = runsPerDay;
        MaxEnergy = maxEnergy;
        Summary = summary;
        ThreatDay = threatDay;
        DaysUntilThreat = daysUntilThreat;
        ThreatActive = threatActive;
    }
}

/// <summary>
/// Дневной цикл: за день можно запустить код runsPerDay раз (неудачные запуски тоже считаются).
/// После последнего запуска (или по кнопке End Day) день заканчивается, и на следующий день
/// энергия восстанавливается до нового максимума:
///   • закончил день без энергии (меньше 1) — штраф baseEnergyLimit × energyPenaltyMultiplier на следующий день;
///   • закончил день у спального места — бонус homeBonusPerDay за каждую ночь подряд, не больше maxEnergyBonus.
/// Максимум не опускается ниже minEnergyLimit.
/// Мировая угроза: с дня RunModifiers.ThreatDay действует угроза (IsThreatActive, GameEventBus ThreatReached —
/// один раз за забег), за день до неё — предупреждение в уведомлениях. Патрули по OnDayStarted спавнит EnemyManager.
/// </summary>
public class DayCycle
{
    /// <summary>Начался новый день (после восстановления энергии). Аргумент — номер дня.</summary>
    public event Action<int> OnDayStarted;
    /// <summary>Наступила мировая угроза (один раз за забег, до OnDayStarted того же дня).</summary>
    public event Action OnThreatReached;

    // Энергии меньше этого — «закончил день с нулём»: на базовую команду уже не хватает
    private const float ExhaustedThreshold = 1f;

    private readonly PlayerConfig _config;
    private readonly RunModifiers _modifiers;
    private readonly PlayerEnergy _energy;
    private readonly ISleepingPlaceProvider _sleepingPlaces;
    private readonly PlayerHealth _health;
    private LuaSandbox _sandbox;

    private int _sleepStreak;
    private string _summary = string.Empty;
    private bool _threatAnnounced;

    public int Day { get; private set; } = 1;
    public int RunsUsed { get; private set; }
    public int RunsPerDay => _modifiers.RunsPerDay;
    public int RunsLeft => Mathf.Max(0, RunsPerDay - RunsUsed);

    /// <summary>День, с которого действует мировая угроза.</summary>
    public int ThreatDay => _modifiers.ThreatDay;
    /// <summary>Сколько дней до угрозы: 1 — завтра, 0 — угроза уже идёт.</summary>
    public int DaysUntilThreat => Mathf.Max(0, ThreatDay - Day);
    public bool IsThreatActive => Day >= ThreatDay;

    public DayCycle(
        PlayerConfig config, RunModifiers modifiers, PlayerEnergy energy, ISleepingPlaceProvider sleepingPlaces,
        PlayerHealth health = null)
    {
        _health = health;
        _config = config;
        _modifiers = modifiers;
        _energy = energy;
        _sleepingPlaces = sleepingPlaces;
    }

    /// <summary>
    /// Считать запуски этой песочницы. Вызывается после её инициализации.
    /// </summary>
    public void Attach(LuaSandbox sandbox)
    {
        _sandbox = sandbox;
        _sandbox.OnRunningChanged += HandleRunningChanged;
    }

    // ─── Public API ───────────────────────────────────────────

    /// <summary>
    /// Угроза в первый день: ThreatDay = 1 — объявление (ThreatReached), ThreatDay = 2 — предупреждение.
    /// Level вызывает после создания AchievementTracker и UI, чтобы событие и уведомление не потерялись.
    /// </summary>
    public void AnnounceStart()
    {
        NotifyThreat();
    }

    /// <summary>
    /// Закончить день досрочно (кнопка End Day). Во время выполнения кода нельзя.
    /// </summary>
    public bool TryEndDay()
    {
        if (_sandbox != null && _sandbox.IsRunning) return false;
        EndDay();
        return true;
    }

    public void Publish()
    {
        UIEventBus.PublishDayChanged(new DayInfo(
            Day, RunsUsed, RunsPerDay, _energy.Max, _summary, ThreatDay, DaysUntilThreat, IsThreatActive));
    }

    // ─── Day ──────────────────────────────────────────────────

    private void HandleRunningChanged(bool running)
    {
        // Запуск засчитывается, когда он закончился — успешно, с ошибкой или по Stop
        if (!running) RegisterRunFinished();
    }

    /// <summary>
    /// Засчитать законченный запуск; после последнего за день — конец дня.
    /// </summary>
    public void RegisterRunFinished()
    {
        RunsUsed++;
        if (RunsUsed >= RunsPerDay)
            EndDay();
        else
            Publish();
    }

    private void EndDay()
    {
        bool exhausted = _energy.Current < ExhaustedThreshold;
        bool slept = _sleepingPlaces != null && _sleepingPlaces.IsSleepingPlaceNearPlayer();
        _sleepStreak = slept ? _sleepStreak + 1 : 0;

        float bonus = Mathf.Min(_config.maxEnergyBonus, _config.homeBonusPerDay * _sleepStreak);
        float baseLimit = _modifiers.BaseEnergyLimit;
        float penalty = exhausted ? baseLimit * _config.energyPenaltyMultiplier : 0f;
        float max = Mathf.Clamp(
            baseLimit + bonus - penalty,
            _config.minEnergyLimit,
            baseLimit + _config.maxEnergyBonus);

        // Ночь у спального места лечит
        float healed = 0f;
        if (slept && _health != null && !_health.IsDead)
        {
            float before = _health.Current;
            _health.Heal(_modifiers.SleepHeal);
            healed = _health.Current - before;
        }

        _summary = BuildSummary(exhausted, penalty, slept, bonus, healed);

        PlayerEventBus.PublishDayEnded();
        GameEventBus.Publish(GameEventType.DayPassed, null, 1, exhausted ? "exhausted" : slept ? "slept" : null);

        Day++;
        RunsUsed = 0;
        _energy.SetMax(max, refill: true);

        Publish();
        UIEventBus.PublishNotification($"Day {Day}: max energy {max:0.#}. {_summary}", LogEntryType.Success);
        NotifyThreat();
        PlayerEventBus.PublishDayStarted();
        OnDayStarted?.Invoke(Day);
    }

    // ─── Threat ───────────────────────────────────────────────

    /// <summary>
    /// В начале дня (и забега — AnnounceStart): за день до угрозы — предупреждение, в день угрозы — объявление (один раз).
    /// </summary>
    private void NotifyThreat()
    {
        if (IsThreatActive)
            AnnounceThreat();
        else if (DaysUntilThreat == 1)
            UIEventBus.PublishNotification("Goblins will start patrolling tomorrow.", LogEntryType.Warning);
    }

    private void AnnounceThreat()
    {
        if (_threatAnnounced) return;
        _threatAnnounced = true;

        UIEventBus.PublishNotification(
            "The threat has begun: goblin patrols come out of the fog every day.", LogEntryType.Error);
        // Amount — 1 (счётчики достижений), номер дня — в Tag
        GameEventBus.Publish(GameEventType.ThreatReached, null, 1, Day.ToString());
        OnThreatReached?.Invoke();
    }

    private string BuildSummary(bool exhausted, float penalty, bool slept, float bonus, float healed)
    {
        if (!exhausted && !slept) return string.Empty;

        string text = string.Empty;
        if (exhausted) text += $"−{penalty:0.#}: energy ran out";
        if (slept)
        {
            if (text.Length > 0) text += ", ";
            text += $"+{bonus:0.#}: slept in bed ({_sleepStreak} night{(_sleepStreak > 1 ? "s" : "")})";
            if (healed > 0f) text += $", +{healed:0.#} health";
        }
        return text;
    }
}
