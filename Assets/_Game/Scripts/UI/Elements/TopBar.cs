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
    private Button _btnInventory;
    private Button _btnRun;
    private Button _btnSimulation;
    private VisualElement _inventoryGrid;
    private InventoryPopupController _inventoryPopup;

    public TopBar(VisualTreeAsset template)
    {
        VisualElement root = template.Instantiate();

        _energyLabel = root.Q<Label>("energy-label");
        _programCostLabel = root.Q<Label>("program-cost");
        _btnInventory = root.Q<Button>("btn-inventory");
        _btnRun = root.Q<Button>("btn-run");
        _btnSimulation = root.Q<Button>("btn-simulation");
        _inventoryGrid = root.Q<VisualElement>("inventory-grid");

        VisualElement popup = root.Q<VisualElement>("inventory-popup");

        Debug.Assert(_energyLabel != null, "TopBar: energy-label not found");
        Debug.Assert(_btnInventory != null, "TopBar: btn-inventory not found");
        Debug.Assert(_btnRun != null, "TopBar: btn-run not found");
        Debug.Assert(popup != null, "TopBar: inventory-popup not found");

        _inventoryPopup = new InventoryPopupController(_btnInventory, popup);

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

    public void UpdateInventory(Dictionary<string, int> items)
    {
        if (_inventoryGrid == null) return;
        _inventoryGrid.Clear();

        foreach (var item in items)
            _inventoryGrid.Add(CreateInventorySlot(item.Key, item.Value));
    }

    private VisualElement CreateInventorySlot(string itemName, int count)
    {
        VisualElement slot = new VisualElement();
        slot.AddToClassList("inventory-slot");

        Label nameLabel = new Label(itemName);
        Label countLabel = new Label(count.ToString());
        countLabel.AddToClassList("inventory-slot-count");

        slot.Add(nameLabel);
        slot.Add(countLabel);

        return slot;
    }
}