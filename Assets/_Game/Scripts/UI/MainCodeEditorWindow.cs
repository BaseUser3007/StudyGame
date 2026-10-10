using UnityEngine;
using UnityEngine.UIElements;

public class MainCodeEditorWindow : CodeEditorWindowBase, IEnergyDisplay
{
    private Label _energyCostLabel;

    public MainCodeEditorWindow(
        string windowId,
        VisualTreeAsset template,
        CodeEditorTheme theme)
        : base(windowId, "Main", template, theme)
    {
    }

    protected override string GetWindowClass() => "window-main";

    protected override void SetupWindowButtons()
    {
        _energyCostLabel = _window.Q<Label>("energy-cost");
    }

    public void UpdateEnergyCost(float cost)
    {
        if (_energyCostLabel == null) return;
        _energyCostLabel.text = Mathf.RoundToInt(cost).ToString();
    }
}