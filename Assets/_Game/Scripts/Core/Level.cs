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

    [Header("Crafting")]
    [SerializeField] private RecipeBook recipeBook;

    [Header("Lua")]
    [SerializeField] private LuaSandbox luaSandbox;
    [SerializeField] private EnergyConfig energyConfig;
    [Tooltip("Стартовые команды и конструкции языка; остальное открывается мета-прогрессом")]
    [SerializeField] private UnlockConfig unlockConfig;

    [Header("UI")]
    [SerializeField] private CodeEditorManager codeEditorManager;
    [SerializeField] private UIManager uiManager;

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
        Debug.Assert(codeEditorManager != null, "Level: CodeEditorManager is not assigned");
        Debug.Assert(uiManager != null, "Level: UIManager is not assigned");
    }

    private void InitializeLevel()
    {
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

        // 4. Туман войны
        fogOfWarSystem.Initialize(tilemapGenerator, playerConfig, startTile);

        // 5. Камера
        cameraController.Initialize(tilemapGenerator, player.transform, codeEditorManager);

        // 6. Объекты
        objectManager.Initialize(tilemapGenerator, startTile);

        // 7. Параметры забега (база из PlayerConfig + постройки лагеря), открытия, энергия, здоровье, инвентарь
        GameMode mode = GameSession.RequestedMode ?? (startInHub ? GameMode.Hub : GameMode.Run);
        MetaProgress progress = GameSession.Progress;
        RunModifiers modifiers = RunModifiers.Create(playerConfig, progress);
        UnlockRegistry unlocks = new UnlockRegistry(unlockConfig, progress);

        PlayerEnergy playerEnergy = new PlayerEnergy(modifiers.BaseEnergyLimit);
        PlayerHealth playerHealth = new PlayerHealth(modifiers.MaxHealth);
        PlayerInventory inventory = new PlayerInventory(modifiers.InventorySlots);
        foreach (ItemStack stack in modifiers.StartingItems)
            inventory.Add(stack.item, stack.amount);

        // 8. Постройки (станции крафта считаются «рядом» в пределах structureInteractionRadius)
        StructureManager structures = new StructureManager(
            tilemapGenerator, executor, playerConfig.structureInteractionRadius);

        // 9. День: запуски кода, восстановление энергии, штраф/бонус к максимуму
        DayCycle dayCycle = new DayCycle(playerConfig, modifiers, playerEnergy, structures);

        // 10. Lua песочница
        luaSandbox.Initialize(
            executor, tilemapGenerator, fogOfWarSystem, energyConfig, objectManager, playerEnergy, playerHealth,
            inventory, recipeBook, structures, playerConfig.structureInteractionRadius, dayCycle, unlocks);
        dayCycle.Attach(luaSandbox);

        // 11. UI
        codeEditorManager.Initialize(luaSandbox, uiManager, recipeBook, structures, dayCycle);
        uiManager.Initialize(
            codeEditorManager.TopBar, codeEditorManager.InventoryBar, codeEditorManager.CraftingPanel);

        // UI подписан — показываем начальное состояние
        playerEnergy.Publish();
        playerHealth.Publish();
        inventory.Publish();
        dayCycle.Publish();

        // 12. Лагерь / забег: выход из забега, поражение, переход в лагерь
        RunController runController = new RunController(
            mode, codeEditorManager.RunScreens, codeEditorManager.TopBar, luaSandbox, inventory, playerHealth,
            dayCycle, progress);
        runController.Start();
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