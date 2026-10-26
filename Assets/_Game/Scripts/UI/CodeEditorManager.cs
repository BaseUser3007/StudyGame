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
    private DebugVariablesPanel _debugVariables;
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
    private TutorialOverlay _tutorialOverlay;
    private TutorialController _tutorial;
    private readonly Dictionary<TutorialAnchor, VisualElement> _tutorialAnchors =
        new Dictionary<TutorialAnchor, VisualElement>();
    // Лагерь (этап 4): null в забеге
    private HubService _hubService;
    private HubPlacementController _hubPlacement;
    private HubBuildPanel _hubPanel;
    private int _windowCount = 0;
    private bool _isUIFocused;
    private bool _navigationBlocked;
    private CodeEditorContext _context;
    // Итог последнего запуска (GameEventBus.OnRunFinished приходит до возврата ExecuteAsync)
    private bool _hasRunReport;
    private float _lastRunSpent;
    // Подсветка выполняемой строки: последнее место из песочницы и то, что уже нарисовано.
    // Событие приходит на каждой итерации цикла — перерисовка не чаще ActiveLineRefreshMs
    private const long ActiveLineRefreshMs = 50;
    private string _executingWindow;
    private int _executingLine;
    // Нарисованное окно — ссылкой: заголовок могут переименовать во время запуска
    private CodeEditorWindowBase _shownWindow;
    private int _shownLine;
    private bool _activeLineScheduled;

    public TopBar TopBar => _topBar;
    public InventoryBar InventoryBar => _inventoryBar;
    public CraftingPanel CraftingPanel => _craftingPanel;
    public RunScreens RunScreens => _runScreens;

    /// <summary>Сцена — лагерь: код не пишется и не выполняется, вместо окон — панель построек.</summary>
    public bool IsHub => _hubService != null;

    // ─── Инициализация ────────────────────────────────────────

    /// <param name="hubService">Логика лагеря (Level.HubService); null — забег.</param>
    /// <param name="hubPlacement">Размещение построек мышью (Level.HubPlacement); null — забег.</param>
    public void Initialize(
        LuaSandbox luaSandbox, UIManager uiManager, RecipeBook recipeBook, IStationProvider stations,
        DayCycle dayCycle, AchievementTracker achievements,
        HubService hubService = null, HubPlacementController hubPlacement = null)
    {
        _luaSandbox = luaSandbox;
        _uiManager = uiManager;
        _recipeBook = recipeBook;
        _stations = stations;
        _dayCycle = dayCycle;
        _achievements = achievements;
        _hubService = hubService;
        _hubPlacement = hubPlacement;
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
        if (IsHub) CreateHub();
        CreateTutorial();

        _luaSandbox.OnRunningChanged += HandleRunningChanged;
        _luaSandbox.OnUnlocksApplied += HandleUnlocksApplied;
        _luaSandbox.OnLogMessage += HandleSandboxLogMessage;
        _luaSandbox.OnExecutingLineChanged += HandleExecutingLineChanged;
        _luaSandbox.Debugger.BreakpointProvider = HasBreakpoint;
        _luaSandbox.Debugger.OnPausedChanged += HandlePausedChanged;
        GameEventBus.OnRunFinished += HandleRunFinished;
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
            _luaSandbox.OnLogMessage -= HandleSandboxLogMessage;
            _luaSandbox.OnExecutingLineChanged -= HandleExecutingLineChanged;
            if (_luaSandbox.Debugger != null)
            {
                _luaSandbox.Debugger.OnPausedChanged -= HandlePausedChanged;
                _luaSandbox.Debugger.BreakpointProvider = null;
            }
        }
        GameEventBus.OnRunFinished -= HandleRunFinished;

        if (_achievements != null)
        {
            _achievements.OnAchievementCompleted -= HandleAchievementCompleted;
            _achievements.OnProgressChanged -= HandleAchievementProgressChanged;
        }

        if (_unlocks != null)
            _unlocks.OnUnlocked -= HandleUnlocked;

        if (_hubService != null)
        {
            _hubService.OnBuilt -= HandleHubBuilt;
            _hubService.OnUpgraded -= HandleHubUpgraded;
        }
        _hubPanel?.Dispose();
        _hubPanel = null;
        if (_uiManager != null)
        {
            _uiManager.SetNotificationDisplay(null);
            _uiManager.SetCodeErrorDisplay(null);
        }

        // Туториал подписан на статическую GameEventBus
        _tutorial?.Dispose();
        _tutorial = null;
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
        if (_hubService != null)
            Debug.Assert(_hubPlacement != null, "CodeEditorManager: HubPlacementController is null in the camp");
    }

    // ─── Лагерь ───────────────────────────────────────────────

    /// <summary>
    /// Лагерь (этап 4): карта с сеткой видна, слева склад и старт забега, справа панель построек.
    /// Код в лагере не выполняется — окна кода, верхняя панель (Run, энергия, день), инвентарь, крафт
    /// и библиотека скрыты; уведомления выводятся строкой внизу панели построек.
    /// </summary>
    private void CreateHub()
    {
        _hubPanel = new HubBuildPanel(_runScreens.HubRoot, _hubService, _hubPlacement, _achievements);
        _runScreens.HubIconResolver = FindHubMaterialIcon;
        _uiManager.SetNotificationDisplay(_hubPanel);
        _hubService.OnBuilt += HandleHubBuilt;
        _hubService.OnUpgraded += HandleHubUpgraded;

        foreach (VisualElement element in new VisualElement[]
                 { _windowLayer, _topBar, _inventoryBar, _craftingPanel, _codeLibraryPanel })
            element.style.display = DisplayStyle.None;
    }

    /// <summary>Постройка поставлена (оплачена и сохранена): склад уменьшился, карточки — в HubBuildPanel.</summary>
    private void HandleHubBuilt(HubBuildingDefinition definition, Vector2Int cell)
    {
        _runScreens.RefreshHub(_hubService.Progress);
        _hubPanel?.Refresh();
        UIEventBus.PublishNotification(Loc.F("{0} built", Loc.T(definition.Title)), LogEntryType.Success);
    }

    /// <summary>Постройка улучшена: склад уменьшился (карточки и уведомление — в HubBuildPanel).</summary>
    private void HandleHubUpgraded(HubBuildingDefinition definition, int level) =>
        _runScreens.RefreshHub(_hubService.Progress);

    /// <summary>Иконка материала склада: предмет ищется среди стоимостей построек (ResourceItem.Icon).</summary>
    private Sprite FindHubMaterialIcon(string key)
    {
        if (_hubService == null || string.IsNullOrEmpty(key)) return null;

        foreach (HubBuildingDefinition definition in _hubService.Definitions)
            for (int level = 1; level <= definition.MaxLevel; level++)
                foreach (HubBuildingCost cost in definition.GetCost(level))
                    if (cost.Key == key && cost.Item != null)
                        return cost.Item.Icon;
        return null;
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

    private void HandleAchievementsRequested()
    {
        _achievementsPanel.Toggle();
        if (_achievementsPanel.IsOpen) _tutorial?.Notify(TutorialFacts.AchievementsOpened);
    }

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
    private void HandleUnlocked(UnlockKind kind, string id)
    {
        _unlockPopup.Enqueue(kind, id);
        // Открыта постройка (например, награда выданная заново в лагере) — карточка уже не Locked
        if (kind == UnlockKind.HubBuilding) _hubPanel?.Refresh();
    }

    // ─── Туториал и подсказки ─────────────────────────────────

    /// <summary>
    /// Подсказки туториала (шаблон внутри RunScreens.uxml, выше всех оверлеев). Режим (лагерь/забег)
    /// известен после RunController.Start — туториал запускается в следующем обновлении панели.
    /// </summary>
    private void CreateTutorial()
    {
        MetaProgress progress = GameSession.Progress;

        _tutorialOverlay = new TutorialOverlay(_runScreens.GetOverlay("tutorial-overlay"), _context.Highlighter);
        var state = new TutorialState(progress, _unlocks, _dayCycle, IsKeywordLocked);
        if (_hubPanel != null) state.HubBuildAvailable = _hubPanel.IsAnyBuildAvailable;
        _tutorial = new TutorialController(_tutorialOverlay, progress, state, ResolveTutorialAnchor, IsTutorialBlocked);
        _tutorial.OnHintsChanged += HandleHintsChanged;
        HandleHintsChanged(_tutorial.HintsEnabled);

        _topBar.OnHelpOpened += HandleHelpOpened;
        _topBar.OnHintsToggleRequested += HandleHintsToggleRequested;
        _runScreens.OnHintsToggleRequested += HandleHintsToggleRequested;
        _runScreens.OnResetTutorialRequested += HandleResetTutorialRequested;
        _runScreens.OnStartRunRequested += HandleStartRunRequested;
        _runScreens.OnLanguageToggleRequested += HandleLanguageToggleRequested;

        UpdateTutorialCode();
        _container.schedule.Execute(StartTutorial);
    }

    private void StartTutorial()
    {
        // Режим известен только сейчас: в лагере угрозы нет — индикатор в верхней панели скрыт
        _topBar.SetHubMode(_runScreens.IsHubShown);
        _tutorial?.Start(_runScreens.IsHubShown ? TutorialMode.Hub : TutorialMode.Run);
    }

    /// <summary>Модальные окна поверх: пока они открыты, карточка подсказки скрыта.</summary>
    private bool IsTutorialBlocked()
    {
        return (_unlockPopup != null && _unlockPopup.IsOpen)
            || (_achievementsPanel != null && _achievementsPanel.IsOpen)
            || _runScreens.IsExitConfirmOpen
            || _runScreens.IsSummaryShown
            // Во время размещения постройки карточка не должна закрывать клетки сетки
            || (_hubPanel != null && _hubPanel.IsPlacing);
    }

    private VisualElement ResolveTutorialAnchor(TutorialAnchor anchor)
    {
        if (anchor == TutorialAnchor.None) return null;
        if (_tutorialAnchors.TryGetValue(anchor, out VisualElement cached) && cached != null && cached.panel != null)
            return cached;

        VisualElement element = FindTutorialAnchor(anchor);
        _tutorialAnchors[anchor] = element;
        return element;
    }

    private VisualElement FindTutorialAnchor(TutorialAnchor anchor)
    {
        switch (anchor)
        {
            case TutorialAnchor.StartRunButton: return _runScreens.FindElement("btn-start-run");
            case TutorialAnchor.HubAchievementsButton: return _runScreens.FindElement("btn-hub-achievements");
            case TutorialAnchor.HubBuildPanel: return _runScreens.FindElement("hub-buildings");
            case TutorialAnchor.MainWindow: return _mainWindow?.Q<VisualElement>("window");
            case TutorialAnchor.LineCosts: return _mainWindow?.Q<VisualElement>(className: "gutter-costs");
            case TutorialAnchor.AddWindowButton: return _mainWindow?.Q<VisualElement>("btn-add-method");
            case TutorialAnchor.RunButton: return _topBar.FindElement("btn-run");
            case TutorialAnchor.Energy: return _topBar.FindElement("energy-label");
            case TutorialAnchor.DayLabel: return _topBar.FindElement("day-label");
            case TutorialAnchor.ThreatLabel: return _topBar.FindElement("threat-label");
            case TutorialAnchor.HelpButton: return _topBar.FindElement("btn-help");
            case TutorialAnchor.EndDayButton: return _topBar.FindElement("btn-end-day");
            case TutorialAnchor.AchievementsButton: return _topBar.FindElement("btn-achievements");
            case TutorialAnchor.ExitRunButton: return _topBar.FindElement("btn-exit-run");
            case TutorialAnchor.Inventory: return _inventoryBar.Q<VisualElement>("inventory-bar");
            case TutorialAnchor.CraftButton: return _craftingPanel.Q<VisualElement>("btn-craft");
            case TutorialAnchor.LibraryTab: return _codeLibraryPanel.Q<VisualElement>("btn-library-toggle");
            default: return null;
        }
    }

    private void UpdateTutorialCode()
    {
        if (_tutorial == null || _mainWindow == null) return;
        _tutorial.SetCode(_mainWindow.Code, GetAllWindowCode().Values);
    }

    private void HandleHelpOpened() => _tutorial?.Notify(TutorialFacts.HelpOpened);

    private void HandleStartRunRequested() => _tutorial?.Notify(TutorialFacts.StartRunRequested);

    private void HandleHintsToggleRequested()
    {
        if (_tutorial == null) return;
        _tutorial.SetHintsEnabled(!_tutorial.HintsEnabled);
    }

    private void HandleHintsChanged(bool enabled)
    {
        _topBar.SetHintsEnabled(enabled);
        _runScreens.SetHintsEnabled(enabled);
    }

    private void HandleResetTutorialRequested()
    {
        _tutorial?.ResetTutorial();
        UIEventBus.PublishNotification(Loc.T("Tutorial reset: hints will guide you again"), LogEntryType.Success);
    }

    /// <summary>
    /// Кнопка языка в лагере: другой язык, сохранение и перезагрузка лагеря — тексты UXML и C#
    /// переводятся при создании, поэтому живого обновления нет. В забеге кнопки нет: перезагрузка
    /// сцены потеряла бы забег.
    /// </summary>
    private void HandleLanguageToggleRequested()
    {
        if (!IsHub || _luaSandbox.IsRunning) return;

        Loc.SetLanguage(Loc.IsRussian ? GameLanguage.English : GameLanguage.Russian);
        MetaProgress progress = GameSession.Progress;
        Loc.SaveTo(progress);
        progress.Save();
        GameSession.ReturnToHub();
    }

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
        _topBar.OnContinueRequested += HandleContinueRequested;
        _topBar.OnStepRequested += HandleStepRequested;
        _container.Add(_topBar);

        // Переменные на точке останова — под верхней панелью справа, только на паузе
        _debugVariables = new DebugVariablesPanel();
        _container.Add(_debugVariables);
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

    private void HandleCodeLibraryOpened()
    {
        _craftingPanel.Close();
        _tutorial?.Notify(TutorialFacts.LibraryOpened);
    }

    private void HandleCraftingOpened()
    {
        _codeLibraryPanel.Close();
        _tutorial?.Notify(TutorialFacts.CraftingOpened);
    }

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
            UIEventBus.PublishNotification(
                Loc.F("{0}: nothing to save, the code is empty", window.Title), LogEntryType.Warning);
            return;
        }

        try
        {
            bool updated = _codeLibrary.Save(window.Title, window.Code);
            _tutorial?.Notify(TutorialFacts.WindowSaved);
            UIEventBus.PublishNotification(
                updated
                    ? Loc.F("{0} updated in the library", window.Title)
                    : Loc.F("{0} saved to the library", window.Title),
                LogEntryType.Success);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            UIEventBus.PublishNotification(Loc.F("Failed to save {0}: {1}", window.Title, e.Message), LogEntryType.Error);
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
        _mainWindow.OnRunRequested += HandleRunRequested;
        _mainWindow.OnAddMethodRequested += CreateAuxiliaryWindow;
        _mainWindow.OnGoToLineRequested += HandleGoToLineRequested;
        // Главное окно в библиотеку не сохраняется — на перетаскивание не подписываемся
        _uiManager.RegisterWindow(_mainWindow);
        _windowLayer.Add(_mainWindow);
        _windows.Add(_mainWindow);

        // В забеге уведомления и ошибки запуска — в логе главного окна (в лагере CreateHub
        // переназначает уведомления на панель построек)
        _uiManager.SetNotificationDisplay(_mainWindow);
        _uiManager.SetCodeErrorDisplay(_mainWindow);
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
                Loc.F("{0} are not unlocked yet", Loc.T(LuaConstructs.GetDisplayName(LuaConstructs.Classes))),
                LogEntryType.Warning);
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
        window.OnRunRequested += HandleRunRequested;
        window.OnDragMoved += HandleWindowDragMoved;
        window.OnDragEnded += HandleWindowDragEnded;
        _uiManager.RegisterWindow(window);
        _windowLayer.Add(window);
        _windows.Add(window);

        if (!string.IsNullOrEmpty(code))
            window.SetCode(code);

        _tutorial?.Notify(TutorialFacts.AuxWindowCreated);
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
        UpdateTutorialCode();
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
        // В лагере код не выполняется (кнопка Run скрыта вместе с верхней панелью — проверка на всякий случай)
        if (IsHub) return;

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

        _hasRunReport = false;
        ExecutionResult result = await _luaSandbox.ExecuteAsync(mainCode, auxiliaryWindows);

        // Расход за запуск (CodeRunReport.EnergySpent) — бесплатный для игрока замер (GameDesign §6.2.6)
        string spent = _hasRunReport ? Loc.F(" — spent {0} energy", FormatEnergy(_lastRunSpent)) : string.Empty;

        if (result.IsSuccess)
            UIEventBus.PublishNotification(Loc.T("Execution completed") + spent, LogEntryType.Success);
        else if (result.IsCancelled)
            UIEventBus.PublishNotification(Loc.T("Execution stopped") + spent, LogEntryType.Warning);
        else
        {
            ReportCodeError(result.ErrorMessage);
            if (_hasRunReport)
                UIEventBus.PublishNotification(Loc.T("Run finished") + spent, LogEntryType.Info);
        }
    }

    private void HandleRunFinished(CodeRunReport report)
    {
        _hasRunReport = true;
        _lastRunSpent = report.EnergySpent;
    }

    /// <summary>
    /// Вывод Lua (print) — в лог главного окна обычной строкой. Через UIEventBus, как все уведомления:
    /// показывает их UIManager.
    /// </summary>
    private void HandleSandboxLogMessage(string message, LogEntryType type)
    {
        UIEventBus.PublishNotification(message, type);
    }

    private static string FormatEnergy(float value) =>
        value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Ошибка запуска: сырой текст, место (окно и строка — для перехода по клику) и, если подсказки
    /// включены, объяснение для новичка (имена для «Did you mean» — из реестра открытых команд и заголовков доп. окон).
    /// </summary>
    private void ReportCodeError(string errorMessage)
    {
        UnlockRegistry unlocks = _luaSandbox.Unlocks;
        LuaErrorExplanation error = LuaErrorExplainer.Explain(
            errorMessage, _luaSandbox.Registry.GetAllCommands(), GetWindowFunctionNames(), LuaSandbox.MainChunkName,
            unlocks != null ? unlocks.IsConstructUnlocked : (System.Func<string, bool>)null);

        // Подсказки выключены — без объяснения, но место ошибки остаётся
        bool hintsEnabled = _tutorial == null || _tutorial.HintsEnabled;
        if (!hintsEnabled)
            error = new LuaErrorExplanation(error.Raw, error.Window, error.Line, null, null);

        // Ошибка «в конце» доп. окна указывает на строку закрывающего end обёртки — не дальше последней строки
        CodeEditorWindowBase window = FindWindowByTitle(error.Window);
        if (window != null) error.ClampLine(CountLines(window.Code));

        UIEventBus.PublishCodeError(error);
    }

    /// <summary>
    /// Клик по месту ошибки в логе: окно с этим заголовком поднимается, курсор — на строку.
    /// Окно закрыто или переименовано — только предупреждение в лог.
    /// </summary>
    private void HandleGoToLineRequested(string title, int line)
    {
        CodeEditorWindowBase window = FindWindowByTitle(title);
        if (window == null)
        {
            UIEventBus.PublishNotification(Loc.F("Window {0} is not open", title), LogEntryType.Warning);
            return;
        }
        window.GoToLine(line);
    }

    private CodeEditorWindowBase FindWindowByTitle(string title)
    {
        if (string.IsNullOrEmpty(title)) return null;
        foreach (CodeEditorWindowBase window in _windows)
            if (window.Title == title) return window;
        return null;
    }

    private static int CountLines(string code)
    {
        int lines = 1;
        if (code == null) return lines;
        foreach (char c in code)
            if (c == '\n') lines++;
        return lines;
    }

    private void HandleExecutingLineChanged(string window, int line)
    {
        _executingWindow = window;
        _executingLine = line;
        // Конец запуска снимаем сразу; остальное — не чаще раза в ActiveLineRefreshMs
        if (line <= 0)
        {
            ApplyExecutingLine();
            return;
        }
        if (_activeLineScheduled || _container == null) return;
        _activeLineScheduled = true;
        _container.schedule.Execute(ApplyExecutingLine).StartingIn(ActiveLineRefreshMs);
    }

    private void ApplyExecutingLine()
    {
        _activeLineScheduled = false;
        CodeEditorWindowBase window = _executingLine > 0 ? FindWindowByTitle(_executingWindow) : null;
        if (window == _shownWindow && _executingLine == _shownLine) return;

        _shownWindow?.SetActiveLine(0);
        window?.SetActiveLine(_executingLine);
        _shownWindow = window;
        _shownLine = window != null ? _executingLine : 0;
    }

    // ─── Debugger ─────────────────────────────────────────────

    private bool HasBreakpoint(string window, int line)
    {
        CodeEditorWindowBase target = FindWindowByTitle(window);
        return target != null && target.HasBreakpoint(line);
    }

    /// <summary>
    /// Пауза на точке останова: строка рисуется сразу (без задержки перерисовки) цветом паузы,
    /// в верхней панели — Continue / Step.
    /// </summary>
    private void HandlePausedChanged(string window, int line)
    {
        _topBar?.SetPaused(window, line);

        LuaDebugger debugger = _luaSandbox != null ? _luaSandbox.Debugger : null;
        if (line > 0 && debugger != null)
            _debugVariables?.Show(window, line, debugger.Locals, debugger.Globals);
        else
            _debugVariables?.Hide();

        if (line > 0)
        {
            _executingWindow = window;
            _executingLine = line;
            CodeEditorWindowBase target = FindWindowByTitle(window);
            if (_shownWindow != target) _shownWindow?.SetActiveLine(0);
            target?.SetActiveLine(line, paused: true);
            _shownWindow = target;
            _shownLine = target != null ? line : 0;
        }
        else
        {
            // Продолжили — та же строка, обычным цветом, пока выполнение не уйдёт дальше
            _shownWindow?.SetActiveLine(_shownLine);
        }
    }

    private void HandleContinueRequested()
    {
        _luaSandbox?.Debugger?.Continue();
    }

    private void HandleStepRequested()
    {
        _luaSandbox?.Debugger?.Step();
    }

    /// <summary>Снять подсветку выполняемой строки во всех окнах (конец запуска).</summary>
    private void ClearExecutingLine()
    {
        _executingWindow = null;
        _executingLine = 0;
        _shownWindow = null;
        _shownLine = 0;
        foreach (CodeEditorWindowBase window in _windows)
            window.SetActiveLine(0);
    }

    private void HandleRunningChanged(bool running)
    {
        _topBar?.SetRunning(running);
        if (!running) ClearExecutingLine();
        // Новый запуск: лог и выделенная строка ошибки прошлого запуска уже не нужны
        if (running)
        {
            _mainWindow?.ClearLog();
            foreach (CodeEditorWindowBase window in _windows)
                window.SetErrorLine(0);
        }
        // «Unlocked: X» — только между запусками: модальное окно не должно появляться посреди работы кода
        _unlockPopup?.SetSuspended(running);
        // Подсказки туториала тоже ждут конца запуска (кроме шагов, которые объясняют сам запуск)
        _tutorial?.SetRunning(running);
    }

    /// <summary>
    /// Кнопка End Day — закончить день досрочно (не во время выполнения кода).
    /// </summary>
    private void HandleEndDayRequested()
    {
        if (_dayCycle != null && !_dayCycle.TryEndDay())
            UIEventBus.PublishNotification(Loc.T("Can't end the day while the code is running"), LogEntryType.Warning);
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
        UpdateTutorialCode();
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
