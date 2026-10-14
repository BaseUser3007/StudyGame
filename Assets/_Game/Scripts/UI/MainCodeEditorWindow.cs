using System;
using UnityEngine;
using UnityEngine.UIElements;

public class MainCodeEditorWindow : CodeEditorWindowBase, IEnergyDisplay
{
    public event Action OnAddMethodRequested;

    private Label _energyCostLabel;
    private Button _btnAddMethod;

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

        _btnAddMethod = _window.Q<Button>("btn-add-method");
        Debug.Assert(_btnAddMethod != null, "MainCodeEditorWindow: btn-add-method not found");

        _btnAddMethod.clicked += () => OnAddMethodRequested?.Invoke();

        // Кнопка лежит в заголовке — нажатие не должно начинать перетаскивание окна
        _btnAddMethod.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
    }

    /// <summary>
    /// Окна-«классы» ещё не открыты — кнопка «+» скрыта.
    /// </summary>
    public void SetAddMethodAvailable(bool available)
    {
        _btnAddMethod?.EnableInClassList("hidden", !available);
    }

    public void UpdateEnergyCost(float cost)
    {
        if (_energyCostLabel == null) return;
        _energyCostLabel.text = $"Cost {cost:0.#}";
    }
}