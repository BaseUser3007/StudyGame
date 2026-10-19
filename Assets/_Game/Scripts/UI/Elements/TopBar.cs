using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class TopBar : VisualElement
{
    public event Action OnRunRequested;
    public event Action OnEndDayRequested;
    public event Action OnExitRunRequested;
    public event Action OnSimulationRequested;
    /// <summary>Кнопка Achievements — открыть панель достижений (её показывает CodeEditorManager).</summary>
    public event Action OnAchievementsRequested;
    /// <summary>Кнопка Hints — переключить подсказки туториала.</summary>
    public event Action OnHintsToggleRequested;
    /// <summary>Открыта справка (кнопка Help).</summary>
    public event Action OnHelpOpened;

    private Label _energyLabel;
    private Label _healthLabel;
    private VisualElement _healthFill;
    private Label _programCostLabel;
    private Label _dayLabel;
    private Label _daySummary;
    private Label _threatLabel;
    private VisualElement _health;
    private Button _btnRun;
    private Button _btnEndDay;
    private Button _btnExitRun;
    private Button _btnSimulation;
    private Button _btnAchievements;
    private Button _btnHints;
    private CommandHelp _commandHelp;

    // Цена прохода краснеет, если больше текущего запаса энергии
    private float _currentEnergy = float.MaxValue;
    private float _programCost;

    // Флэш полосы здоровья при уроне
    private const string HealthHitClass = "health--hit";
    private const long HealthFlashMs = 180;
    private float _lastHealth = -1f;
    private IVisualElementScheduledItem _healthFlashReset;

    // В лагере угрозы нет — индикатор скрыт
    private bool _hubMode;
    private bool _hasThreatInfo;

    public TopBar(VisualTreeAsset template)
    {
        VisualElement root = template.Instantiate();
        LocUxml.Translate(root);

        _energyLabel = root.Q<Label>("energy-label");
        _healthLabel = root.Q<Label>("health-label");
        _healthFill = root.Q<VisualElement>("health-fill");
        _programCostLabel = root.Q<Label>("program-cost");
        _dayLabel = root.Q<Label>("day-label");
        _daySummary = root.Q<Label>("day-summary");
        _threatLabel = root.Q<Label>("threat-label");
        _health = root.Q<VisualElement>("health");
        _btnRun = root.Q<Button>("btn-run");
        _btnEndDay = root.Q<Button>("btn-end-day");
        _btnExitRun = root.Q<Button>("btn-exit-run");
        _btnSimulation = root.Q<Button>("btn-simulation");
        _btnAchievements = root.Q<Button>("btn-achievements");
        _btnHints = root.Q<Button>("btn-hints");
        Debug.Assert(_btnHints != null, "TopBar: btn-hints not found");

        Debug.Assert(_dayLabel != null && _daySummary != null, "TopBar: day labels not found");
        Debug.Assert(_btnEndDay != null, "TopBar: btn-end-day not found");
        Debug.Assert(_btnExitRun != null, "TopBar: btn-exit-run not found");
        Debug.Assert(_healthLabel != null && _healthFill != null && _health != null,
            "TopBar: health elements not found");
        Debug.Assert(_threatLabel != null, "TopBar: threat-label not found");
        Debug.Assert(_btnAchievements != null, "TopBar: btn-achievements not found");

        Button btnHelp = root.Q<Button>("btn-help");
        VisualElement helpPopup = root.Q<VisualElement>("help-popup");
        Button btnHelpClose = root.Q<Button>("btn-help-close");
        ScrollView helpContent = root.Q<ScrollView>("help-content");

        Debug.Assert(_energyLabel != null, "TopBar: energy-label not found");
        Debug.Assert(_btnRun != null, "TopBar: btn-run not found");
        Debug.Assert(btnHelp != null && helpPopup != null && btnHelpClose != null && helpContent != null,
            "TopBar: help elements not found");

        _commandHelp = new CommandHelp(btnHelp, helpPopup, btnHelpClose, helpContent);
        _commandHelp.OnOpened += () => OnHelpOpened?.Invoke();

        RegisterCallbacks();
        Add(root);
    }

    /// <summary>
    /// Заполняет справку (кнопка Help): открытые команды и все конструкции языка
    /// (закрытые — с пометкой locked). Вызывается заново после каждого открытия.
    /// </summary>
    public void BuildCommandHelp(
        IEnumerable<ILuaCommand> commands, EnergyConfig energyConfig, UnlockRegistry unlocks)
    {
        _commandHelp.Build(commands, energyConfig, unlocks);
    }

    private void RegisterCallbacks()
    {
        _btnRun.clicked += () => OnRunRequested?.Invoke();
        _btnEndDay.clicked += () => OnEndDayRequested?.Invoke();
        _btnExitRun.clicked += () => OnExitRunRequested?.Invoke();
        _btnSimulation.clicked += () => OnSimulationRequested?.Invoke();

        // UI управляется только мышью — кнопка не должна забирать фокус
        _btnAchievements.focusable = false;
        _btnAchievements.clicked += () => OnAchievementsRequested?.Invoke();

        _btnHints.focusable = false;
        _btnHints.clicked += () => OnHintsToggleRequested?.Invoke();
    }

    /// <summary>Текст кнопки Hints.</summary>
    public void SetHintsEnabled(bool enabled)
    {
        _btnHints.text = Loc.T(enabled ? "Hints: On" : "Hints: Off");
        _btnHints.EnableInClassList("btn-hints--off", !enabled);
    }

    /// <summary>Элемент панели по имени (якорь подсказки туториала).</summary>
    public VisualElement FindElement(string name) => this.Q<VisualElement>(name);

    /// <summary>
    /// День, запуски и итог прошлого дня (штраф — красным, бонус — зелёным).
    /// </summary>
    public void UpdateDay(DayInfo info)
    {
        _dayLabel.text = Loc.F("Day {0} · Runs {1}/{2}", info.Day, info.RunsUsed, info.RunsPerDay);

        _daySummary.text = info.Summary;
        _daySummary.EnableInClassList("day-summary--penalty", info.Summary.StartsWith("−"));
        _daySummary.EnableInClassList("day-summary--bonus", info.Summary.StartsWith("+"));

        UpdateThreat(info);
    }

    /// <summary>
    /// В лагере (режим известен после RunController.Start) индикатор угрозы скрыт.
    /// </summary>
    public void SetHubMode(bool hub)
    {
        _hubMode = hub;
        RefreshThreatVisibility();
    }

    // ─── Угроза ───────────────────────────────────────────────

    /// <summary>
    /// «Threat in N days» → «Threat tomorrow!» → «Threat!» (красным). ThreatDay ≤ 0 — данных об угрозе нет.
    /// </summary>
    private void UpdateThreat(DayInfo info)
    {
        _hasThreatInfo = info.ThreatDay > 0;
        if (_hasThreatInfo)
        {
            bool soon = !info.ThreatActive && info.DaysUntilThreat == 1;
            if (info.ThreatActive)
                _threatLabel.text = Loc.T("Threat!");
            else if (soon)
                _threatLabel.text = Loc.T("Threat tomorrow!");
            else
                _threatLabel.text = Loc.F("Threat in {0} days", info.DaysUntilThreat);

            _threatLabel.tooltip = info.ThreatActive
                ? Loc.F("Enemy patrols roam the map since day {0}", info.ThreatDay)
                : Loc.F("Enemy patrols appear on day {0}", info.ThreatDay);
            _threatLabel.EnableInClassList("threat-label--soon", soon);
            _threatLabel.EnableInClassList("threat-label--active", info.ThreatActive);
        }
        RefreshThreatVisibility();
    }

    private void RefreshThreatVisibility()
    {
        _threatLabel.EnableInClassList("hidden", _hubMode || !_hasThreatInfo);
    }

    /// <summary>
    /// Пока скрипт выполняется, кнопка Run превращается в Stop.
    /// </summary>
    public void SetRunning(bool running)
    {
        _btnRun.text = Loc.T(running ? "Stop" : "Run");
        _btnRun.EnableInClassList("btn-stop", running);
        // Закончить день или выйти из забега во время выполнения нельзя
        _btnEndDay.SetEnabled(!running);
        _btnExitRun.SetEnabled(!running);
    }

    /// <summary>
    /// Стоимость одного прохода программы. Красным — если больше текущего запаса энергии.
    /// </summary>
    public void UpdateProgramCost(float cost)
    {
        if (_programCostLabel == null) return;
        _programCost = cost;
        // Цена одного прохода: циклы и повторные вызовы оплачиваются при каждом выполнении
        _programCostLabel.text = Loc.F("Cost per pass: {0}", cost.ToString("0.#"));
        RefreshCostWarning();
    }

    public void UpdateEnergy(float current, float max)
    {
        if (_energyLabel == null) return;
        _currentEnergy = current;
        _energyLabel.text = Loc.F("Energy: {0} / {1}", current.ToString("0.#"), Mathf.RoundToInt(max));
        RefreshCostWarning();
    }

    public void UpdateHealth(float current, float max)
    {
        _healthLabel.text = Loc.F("HP {0} / {1}", Mathf.CeilToInt(current), Mathf.RoundToInt(max));
        float fraction = max > 0f ? Mathf.Clamp01(current / max) : 0f;
        _healthFill.style.width = Length.Percent(fraction * 100f);

        // Первое значение (_lastHealth < 0) — без флэша
        if (_lastHealth >= 0f && current < _lastHealth) FlashHealth();
        _lastHealth = current;
    }

    /// <summary>Короткая подсветка полосы здоровья при уроне (переходы — в TopBar.uss).</summary>
    private void FlashHealth()
    {
        _health.AddToClassList(HealthHitClass);
        if (_healthFlashReset == null)
            _healthFlashReset = _health.schedule.Execute(ResetHealthFlash).StartingIn(HealthFlashMs);
        else
            _healthFlashReset.ExecuteLater(HealthFlashMs);
    }

    private void ResetHealthFlash() => _health.RemoveFromClassList(HealthHitClass);

    private void RefreshCostWarning()
    {
        _programCostLabel?.EnableInClassList("program-cost--over", _programCost > _currentEnergy);
    }
}
