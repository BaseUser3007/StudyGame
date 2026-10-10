using UnityEngine;
using UnityEngine.UIElements;

public class AuxiliaryCodeEditorWindow : CodeEditorWindowBase, IEnergyDisplay
{
    private Label _energyCostLabel;
    private Button _btnDelete;
    private TitleEditor _titleEditor;

    public AuxiliaryCodeEditorWindow(
        string windowId,
        string title,
        VisualTreeAsset template,
        CodeEditorContext context)
        : base(windowId, title, template, context)
    {
    }

    protected override string GetWindowClass() => "window-auxiliary";

    protected override void SetupWindowButtons()
    {
        _energyCostLabel = _window.Q<Label>("energy-cost");
        _btnDelete = _window.Q<Button>("btn-delete");

        if (_btnClose != null)
            _btnClose.clicked += PublishCloseRequested;

        if (_btnDelete != null)
            _btnDelete.clicked += PublishCloseRequested;

        Label titleLabel = _window.Q<Label>("title");
        TextField titleEdit = _window.Q<TextField>("title-edit");

        if (titleLabel != null && titleEdit != null)
        {
            _titleEditor = new TitleEditor(
                titleLabel,
                titleEdit,
                new LuaIdentifierValidator());

            _titleEditor.OnTitleChanged += newTitle =>
            {
                _titleEditor.SetTitle(newTitle);
                SetTitle(newTitle);
            };
        }
    }

    // ─── IEnergyDisplay ───────────────────────────────────────

    public void UpdateEnergyCost(float cost)
    {
        if (_energyCostLabel == null) return;
        _energyCostLabel.text = $"Cost {cost:0.#}";
    }
}