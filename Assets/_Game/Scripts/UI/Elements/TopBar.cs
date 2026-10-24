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
    public event Action OnContinueRequested;
    public event Action OnStepRequested;

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
    private Label _pausedLabel;
    private Button _btnContinue;
    private Button _btnStep;
    private CommandHelp _commandHelp;

    // Цена прохода краснеет, если больше текущего запаса энергии
    private float _currentEnergy = float.MaxValue;
    private float _programCost;

    // Флэш полосы здоровья при уроне
    private const string HealthHitClass = "health--hit";
    private const long HealthFlashMs = 180;
    private float _lastHealth = -1f;
    private IVisualElementScheduledItem _healthFlashReset;

    // Всплывающие «−N»/«+N» у энергии: мелкие изменения копятся окном и показываются одной суммой
    private const long FloaterWindowMs = 250;
    private const long FloaterLifeMs = 1100;
    private const float FloaterMinAmount = 0.05f;
    private const float FloaterBigAmount = 10f;
    private const int FloaterColumns = 3;
    private VisualElement _floaterLayer;
    private float _lastEnergy = -1f;
    private float _lastMax = -1f;
    private float _pendingSpend;
    private float _pendingGain;
    // Последнее изменение запаса — его отменяет UpdateDay, если оно было пополнением нового дня
    private float _lastEnergyDelta;
    private int _lastDay;
    private int _floaterSerial;
    private IVisualElementScheduledItem _floaterFlush;

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
        _pausedLabel = root.Q<Label>("paused-label");
        _btnContinue = root.Q<Button>("btn-continue");
        _btnStep = root.Q<Button>("btn-step");
        Debug.Assert(_pausedLabel != null && _btnContinue != null && _btnStep != null,
            "TopBar: debugger elements not found");

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
        CreateFloaterLayer();
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

        _btnContinue.focusable = false;
        _btnContinue.clicked += () => OnContinueRequested?.Invoke();
        _btnStep.focusable = false;
        _btnStep.clicked += () => OnStepRequested?.Invoke();
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

        // DayCycle публикует день сразу после PlayerEnergy.SetMax: пополнение запаса нового дня — не трата и не еда
        if (_lastDay > 0 && info.Day != _lastDay) DiscardLastEnergyDelta();
        _lastDay = info.Day;

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
    /// Код стоит на точке останова: метка «Paused · окно, строка N» и кнопки Continue / Step (null — пауза снята).
    /// </summary>
    public void SetPaused(string window, int line)
    {
        bool paused = line > 0;
        _pausedLabel.text = paused ? Loc.F("Paused · {0}, line {1}", window, line) : string.Empty;
        _pausedLabel.EnableInClassList("hidden", !paused);
        _btnContinue.EnableInClassList("hidden", !paused);
        _btnStep.EnableInClassList("hidden", !paused);
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
        TrackEnergyDelta(current, max);
    }

    // ─── Всплывающие изменения энергии ───────────────────────

    /// <summary>
    /// Слой под надписью энергии: в нём живут числа «−N»/«+N». Мышь и фокус не забирает.
    /// </summary>
    private void CreateFloaterLayer()
    {
        if (_energyLabel == null) return;

        _floaterLayer = new VisualElement { name = "energy-floaters", pickingMode = PickingMode.Ignore, focusable = false };
        _floaterLayer.AddToClassList("energy-floaters");

        // Слой — сосед надписи, а не её ребёнок: у Label с детьми пропадает измерение по тексту
        // и надпись схлопывается, наезжая на соседей. Позицию берём из раскладки надписи
        VisualElement host = _energyLabel.parent ?? _energyLabel;
        host.Add(_floaterLayer);
        _energyLabel.RegisterCallback<GeometryChangedEvent>(_ => PositionFloaterLayer());
    }

    private void PositionFloaterLayer()
    {
        if (_floaterLayer == null || _floaterLayer.parent == _energyLabel) return;
        Rect label = _energyLabel.layout;
        _floaterLayer.style.left = label.x;
        _floaterLayer.style.top = label.yMax;
    }

    /// <summary>
    /// Разница с прошлым значением. Первое значение и любое изменение максимума (SetMax в начале дня,
    /// награды к лимиту) не показываются; остальное копится и выводится раз в FloaterWindowMs.
    /// </summary>
    private void TrackEnergyDelta(float current, float max)
    {
        bool first = _lastEnergy < 0f;
        bool maxChanged = !first && !Mathf.Approximately(max, _lastMax);
        float delta = first ? 0f : current - _lastEnergy;
        _lastEnergy = current;
        _lastMax = max;

        _lastEnergyDelta = 0f;
        if (first || maxChanged || Mathf.Abs(delta) < 0.0001f) return;

        _lastEnergyDelta = delta;
        if (delta < 0f) _pendingSpend -= delta;
        else _pendingGain += delta;
        ScheduleFloaterFlush();
    }

    /// <summary>Убирает из накопленного последнее изменение запаса (пополнение при смене дня).</summary>
    private void DiscardLastEnergyDelta()
    {
        if (_lastEnergyDelta < 0f) _pendingSpend = Mathf.Max(0f, _pendingSpend + _lastEnergyDelta);
        else if (_lastEnergyDelta > 0f) _pendingGain = Mathf.Max(0f, _pendingGain - _lastEnergyDelta);
        _lastEnergyDelta = 0f;
    }

    private void ScheduleFloaterFlush()
    {
        if (_floaterLayer == null) return;
        // Окно отсчитывается от первого изменения: в длинном цикле числа идут равномерно, а не копятся бесконечно
        if (_floaterFlush == null)
            _floaterFlush = _floaterLayer.schedule.Execute(FlushFloaters).StartingIn(FloaterWindowMs);
        else if (!_floaterFlush.isActive)
            _floaterFlush.ExecuteLater(FloaterWindowMs);
    }

    private void FlushFloaters()
    {
        float spend = _pendingSpend;
        float gain = _pendingGain;
        _pendingSpend = 0f;
        _pendingGain = 0f;
        _lastEnergyDelta = 0f;

        if (spend >= FloaterMinAmount) SpawnFloater(-spend);
        if (gain >= FloaterMinAmount) SpawnFloater(gain);
    }

    /// <summary>
    /// Одно число: появляется под надписью, через кадр получает класс полёта (переход вверх с угасанием
    /// задан в TopBar.uss) и удаляется по окончании.
    /// </summary>
    private void SpawnFloater(float amount)
    {
        bool gain = amount > 0f;
        string text = (gain ? "+" : "−") + Mathf.Abs(amount).ToString("0.#");

        var floater = new Label(text) { pickingMode = PickingMode.Ignore, focusable = false };
        floater.AddToClassList("energy-floater");
        floater.AddToClassList(gain ? "energy-floater--gain" : "energy-floater--spend");
        floater.EnableInClassList("energy-floater--big", Mathf.Abs(amount) >= FloaterBigAmount);
        // Соседние числа сдвинуты по колонкам, чтобы не накладывались друг на друга
        floater.style.left = (_floaterSerial++ % FloaterColumns) * 34f;
        _floaterLayer.Add(floater);

        floater.schedule.Execute(() => floater.AddToClassList("energy-floater--fly")).StartingIn(16);
        floater.schedule.Execute(floater.RemoveFromHierarchy).StartingIn(FloaterLifeMs);
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
