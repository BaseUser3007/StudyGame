using System;
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

    public TopBar(VisualTreeAsset template)
    {
        VisualElement root = template.Instantiate();

        _energyLabel = root.Q<Label>("energy-label");
        _programCostLabel = root.Q<Label>("program-cost");
        _btnRun = root.Q<Button>("btn-run");
        _btnSimulation = root.Q<Button>("btn-simulation");

        Debug.Assert(_energyLabel != null, "TopBar: energy-label not found");
        Debug.Assert(_btnRun != null, "TopBar: btn-run not found");

        RegisterCallbacks();
        Add(root);
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
        _programCostLabel.text = $"Program cost: {cost:0.#}";
        _programCostLabel.EnableInClassList("program-cost--over", cost > energyMax);
    }

    public void UpdateEnergy(float current, float max)
    {
        if (_energyLabel == null) return;
        _energyLabel.text = $"Energy: {Mathf.RoundToInt(current)} / {Mathf.RoundToInt(max)}";
    }
}
