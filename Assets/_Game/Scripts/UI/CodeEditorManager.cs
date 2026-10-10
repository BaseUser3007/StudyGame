using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

public class CodeEditorManager : MonoBehaviour
{
    [SerializeField] private VisualTreeAsset mainWindowTemplate;
    [SerializeField] private VisualTreeAsset auxiliaryWindowTemplate;
    [SerializeField] private VisualTreeAsset topBarTemplate;
    [SerializeField] private CodeEditorTheme theme;

    private LuaSandbox _luaSandbox;
    private UIManager _uiManager;
    private UIDocument _uiDocument;
    private VisualElement _container;
    private List<CodeEditorWindowBase> _windows = new List<CodeEditorWindowBase>();
    private MainCodeEditorWindow _mainWindow;
    private TopBar _topBar;
    private int _windowCount = 0;
    private bool _isUIFocused;
    private bool _navigationBlocked;
    private float _energyMax = 100f;
    private CodeEditorContext _context;

    public TopBar TopBar => _topBar;

    // ─── Инициализация ────────────────────────────────────────

    public void Initialize(LuaSandbox luaSandbox, UIManager uiManager)
    {
        _luaSandbox = luaSandbox;
        _uiManager = uiManager;
        _uiDocument = GetComponent<UIDocument>();

        ValidateReferences();

        _container = _uiDocument.rootVisualElement;

        PlayerEventBus.OnUIFocused += HandleUIFocused;
        PlayerEventBus.OnUIUnfocused += HandleUIUnfocused;

        // Без фокуса навигация адресована корню панели и мимо _container не проходит,
        // поэтому перехватываем её на самом корне
        if (_container.panel != null)
            BlockKeyboardNavigation(_container.panel.visualTree);
        else
            _container.RegisterCallback<AttachToPanelEvent>(
                evt => BlockKeyboardNavigation(evt.destinationPanel.visualTree));

        _container.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (!_isUIFocused) evt.StopImmediatePropagation();
        }, TrickleDown.TrickleDown);

        _context = CreateContext();

        CreateTopBar();
        CreateMainWindow();

        _luaSandbox.OnRunningChanged += HandleRunningChanged;
    }

    /// <summary>
    /// Клавиатурная навигация UI Toolkit (WASD/стрелки/Tab/Enter) не должна трогать интерфейс:
    /// те же клавиши двигают камеру. Иначе фокус перескакивает в поле кода
    /// (и выделяет весь текст) или на кнопки верхней панели.
    /// Интерфейс управляется только мышью.
    /// </summary>
    private void BlockKeyboardNavigation(VisualElement panelRoot)
    {
        if (_navigationBlocked) return;
        _navigationBlocked = true;

        panelRoot.RegisterCallback<NavigationMoveEvent>(IgnoreNavigation, TrickleDown.TrickleDown);
        panelRoot.RegisterCallback<NavigationSubmitEvent>(IgnoreNavigation, TrickleDown.TrickleDown);
        panelRoot.RegisterCallback<NavigationCancelEvent>(IgnoreNavigation, TrickleDown.TrickleDown);
    }

    private void IgnoreNavigation(EventBase evt)
    {
        // Фокус от навигации никогда не меняется
        (evt.target as Focusable)?.focusController?.IgnoreEvent(evt);

        // Пока пишут код, событие доходит до поля ввода — оно само решает, что с ним делать.
        // Иначе (например, Enter после клика по кнопке Run) событие гасится целиком.
        if (!_isUIFocused)
            evt.StopPropagation();
    }

    public void SetEnergyMax(float max)
    {
        _energyMax = max;
        UIEventBus.PublishPlayerEnergyChanged(_energyMax, _energyMax);
        UpdateProgramCost();
    }

    private CodeEditorContext CreateContext()
    {
        LuaCommandRegistry registry = _luaSandbox.Registry;

        var apiNames = new List<string>();
        foreach (ILuaCommand command in registry.GetAllCommands())
            apiNames.Add(command.Name);
        apiNames.Add("getNear");

        var completion = new LuaCompletionProvider(
            registry.GetAllCommands,
            GetWindowFunctionNames);

        return new CodeEditorContext(
            theme,
            new LuaSyntaxHighlighter(theme, apiNames),
            completion,
            _luaSandbox.CalculateLineCosts);
    }

    private IEnumerable<string> GetWindowFunctionNames()
    {
        foreach (CodeEditorWindowBase window in _windows)
            if (window != _mainWindow)
                yield return window.Title;
    }

    private void OnDestroy()
    {
        PlayerEventBus.OnUIFocused -= HandleUIFocused;
        PlayerEventBus.OnUIUnfocused -= HandleUIUnfocused;

        if (_luaSandbox != null)
            _luaSandbox.OnRunningChanged -= HandleRunningChanged;
    }

    private void HandleUIFocused() => _isUIFocused = true;
    private void HandleUIUnfocused() => _isUIFocused = false;

    // ─── Validation ───────────────────────────────────────────

    private void ValidateReferences()
    {
        Debug.Assert(_uiDocument != null, "CodeEditorManager: UIDocument not found");
        Debug.Assert(mainWindowTemplate != null, "CodeEditorManager: mainWindowTemplate is not assigned");
        Debug.Assert(auxiliaryWindowTemplate != null, "CodeEditorManager: auxiliaryWindowTemplate is not assigned");
        Debug.Assert(topBarTemplate != null, "CodeEditorManager: topBarTemplate is not assigned");
        Debug.Assert(theme != null, "CodeEditorManager: theme is not assigned");
        Debug.Assert(_luaSandbox != null, "CodeEditorManager: LuaSandbox is not assigned");
        Debug.Assert(_uiManager != null, "CodeEditorManager: UIManager is not assigned");
    }

    // ─── TopBar ───────────────────────────────────────────────

    private void CreateTopBar()
    {
        _topBar = new TopBar(topBarTemplate);
        _topBar.OnRunRequested += HandleRunRequested;
        _topBar.OnSimulationRequested += HandleSimulationRequested;
        _container.Add(_topBar);
    }

    // ─── Window Management ────────────────────────────────────

    private void CreateMainWindow()
    {
        _windowCount++;
        _mainWindow = new MainCodeEditorWindow(
            $"window_{_windowCount}",
            mainWindowTemplate,
            _context);

        _mainWindow.SetPosition(20, 50);
        _mainWindow.OnCodeChanged += HandleCodeChanged;
        _mainWindow.OnAddMethodRequested += CreateAuxiliaryWindow;
        _uiManager.RegisterWindow(_mainWindow);
        _container.Add(_mainWindow);
        _windows.Add(_mainWindow);
    }

    public void CreateAuxiliaryWindow()
    {
        _windowCount++;
        string title = $"Class_{_windowCount}";

        AuxiliaryCodeEditorWindow window = new AuxiliaryCodeEditorWindow(
            $"window_{_windowCount}",
            title,
            auxiliaryWindowTemplate,
            _context);

        float offset = (_windows.Count % 5) * 30f;
        window.SetPosition(20 + offset, 50 + offset);
        window.OnCloseRequested += HandleCloseRequested;
        window.OnCodeChanged += HandleCodeChanged;
        _uiManager.RegisterWindow(window);
        _container.Add(window);
        _windows.Add(window);
    }

    // ─── Event Handlers ───────────────────────────────────────

    private void HandleCodeChanged(CodeEditorWindowBase changedWindow, string newCode)
    {
        float cost = _luaSandbox.CalculateEnergyCost(
            new Dictionary<string, string> { { changedWindow.Title, newCode } });

        UIEventBus.PublishWindowEnergyCostChanged(changedWindow.WindowId, cost);
        UpdateProgramCost();
    }

    /// <summary>
    /// Стоимость всей программы — сумма всех окон, показывается рядом с энергией.
    /// </summary>
    private void UpdateProgramCost()
    {
        if (_luaSandbox == null) return;
        float total = _luaSandbox.CalculateEnergyCost(GetAllWindowCode());
        _topBar?.UpdateProgramCost(total, _energyMax);
    }

    private async void HandleRunRequested()
    {
        if (_mainWindow == null || _luaSandbox == null) return;

        // Повторное нажатие во время выполнения — остановка
        if (_luaSandbox.IsRunning)
        {
            _luaSandbox.Stop();
            return;
        }

        string mainCode = _mainWindow.Code;

        // Вспомогательные окна — просто их код без ключей
        var auxiliaryWindows = new Dictionary<string, string>();
        foreach (CodeEditorWindowBase window in _windows)
            if (window != _mainWindow)
                auxiliaryWindows[window.Title] = window.Code;

        ExecutionResult result = await _luaSandbox.ExecuteAsync(mainCode, auxiliaryWindows);

        if (result.IsSuccess)
            UIEventBus.PublishNotification("Execution completed", LogEntryType.Success);
        else if (result.IsCancelled)
            UIEventBus.PublishNotification("Execution stopped", LogEntryType.Warning);
        else
            UIEventBus.PublishNotification(result.ErrorMessage, LogEntryType.Error);
    }

    private void HandleRunningChanged(bool running)
    {
        _topBar?.SetRunning(running);
    }

    private void HandleSimulationRequested()
    {
        Debug.Log("Simulation requested");
    }

    private void HandleCloseRequested(CodeEditorWindowBase window)
    {
        _uiManager.UnregisterWindow(window);
        _windows.Remove(window);
        window.DestroyWindow();
        UpdateProgramCost();
    }

    // ─── Public API ───────────────────────────────────────────

    public Dictionary<string, string> GetAllWindowCode()
    {
        var result = new Dictionary<string, string>();
        foreach (CodeEditorWindowBase window in _windows)
            result[window.Title] = window.Code;
        return result;
    }

    public MainCodeEditorWindow MainWindow => _mainWindow;
}