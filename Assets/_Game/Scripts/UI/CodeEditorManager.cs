using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

public class CodeEditorManager : MonoBehaviour, IPointerOverUIProvider
{
    [SerializeField] private VisualTreeAsset mainWindowTemplate;
    [SerializeField] private VisualTreeAsset auxiliaryWindowTemplate;
    [SerializeField] private VisualTreeAsset topBarTemplate;
    [SerializeField] private VisualTreeAsset inventoryBarTemplate;
    [SerializeField] private VisualTreeAsset craftingPanelTemplate;
    [SerializeField] private VisualTreeAsset codeLibraryTemplate;
    [SerializeField] private VisualTreeAsset runScreensTemplate;
    [SerializeField] private CodeEditorTheme theme;

    private LuaSandbox _luaSandbox;
    private UIManager _uiManager;
    private UIDocument _uiDocument;
    private VisualElement _container;
    // Слой окон кода — ниже панелей, чтобы окна не перекрывали инвентарь и крафт
    private VisualElement _windowLayer;
    private List<CodeEditorWindowBase> _windows = new List<CodeEditorWindowBase>();
    private MainCodeEditorWindow _mainWindow;
    private TopBar _topBar;
    private InventoryBar _inventoryBar;
    private CraftingPanel _craftingPanel;
    private CodeLibrary _codeLibrary;
    private CodeLibraryPanel _codeLibraryPanel;
    private RunScreens _runScreens;
    private readonly LuaIdentifierValidator _identifierValidator = new LuaIdentifierValidator();
    private RecipeBook _recipeBook;
    private IStationProvider _stations;
    private DayCycle _dayCycle;
    private AchievementTracker _achievements;
    private UnlockRegistry _unlocks;
    private AchievementsPanel _achievementsPanel;
    private AchievementToasts _achievementToasts;
    private UnlockPopup _unlockPopup;
    private int _windowCount = 0;
    private bool _isUIFocused;
    private bool _navigationBlocked;
    private CodeEditorContext _context;

    public TopBar TopBar => _topBar;
    public InventoryBar InventoryBar => _inventoryBar;
    public CraftingPanel CraftingPanel => _craftingPanel;
    public RunScreens RunScreens => _runScreens;

    // ─── Инициализация ────────────────────────────────────────

    public void Initialize(
        LuaSandbox luaSandbox, UIManager uiManager, RecipeBook recipeBook, IStationProvider stations,
        DayCycle dayCycle, AchievementTracker achievements)
    {
        _luaSandbox = luaSandbox;
        _uiManager = uiManager;
        _recipeBook = recipeBook;
        _stations = stations;
        _dayCycle = dayCycle;
        _achievements = achievements;
        _unlocks = luaSandbox != null ? luaSandbox.Unlocks : null;
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

        CreateWindowLayer();
        CreateTopBar();
        CreateInventoryBar();
        CreateCraftingPanel();
        CreateCodeLibraryPanel();
        CreateMainWindow();

        // Справка выпадает из верхней панели справа — поверх панели крафта
        _topBar.BringToFront();
        RebuildCommandHelp();
        ApplyClassesAvailability();

        // Лагерь, диалоги и итоги забега — поверх всего, включая верхнюю панель
        _runScreens = new RunScreens(runScreensTemplate);
        _container.Add(_runScreens);
        CreateAchievementOverlays();

        _luaSandbox.OnRunningChanged += HandleRunningChanged;
        _luaSandbox.OnUnlocksApplied += HandleUnlocksApplied;
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

    private CodeEditorContext CreateContext()
    {
        LuaCommandRegistry registry = _luaSandbox.Registry;

        // Реестр читается на каждом запросе — открытые позже команды появляются сами
        var completion = new LuaCompletionProvider(
            registry.GetAllCommands,
            GetWindowFunctionNames);

        return new CodeEditorContext(
            theme,
            new LuaSyntaxHighlighter(theme, BuildApiNames(), IsKeywordLocked),
            completion,
            _luaSandbox.CalculateLineCosts,
            IsKeywordLocked);
    }

    /// <summary>
    /// Имена, которые подсвечиваются как API: открытые команды и методы массива findObjects.
    /// </summary>
    private List<string> BuildApiNames()
    {
        var apiNames = new List<string>();
        foreach (ILuaCommand command in _luaSandbox.Registry.GetAllCommands())
            apiNames.Add(command.Name);
        foreach (var method in LuaObjectList.Methods)
            apiNames.Add(method.Name);
        return apiNames;
    }

    /// <summary>
    /// Ключевое слово относится к ещё закрытой конструкции языка (for, while, function…).
    /// </summary>
    private bool IsKeywordLocked(string keyword)
    {
        UnlockRegistry unlocks = _luaSandbox.Unlocks;
        if (LuaConstructs.IsForbiddenKeyword(keyword)) return true;
        if (unlocks == null) return false;

        string id = LuaConstructs.GetConstructForKeyword(keyword);
        return id != null && !unlocks.IsConstructUnlocked(id);
    }

    private bool AreClassesUnlocked()
    {
        UnlockRegistry unlocks = _luaSandbox.Unlocks;
        return unlocks == null || unlocks.IsConstructUnlocked(LuaConstructs.Classes);
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
        {
            _luaSandbox.OnRunningChanged -= HandleRunningChanged;
            _luaSandbox.OnUnlocksApplied -= HandleUnlocksApplied;
        }

        if (_achievements != null)
        {
            _achievements.OnAchievementCompleted -= HandleAchievementCompleted;
            _achievements.OnProgressChanged -= HandleAchievementProgressChanged;
        }

        if (_unlocks != null)
            _unlocks.OnUnlocked -= HandleUnlocked;
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
        Debug.Assert(inventoryBarTemplate != null, "CodeEditorManager: inventoryBarTemplate is not assigned");
        Debug.Assert(craftingPanelTemplate != null, "CodeEditorManager: craftingPanelTemplate is not assigned");
        Debug.Assert(codeLibraryTemplate != null, "CodeEditorManager: codeLibraryTemplate is not assigned");
        Debug.Assert(runScreensTemplate != null, "CodeEditorManager: runScreensTemplate is not assigned");
        Debug.Assert(_recipeBook != null, "CodeEditorManager: RecipeBook is not assigned");
        Debug.Assert(theme != null, "CodeEditorManager: theme is not assigned");
        Debug.Assert(_luaSandbox != null, "CodeEditorManager: LuaSandbox is not assigned");
        Debug.Assert(_uiManager != null, "CodeEditorManager: UIManager is not assigned");
        if (_achievements == null)
            Debug.LogWarning("CodeEditorManager: AchievementTracker is null — the achievements panel will be empty");
    }

    // ─── Достижения и открытия ────────────────────────────────

    /// <summary>
    /// Панель достижений, окно «Unlocked: X» и тосты — шаблоны внутри RunScreens.uxml (поверх экранов
    /// лагеря и итогов). Панель открывается кнопкой Achievements в верхней панели и в лагере.
    /// </summary>
    private void CreateAchievementOverlays()
    {
        _achievementsPanel = new AchievementsPanel(_runScreens.GetOverlay("achievements-overlay"), _achievements);
        _unlockPopup = new UnlockPopup(
            _runScreens.GetOverlay("unlock-overlay"), _luaSandbox, _recipeBook, _context.Highlighter);
        _achievementToasts = new AchievementToasts(_runScreens.GetOverlay("toast-overlay"));

        _topBar.OnAchievementsRequested += HandleAchievementsRequested;
        _runScreens.OnAchievementsRequested += HandleAchievementsRequested;

        if (_achievements != null)
        {
            _achievements.OnAchievementCompleted += HandleAchievementCompleted;
            _achievements.OnProgressChanged += HandleAchievementProgressChanged;
        }

        if (_unlocks != null)
            _unlocks.OnUnlocked += HandleUnlocked;
    }

    private void HandleAchievementsRequested() => _achievementsPanel.Toggle();

    private void HandleAchievementCompleted(AchievementDefinition definition)
    {
        _achievementToasts.Enqueue(definition);
        _achievementsPanel.MarkDirty();
    }

    private void HandleAchievementProgressChanged(AchievementDefinition definition) =>
        _achievementsPanel.MarkDirty();

    /// <summary>
    /// Открытие (обычно награда достижения). Во время выполнения кода окно ждёт конца запуска —
    /// см. HandleRunningChanged.
    /// </summary>
    private void HandleUnlocked(UnlockKind kind, string id) => _unlockPopup.Enqueue(kind, id);

    // ─── Window Layer ─────────────────────────────────────────

    /// <summary>
    /// Добавляется первым, поэтому всё, что создаётся после (верхняя панель,
    /// инвентарь, крафт), рисуется поверх окон кода.
    /// </summary>
    private void CreateWindowLayer()
    {
        _windowLayer = new VisualElement { name = "window-layer", pickingMode = PickingMode.Ignore };
        _windowLayer.style.position = Position.Absolute;
        _windowLayer.style.left = 0;
        _windowLayer.style.top = 0;
        _windowLayer.style.right = 0;
        _windowLayer.style.bottom = 0;
        _container.Add(_windowLayer);
    }

    // ─── TopBar ───────────────────────────────────────────────

    private void CreateTopBar()
    {
        _topBar = new TopBar(topBarTemplate);
        _topBar.OnRunRequested += HandleRunRequested;
        _topBar.OnEndDayRequested += HandleEndDayRequested;
        _topBar.OnSimulationRequested += HandleSimulationRequested;
        _container.Add(_topBar);
    }

    // ─── InventoryBar ─────────────────────────────────────────

    private void CreateInventoryBar()
    {
        _inventoryBar = new InventoryBar(inventoryBarTemplate);
        _container.Add(_inventoryBar);
    }

    // ─── CraftingPanel ────────────────────────────────────────

    private void CreateCraftingPanel()
    {
        // Закрытые рецепты (CraftRecipe.locked) не показываются, пока не открыты достижением
        _craftingPanel = new CraftingPanel(
            craftingPanelTemplate, _recipeBook.Recipes, _stations, _luaSandbox.Unlocks.IsRecipeAvailable);
        // Панели занимают одно место справа — открытая крафта закрывает библиотеку
        _craftingPanel.OnOpened += HandleCraftingOpened;
        _container.Add(_craftingPanel);
    }

    // ─── CodeLibraryPanel ─────────────────────────────────────

    private void CreateCodeLibraryPanel()
    {
        _codeLibrary = new CodeLibrary();
        _codeLibraryPanel = new CodeLibraryPanel(codeLibraryTemplate, _codeLibrary);
        _codeLibraryPanel.OnOpened += HandleCodeLibraryOpened;
        _codeLibraryPanel.OnOpenSnippetRequested += HandleOpenSnippetRequested;
        _container.Add(_codeLibraryPanel);
    }

    private void HandleCodeLibraryOpened() => _craftingPanel.Close();
    private void HandleCraftingOpened() => _codeLibraryPanel.Close();

    private void HandleWindowDragMoved(CodeEditorWindowBase window, Vector2 position)
    {
        _codeLibraryPanel.SetDropHighlight(_codeLibraryPanel.IsDropTarget(position));
    }

    /// <summary>
    /// Окно отпустили над библиотекой — сохраняем его код и возвращаем окно на место.
    /// </summary>
    private void HandleWindowDragEnded(CodeEditorWindowBase window, Vector2 position)
    {
        _codeLibraryPanel.SetDropHighlight(false);
        if (!_codeLibraryPanel.IsDropTarget(position)) return;

        window.ReturnToDragStart();

        if (string.IsNullOrWhiteSpace(window.Code))
        {
            UIEventBus.PublishNotification($"{window.Title}: nothing to save, the code is empty", LogEntryType.Warning);
            return;
        }

        try
        {
            bool updated = _codeLibrary.Save(window.Title, window.Code);
            UIEventBus.PublishNotification(
                updated ? $"{window.Title} updated in the library" : $"{window.Title} saved to the library",
                LogEntryType.Success);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            UIEventBus.PublishNotification($"Failed to save {window.Title}: {e.Message}", LogEntryType.Error);
        }
    }

    private void HandleOpenSnippetRequested(CodeSnippet snippet)
    {
        CreateAuxiliaryWindow(snippet.title, snippet.code);
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
        // Главное окно в библиотеку не сохраняется — на перетаскивание не подписываемся
        _uiManager.RegisterWindow(_mainWindow);
        _windowLayer.Add(_mainWindow);
        _windows.Add(_mainWindow);
    }

    public void CreateAuxiliaryWindow()
    {
        CreateAuxiliaryWindow(null, null);
    }

    /// <summary>
    /// Создаёт вспомогательное окно. Название должно быть допустимым и свободным
    /// Lua-идентификатором — иначе берётся Class_N / добавляется суффикс.
    /// </summary>
    public void CreateAuxiliaryWindow(string title, string code)
    {
        // Кнопки создания скрыты/выключены, но проверяем и здесь
        if (!AreClassesUnlocked())
        {
            UIEventBus.PublishNotification(
                $"{LuaConstructs.GetDisplayName(LuaConstructs.Classes)} are not unlocked yet", LogEntryType.Warning);
            return;
        }

        _windowCount++;
        title = MakeUniqueTitle(_identifierValidator.IsValid(title) ? title : $"Class_{_windowCount}");

        AuxiliaryCodeEditorWindow window = new AuxiliaryCodeEditorWindow(
            $"window_{_windowCount}",
            title,
            auxiliaryWindowTemplate,
            _context);

        float offset = (_windows.Count % 5) * 30f;
        window.SetPosition(20 + offset, 50 + offset);
        window.OnCloseRequested += HandleCloseRequested;
        window.OnCodeChanged += HandleCodeChanged;
        window.OnDragMoved += HandleWindowDragMoved;
        window.OnDragEnded += HandleWindowDragEnded;
        _uiManager.RegisterWindow(window);
        _windowLayer.Add(window);
        _windows.Add(window);

        if (!string.IsNullOrEmpty(code))
            window.SetCode(code);
    }

    private string MakeUniqueTitle(string title)
    {
        if (!IsTitleTaken(title)) return title;

        int suffix = 2;
        while (IsTitleTaken($"{title}_{suffix}"))
            suffix++;
        return $"{title}_{suffix}";
    }

    private bool IsTitleTaken(string title)
    {
        foreach (CodeEditorWindowBase window in _windows)
            if (window.Title == title) return true;
        return false;
    }

    // ─── Event Handlers ───────────────────────────────────────

    private void HandleCodeChanged(CodeEditorWindowBase changedWindow, string newCode)
    {
        PublishWindowCost(changedWindow, newCode);
        UpdateProgramCost();
    }

    private void PublishWindowCost(CodeEditorWindowBase window, string code)
    {
        float cost = _luaSandbox.CalculateEnergyCost(
            new Dictionary<string, string> { { window.Title, code } });

        UIEventBus.PublishWindowEnergyCostChanged(window.WindowId, cost);
    }

    // ─── Открытия ─────────────────────────────────────────────

    /// <summary>
    /// Открылась команда или конструкция (песочница уже обновила реестр, код не выполняется):
    /// всё, что построено из реестра и открытий, перестраивается.
    /// Автодополнение читает реестр на каждом запросе — его трогать не нужно.
    /// </summary>
    private void HandleUnlocksApplied()
    {
        _context.Highlighter.SetApiNames(BuildApiNames());
        RebuildCommandHelp();
        ApplyClassesAvailability();
        _craftingPanel.RefreshRecipes();

        // Подсветка (новые команды, открытые ключевые слова) и цены строк/окон
        foreach (CodeEditorWindowBase window in _windows)
        {
            window.RefreshEditor();
            PublishWindowCost(window, window.Code);
        }
        UpdateProgramCost();
    }

    private void RebuildCommandHelp()
    {
        _topBar.BuildCommandHelp(
            _luaSandbox.Registry.GetAllCommands(), _luaSandbox.EnergyConfig, _luaSandbox.Unlocks);
    }

    /// <summary>
    /// Окна-«классы» закрыты — нельзя создать доп. окно ни кнопкой «+», ни из библиотеки.
    /// </summary>
    private void ApplyClassesAvailability()
    {
        bool available = AreClassesUnlocked();
        _mainWindow?.SetAddMethodAvailable(available);
        _codeLibraryPanel?.SetSnippetOpeningAvailable(available);
    }

    /// <summary>
    /// Стоимость всей программы — сумма всех окон, показывается рядом с энергией.
    /// </summary>
    private void UpdateProgramCost()
    {
        if (_luaSandbox == null) return;
        float total = _luaSandbox.CalculateEnergyCost(GetAllWindowCode());
        _topBar?.UpdateProgramCost(total);
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
        // «Unlocked: X» — только между запусками: модальное окно не должно появляться посреди работы кода
        _unlockPopup?.SetSuspended(running);
    }

    /// <summary>
    /// Кнопка End Day — закончить день досрочно (не во время выполнения кода).
    /// </summary>
    private void HandleEndDayRequested()
    {
        if (_dayCycle != null && !_dayCycle.TryEndDay())
            UIEventBus.PublishNotification("Can't end the day while the code is running", LogEntryType.Warning);
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

    // ─── IPointerOverUIProvider ───────────────────────────────

    public bool IsPointerOverUI(Vector2 screenPosition)
    {
        IPanel panel = _container?.panel;
        if (panel == null) return false;

        // Input System считает Y снизу, панель — сверху
        Vector2 panelPosition = RuntimePanelUtils.ScreenToPanel(
            panel, new Vector2(screenPosition.x, Screen.height - screenPosition.y));

        VisualElement picked = panel.Pick(panelPosition);
        return picked != null && !IsBackground(picked, panel);
    }

    /// <summary>
    /// Полноэкранные подложки (корень документа, корень GameUI) — это не интерфейс,
    /// под ними видна карта.
    /// </summary>
    private bool IsBackground(VisualElement element, IPanel panel)
    {
        return element == panel.visualTree
            || element == _container
            || element.ClassListContains("game-ui");
    }
}