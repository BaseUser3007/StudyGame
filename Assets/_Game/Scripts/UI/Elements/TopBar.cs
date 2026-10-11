using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class TopBar : VisualElement
{
    public event Action OnRunRequested;
    public event Action OnSimulationRequested;

    private Label _energyLabel;
    private Label _programCostLabel;
    private Button _btnRun;
    private Button _btnSimulation;
    private CommandHelp _commandHelp;

    public TopBar(VisualTreeAsset template)
    {
        VisualElement root = template.Instantiate();

        _energyLabel = root.Q<Label>("energy-label");
        _programCostLabel = root.Q<Label>("program-cost");
        _btnRun = root.Q<Button>("btn-run");
        _btnSimulation = root.Q<Button>("btn-simulation");

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
        _btnSimulation.clicked += () => OnSimulationRequested?.Invoke();
    }

    /// <summary>
    /// Пока скрипт выполняется, кнопка Run превращается в Stop.
    /// </summary>
    public void SetRunning(bool running)
    {
        _btnRun.text = running ? "Stop" : "Run";
        _btnRun.EnableInClassList("btn-stop", running);
    }

    /// <summary>
    /// Стоимость всей программы. Красным — если энергии не хватит.
    /// </summary>
    public void UpdateProgramCost(float cost, float energyMax)
    {
        if (_programCostLabel == null) return;
        // Цена одного прохода: циклы и повторные вызовы оплачиваются при каждом выполнении
        _programCostLabel.text = $"Cost per pass: {cost:0.#}";
        _programCostLabel.EnableInClassList("program-cost--over", cost > energyMax);
    }

    public void UpdateEnergy(float current, float max)
    {
        if (_energyLabel == null) return;
        _energyLabel.text = $"Energy: {Mathf.RoundToInt(current)} / {Mathf.RoundToInt(max)}";
    }
}
