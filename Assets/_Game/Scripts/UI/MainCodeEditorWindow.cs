using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Главное окно кода. Внизу — лог запуска (RunLog): итог Run, уведомления забега и ошибки
/// с объяснением для новичка.
/// </summary>
public class MainCodeEditorWindow : CodeEditorWindowBase, IEnergyDisplay, INotificationDisplay, ICodeErrorDisplay
{
    public event Action OnAddMethodRequested;

    private Label _energyCostLabel;
    private Button _btnAddMethod;
    private RunLog _runLog;

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

        _runLog = new RunLog(_window.Q<VisualElement>("run-log"), _context?.Theme);
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

    // ─── Лог запуска ──────────────────────────────────────────

    public void ShowNotification(string message, LogEntryType type) => _runLog?.AddMessage(message, type);

    public void ShowCodeError(LuaErrorExplanation error) => _runLog?.AddCodeError(error);

    /// <summary>Новый запуск — старые записи уже не нужны.</summary>
    public void ClearLog() => _runLog?.Clear();
}
