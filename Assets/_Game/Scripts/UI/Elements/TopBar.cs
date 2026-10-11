using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class TopBar : VisualElement
{
    public event Action OnRunRequested;
    public event Action OnEndDayRequested;
    public event Action OnSimulationRequested;

    private Label _energyLabel;
    private Label _programCostLabel;
    private Label _dayLabel;
    private Label _daySummary;
    private Button _btnRun;
    private Button _btnEndDay;
    private Button _btnSimulation;
    private CommandHelp _commandHelp;

    // Цена прохода краснеет, если больше текущего запаса энергии
    private float _currentEnergy = float.MaxValue;
    private float _programCost;

    public TopBar(VisualTreeAsset template)
    {
        VisualElement root = template.Instantiate();

        _energyLabel = root.Q<Label>("energy-label");
        _programCostLabel = root.Q<Label>("program-cost");
        _dayLabel = root.Q<Label>("day-label");
        _daySummary = root.Q<Label>("day-summary");
        _btnRun = root.Q<Button>("btn-run");
        _btnEndDay = root.Q<Button>("btn-end-day");
        _btnSimulation = root.Q<Button>("btn-simulation");

        Debug.Assert(_dayLabel != null && _daySummary != null, "TopBar: day labels not found");
        Debug.Assert(_btnEndDay != null, "TopBar: btn-end-day not found");

        Button btnHelp = root.Q<Button>("btn-help");
        VisualElement helpPopup = root.Q<VisualElement>("help-popup");
        Button btnHelpClose = root.Q<Button>("btn-help-close");
        ScrollView helpContent = root.Q<ScrollView>("help-content");

        Debug.Assert(_energyLabel != null, "TopBar: energy-label not found");
        Debug.Assert(_btnRun != null, "TopBar: btn-run not found");
        Debug.Assert(btnHelp != null && helpPopup != null && btnHelpClose != null && helpContent != null,
            "TopBar: help elements not found");

        _commandHelp = new CommandHelp(btnHelp, helpPopup, btnHelpClose, helpContent);

        RegisterCallbacks();
        Add(root);
    }

    /// <summary>
    /// Заполняет справку по командам (кнопка Help).
    /// </summary>
    public void BuildCommandHelp(IEnumerable<ILuaCommand> commands, EnergyConfig energyConfig)
    {
        _commandHelp.Build(commands, energyConfig);
    }

    private void RegisterCallbacks()
    {
        _btnRun.clicked += () => OnRunRequested?.Invoke();
        _btnEndDay.clicked += () => OnEndDayRequested?.Invoke();
        _btnSimulation.clicked += () => OnSimulationRequested?.Invoke();
    }

    /// <summary>
    /// День, запуски и итог прошлого дня (штраф — красным, бонус — зелёным).
    /// </summary>
    public void UpdateDay(DayInfo info)
    {
        _dayLabel.text = $"Day {info.Day} · Runs {info.RunsUsed}/{info.RunsPerDay}";

        _daySummary.text = info.Summary;
        _daySummary.EnableInClassList("day-summary--penalty", info.Summary.StartsWith("−"));
        _daySummary.EnableInClassList("day-summary--bonus", info.Summary.StartsWith("+"));
    }

    /// <summary>
    /// Пока скрипт выполняется, кнопка Run превращается в Stop.
    /// </summary>
    public void SetRunning(bool running)
    {
        _btnRun.text = running ? "Stop" : "Run";
        _btnRun.EnableInClassList("btn-stop", running);
        // Закончить день во время выполнения нельзя
        _btnEndDay.SetEnabled(!running);
    }

    /// <summary>
    /// Стоимость одного прохода программы. Красным — если больше текущего запаса энергии.
    /// </summary>
    public void UpdateProgramCost(float cost)
    {
        if (_programCostLabel == null) return;
        _programCost = cost;
        // Цена одного прохода: циклы и повторные вызовы оплачиваются при каждом выполнении
        _programCostLabel.text = $"Cost per pass: {cost:0.#}";
        RefreshCostWarning();
    }

    public void UpdateEnergy(float current, float max)
    {
        if (_energyLabel == null) return;
        _currentEnergy = current;
        _energyLabel.text = $"Energy: {current:0.#} / {Mathf.RoundToInt(max)}";
        RefreshCostWarning();
    }

    private void RefreshCostWarning()
    {
        _programCostLabel?.EnableInClassList("program-cost--over", _programCost > _currentEnergy);
    }
}
