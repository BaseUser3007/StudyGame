using System;
using UnityEngine;
using UnityEngine.UIElements;

public class MainCodeEditorWindow : CodeEditorWindowBase, IEnergyDisplay
{
    public event Action OnAddMethodRequested;

    private Label _energyCostLabel;

    public MainCodeEditorWindow(
        string windowId,
        VisualTreeAsset template,
        CodeEditorContext context)
        : base(windowId, "Main", template, context)
    {
    }

    protected override string GetWindowClass() => "window-main";

    protected override void SetupWindowButtons()
    {
        _energyCostLabel = _window.Q<Label>("energy-cost");

        Button btnAddMethod = _window.Q<Button>("btn-add-method");
        Debug.Assert(btnAddMethod != null, "MainCodeEditorWindow: btn-add-method not found");

        btnAddMethod.clicked += () => OnAddMethodRequested?.Invoke();

        // Кнопка лежит в заголовке — нажатие не должно начинать перетаскивание окна
        btnAddMethod.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
    }

    public void UpdateEnergyCost(float cost)
    {
        if (_energyCostLabel == null) return;
        _energyCostLabel.text = $"Cost {cost:0.#}";
    }
}