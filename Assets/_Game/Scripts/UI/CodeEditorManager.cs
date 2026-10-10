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
    private float _energyMax = 100f;

    public TopBar TopBar => _topBar;

    // ─── Инициализация ────────────────────────────────────────

    public void Initialize(LuaSandbox luaSandbox, UIManager uiManager)
    {
        _luaSandbox = luaSandbox;
        _uiManager = uiManager;
        _uiDocument = GetComponent<UIDocument>();

        ValidateReferences();

        _container = _uiDocument.rootVisualElement;

        PlayerEventBus.OnUIFocused += () => _isUIFocused = true;
        PlayerEventBus.OnUIUnfocused += () => _isUIFocused = false;

        _container.RegisterCallback<NavigationMoveEvent>(evt =>
        {
            if (!_isUIFocused) evt.StopPropagation();
        }, TrickleDown.TrickleDown);

        _container.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (!_isUIFocused) evt.StopImmediatePropagation();
        }, TrickleDown.TrickleDown);

        CreateTopBar();
        CreateMainWindow();
    }

    public void SetEnergyMax(float max)
    {
        _energyMax = max;
        UIEventBus.PublishPlayerEnergyChanged(_energyMax, _energyMax);
    }

    private void OnDestroy()
    {
        PlayerEventBus.OnUIFocused -= () => _isUIFocused = true;
        PlayerEventBus.OnUIUnfocused -= () => _isUIFocused = false;
    }

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
        _topBar.OnAddWindowRequested += CreateAuxiliaryWindow;
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
            theme);

        _mainWindow.SetPosition(20, 50);
        _mainWindow.OnCodeChanged += HandleCodeChanged;
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
            theme);

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
    }

    private async void HandleRunRequested()
    {
        if (_mainWindow == null || _luaSandbox == null) return;

        string mainCode = _mainWindow.Code;

        // Вспомогательные окна — просто их код без ключей
        var auxiliaryWindows = new Dictionary<string, string>();
        foreach (CodeEditorWindowBase window in _windows)
            if (window != _mainWindow)
                auxiliaryWindows[window.Title] = window.Code;

        ExecutionResult result = await _luaSandbox.ExecuteAsync(mainCode, auxiliaryWindows);

        if (result.IsSuccess)
            UIEventBus.PublishNotification("Execution completed", LogEntryType.Success);
        else if (!result.IsCancelled)
            UIEventBus.PublishNotification(result.ErrorMessage, LogEntryType.Error);
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