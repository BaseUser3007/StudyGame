using UnityEngine;

public class Level : MonoBehaviour
{
    [Header("Map")]
    [SerializeField] private TilemapGenerator tilemapGenerator;
    [SerializeField] private SpawnPoint spawnPoint;
    [SerializeField] private FogOfWarSystem fogOfWarSystem;

    [Header("Session")]
    [Tooltip("Первая загрузка сцены открывает лагерь. Выключите, чтобы при отладке сразу начинать забег")]
    [SerializeField] private bool startInHub = true;

    [Header("Player")]
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private PlayerConfig playerConfig;

    [Header("Camera")]
    [SerializeField] private CameraController cameraController;

    [Header("Objects")]
    [SerializeField] private ObjectManager objectManager;

    [Header("Enemies")]
    [Tooltip("Лагеря врагов, патрули после угрозы, характеристики по умолчанию. Пусто — забег без врагов (предупреждение)")]
    [SerializeField] private EnemyConfig enemyConfig;

    [Header("Crafting")]
    [SerializeField] private RecipeBook recipeBook;

    [Header("Lua")]
    [SerializeField] private LuaSandbox luaSandbox;
    [SerializeField] private EnergyConfig energyConfig;
    [Tooltip("Стартовые команды и конструкции языка; остальное открывается мета-прогрессом")]
    [SerializeField] private UnlockConfig unlockConfig;

    [Header("Achievements")]
    [Tooltip("Все достижения игры (AchievementDefinition). Без каталога достижений нет, игра запускается")]
    [SerializeField] private AchievementCatalog achievementCatalog;

    [Header("Puzzles")]
    [Tooltip("Головоломки на карте (обелиски code_task, лабиринт maze). Пусто — забег без головоломок (предупреждение)")]
    [SerializeField] private PuzzleConfig puzzleConfig;

    [Header("Hub")]
    [Tooltip("Постройки лагеря и размер его сетки (HubBuildingCatalog). Без каталога в лагере ничего не построить, " +
             "бонусов построек в забеге нет (предупреждение)")]
    [SerializeField] private HubBuildingCatalog hubBuildingCatalog;

    [Header("UI")]
    [SerializeField] private CodeEditorManager codeEditorManager;
    [SerializeField] private UIManager uiManager;

    private AchievementTracker _achievementTracker;
    private EnemyManager _enemyManager;
    private HubService _hubService;
    private HubGrid _hubGrid;
    private HubPlacementController _hubPlacement;
    private PuzzleManager _puzzleManager;

    /// <summary>Достижения этой загрузки сцены (для UI — панель и уведомления).</summary>
    public AchievementTracker AchievementTracker => _achievementTracker;

    /// <summary>Враги забега (в лагере — пустой список).</summary>
    public EnemyManager Enemies => _enemyManager;

    /// <summary>Логика лагеря: каталог, склад, проверка и постройка (для панели построек). В забеге — null.</summary>
    public HubService HubService => _hubService;

    /// <summary>Мировая сетка лагеря: координаты клеток, призрак. В забеге — null.</summary>
    public HubGrid HubGrid => _hubGrid;

    /// <summary>Установка постройки мышью: Begin(definition) из панели. В забеге — null.</summary>
    public HubPlacementController HubPlacement => _hubPlacement;

    /// <summary>Головоломки забега (в лагере — пустой список, но не null).</summary>
    public PuzzleManager Puzzles => _puzzleManager;

    private void Awake()
    {
        ValidateReferences();
        InitializeLevel();
    }

    private void ValidateReferences()
    {
        Debug.Assert(tilemapGenerator != null, "Level: TilemapGenerator is not assigned");
        Debug.Assert(spawnPoint != null, "Level: SpawnPoint is not assigned");
        Debug.Assert(fogOfWarSystem != null, "Level: FogOfWarSystem is not assigned");
        Debug.Assert(playerPrefab != null, "Level: playerPrefab is not assigned");
        Debug.Assert(playerConfig != null, "Level: playerConfig is not assigned");
        Debug.Assert(cameraController != null, "Level: CameraController is not assigned");
        Debug.Assert(objectManager != null, "Level: ObjectManager is not assigned");
        Debug.Assert(recipeBook != null, "Level: recipeBook is not assigned");
        Debug.Assert(luaSandbox != null, "Level: LuaSandbox is not assigned");
        Debug.Assert(energyConfig != null, "Level: energyConfig is not assigned");
        Debug.Assert(unlockConfig != null, "Level: unlockConfig is not assigned");
        Debug.Assert(achievementCatalog != null, "Level: achievementCatalog is not assigned");
        Debug.Assert(codeEditorManager != null, "Level: CodeEditorManager is not assigned");
        Debug.Assert(uiManager != null, "Level: UIManager is not assigned");

        // Без конфига врагов игра работает (до привязки ассетов), враги просто не появляются
        if (enemyConfig == null)
            Debug.LogWarning("Level: enemyConfig is not assigned — no enemies in the run");

        // Без каталога построек лагерь пуст, а бонусы построек в забеге не применяются
        if (hubBuildingCatalog == null)
            Debug.LogWarning("Level: hubBuildingCatalog is not assigned — no camp buildings and no their bonuses");

        // Без конфига головоломок забег идёт без обелисков и лабиринта
        if (puzzleConfig == null)
            Debug.LogWarning("Level: puzzleConfig is not assigned — no puzzles in the run");
    }

    private void InitializeLevel()
    {
        // 0. Язык интерфейса — до создания UI (тексты переводятся при построении)
        Loc.LoadFrom(GameSession.Progress);

        // 1. Карта
        tilemapGenerator.GenerateMap();
        ConfigureDepthSorting();

        // 2. Спавн
        spawnPoint.Initialize(tilemapGenerator);
        Vector2Int startTile = spawnPoint.GetSpawnTile();
        GameObject player = SpawnPlayer(startTile);

        // 3. Компоненты игрока
        PlayerCommandExecutor executor = player.GetComponent<PlayerCommandExecutor>();
        executor.Initialize(tilemapGenerator, playerConfig, startTile);
        // Анимация и разворот спрайта (Animator на Visual необязателен)
        player.GetComponent<PlayerAnimator>().Initialize(executor);

        // 4. Туман войны
        fogOfWarSystem.Initialize(tilemapGenerator, playerConfig, startTile);

        // 5. Камера
        cameraController.Initialize(tilemapGenerator, player.transform, codeEditorManager);

        // 6. Объекты (в забеге — плюс гарантированный стартовый набор у спавна из MapGenerationConfig)
        GameMode mode = GameSession.RequestedMode ?? (startInHub ? GameMode.Hub : GameMode.Run);
        objectManager.Initialize(
            tilemapGenerator, startTile, mode == GameMode.Run ? tilemapGenerator.Config : null);

        // 7. Параметры забега (база из PlayerConfig + постройки лагеря), открытия, энергия, здоровье, инвентарь
        MetaProgress progress = GameSession.Progress;
        RunModifiers modifiers = RunModifiers.Create(playerConfig, progress, hubBuildingCatalog);
        UnlockRegistry unlocks = new UnlockRegistry(unlockConfig, progress);
        unlocks.SetKnownRecipes(GetRecipeKeys());
        // Постройки лагеря открываются достижениями и в забеге — каталог нужен в обоих режимах,
        // иначе открытия остаются отложенными (без всплывашки)
        if (hubBuildingCatalog != null)
            unlocks.SetKnownHubBuildings(hubBuildingCatalog.GetIds());
        // Типы головоломок — события на карте; известны всегда (их открывают достижения в обоих режимах)
        unlocks.SetKnownMapEvents(PuzzleTypes.All);

        PlayerEnergy playerEnergy = new PlayerEnergy(modifiers.BaseEnergyLimit);
        PlayerHealth playerHealth = new PlayerHealth(modifiers.MaxHealth);
        PlayerInventory inventory = new PlayerInventory(modifiers.InventorySlots);
        // Стартовые предметы — не добыча: достижения их не считают
        foreach (ItemStack stack in modifiers.StartingItems)
            inventory.Add(stack.item, stack.amount, recycled: true);

        // 8. Постройки (станции крафта считаются «рядом» в пределах structureInteractionRadius)
        StructureManager structures = new StructureManager(
            tilemapGenerator, executor, playerConfig.structureInteractionRadius);

        // 9. День: запуски кода, восстановление энергии, штраф/бонус к максимуму
        DayCycle dayCycle = new DayCycle(playerConfig, modifiers, playerEnergy, structures, playerHealth);

        // 9a. Враги и бой: лагеря — только в забеге, после объектов (не ставятся на ресурсы);
        // патрули — по началу дня после угрозы; время врагов идёт, пока выполняется код
        _enemyManager = new EnemyManager(
            tilemapGenerator, fogOfWarSystem, executor, playerHealth, inventory, objectManager, enemyConfig,
            dayCycle, spawnEnabled: mode == GameMode.Run);
        _enemyManager.SpawnInitial(startTile);
        // Удар по врагу (attack) — общий сервис, как ResourceHarvester; Lua-команда attack оборачивает его
        CombatService combat = new CombatService(executor, inventory, _enemyManager);

        // 9b. Головоломки: только в забеге и только открытые типы, после врагов (держатся от лагерей
        // подальше), до песочницы (Lua-команды puzzle/solve получают PuzzleManager)
        PuzzleRewards puzzleRewards = new PuzzleRewards(
            puzzleConfig, modifiers, playerEnergy, playerHealth, dayCycle, inventory, objectManager);
        _puzzleManager = new PuzzleManager(
            () => executor.Position, playerConfig.structureInteractionRadius, puzzleRewards);
        if (mode == GameMode.Run)
            PuzzleGenerator.Generate(
                puzzleConfig, tilemapGenerator, fogOfWarSystem, objectManager, _enemyManager.CampCenters, startTile,
                unlocks.IsMapEventUnlocked, _puzzleManager, salt: new System.Random().Next());
        // Патрули обходят области головоломок; патрули первого дня — только когда места головоломок известны
        _enemyManager.SetPuzzles(_puzzleManager);
        _enemyManager.SpawnInitialPatrols();

        // 10. Lua песочница
        luaSandbox.Initialize(
            executor, tilemapGenerator, fogOfWarSystem, energyConfig, objectManager, playerEnergy, playerHealth,
            inventory, recipeBook, structures, playerConfig.structureInteractionRadius, dayCycle, unlocks,
            _enemyManager, combat, _puzzleManager);
        dayCycle.Attach(luaSandbox);
        _enemyManager.Attach(luaSandbox);

        // 10a. Достижения: после песочницы (известны все команды для проверки наград), до UI
        _achievementTracker = new AchievementTracker(achievementCatalog, progress, unlocks, playerEnergy);

        // 10b. Лагерь: сетка с постройками у точки спавна (только в режиме Hub)
        if (mode == GameMode.Hub)
            InitializeHub(progress, unlocks, startTile, player);

        // 11. UI
        codeEditorManager.Initialize(
            luaSandbox, uiManager, recipeBook, structures, dayCycle, _achievementTracker,
            _hubService, _hubPlacement);
        uiManager.Initialize(
            codeEditorManager.TopBar, codeEditorManager.InventoryBar, codeEditorManager.CraftingPanel);

        // UI подписан на OnUnlocked — выдаём награды выполненных достижений, добавленные позже
        _achievementTracker.GrantRewardsOfCompleted();

        // UI подписан — показываем начальное состояние
        playerEnergy.Publish();
        playerHealth.Publish();
        inventory.Publish();
        dayCycle.Publish();

        // Стартовые события забега — после трекера достижений и UI (туториала), иначе их некому услышать:
        // угроза с первого дня / предупреждение о ней, враги, видимые со спавна
        if (mode == GameMode.Run)
            dayCycle.AnnounceStart();
        _enemyManager.PublishInitialVisibility();

        // 12. Лагерь / забег: выход из забега, поражение, переход в лагерь
        RunController runController = new RunController(
            mode, codeEditorManager.RunScreens, codeEditorManager.TopBar, luaSandbox, inventory, playerHealth,
            dayCycle, progress, _achievementTracker);
        runController.Start();
    }

    private void OnDestroy()
    {
        // Трекер подписан на статическую GameEventBus и дописывает несохранённый прогресс
        _achievementTracker?.Dispose();
        _achievementTracker = null;

        // Останавливает игровое время врагов и отписывается от песочницы и дня
        _enemyManager?.Dispose();
        _enemyManager = null;

        // Отписка от HubService и runtime-текстура площадки
        _hubGrid?.Dispose();
        _hubGrid = null;

        // Отписка лабиринта от статической PlayerEventBus
        _puzzleManager?.Dispose();
        _puzzleManager = null;
    }

    /// <summary>
    /// Лагерь поверх сгенерированной карты: площадка с сеткой над точкой спавна (игрок стоит у её
    /// нижнего края), туман над ней снят, объекты карты с неё убраны, камера — на центре сетки.
    /// </summary>
    private void InitializeHub(MetaProgress progress, UnlockRegistry unlocks, Vector2Int startTile, GameObject player)
    {
        _hubService = new HubService(hubBuildingCatalog, progress, unlocks);

        Vector2Int origin = HubGrid.ChooseOrigin(tilemapGenerator, startTile, _hubService.GridSize);
        SpriteRenderer playerRenderer = player.GetComponentInChildren<SpriteRenderer>();
        _hubGrid = new HubGrid(
            tilemapGenerator, _hubService, origin, playerRenderer != null ? playerRenderer.sharedMaterial : null);

        RectInt area = _hubGrid.TileArea;
        fogOfWarSystem.RevealRect(area, margin: 2);
        // Объекты карты на площадке лагеря не нужны — убираем вместе с индексом ObjectManager
        objectManager.RemoveObjectsInArea(new RectInt(area.xMin - 1, area.yMin - 1, area.width + 2, area.height + 2));

        _hubPlacement = _hubGrid.Root.gameObject.AddComponent<HubPlacementController>();
        _hubPlacement.Initialize(_hubService, _hubGrid, codeEditorManager);

        cameraController.FocusOn(_hubGrid.WorldCenter);
    }

    private System.Collections.Generic.IEnumerable<string> GetRecipeKeys()
    {
        foreach (CraftRecipe recipe in recipeBook.Recipes)
            if (recipe != null && recipe.result != null)
                yield return recipe.Key;
    }

    private GameObject SpawnPlayer(Vector2Int tile)
    {
        Vector3 worldPos = tilemapGenerator.TileIndexToWorld(tile.x, tile.y);
        GameObject player = Instantiate(playerPrefab, worldPos, Quaternion.identity);

        // Игрок двигается — порядок отрисовки пересчитывается на ходу
        if (!player.TryGetComponent(out DepthSortedSprite depthSorting))
            depthSorting = player.AddComponent<DepthSortedSprite>();
        depthSorting.Dynamic = true;
        depthSorting.Refresh();

        // Анимация движения/действий — как у врагов, параметры Animator необязательны
        if (!player.TryGetComponent(out PlayerAnimator _))
            player.AddComponent<PlayerAnimator>();

        return player;
    }

    /// <summary>
    /// Диапазон сортировки по высоте — от нижнего до верхнего края игровой области.
    /// </summary>
    private void ConfigureDepthSorting()
    {
        float bottom = tilemapGenerator.MapToWorld(new Vector2(0f, -0.5f)).y;
        float top = tilemapGenerator.MapToWorld(new Vector2(0f, tilemapGenerator.MapHeight - 0.5f)).y;
        DepthSorting.Configure(bottom, top);
    }
}