using UnityEngine;

public class Level : MonoBehaviour
{
    [Header("Map")]
    [SerializeField] private TilemapGenerator tilemapGenerator;
    [SerializeField] private SpawnPoint spawnPoint;
    [SerializeField] private FogOfWarSystem fogOfWarSystem;

    [Header("Player")]
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private PlayerConfig playerConfig;

    [Header("Camera")]
    [SerializeField] private CameraController cameraController;

    [Header("Objects")]
    [SerializeField] private ObjectManager objectManager;

    [Header("Lua")]
    [SerializeField] private LuaSandbox luaSandbox;
    [SerializeField] private EnergyConfig energyConfig;

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
        Debug.Assert(luaSandbox != null, "Level: LuaSandbox is not assigned");
        Debug.Assert(energyConfig != null, "Level: energyConfig is not assigned");
        Debug.Assert(codeEditorManager != null, "Level: CodeEditorManager is not assigned");
        Debug.Assert(uiManager != null, "Level: UIManager is not assigned");
    }

    private void InitializeLevel()
    {
        // 1. Карта
        tilemapGenerator.GenerateMap();

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
        cameraController.Initialize(tilemapGenerator, player.transform);

        // 6. Объекты
        objectManager.Initialize(tilemapGenerator, startTile);

        // 7. Lua песочница
        luaSandbox.Initialize(executor, tilemapGenerator, energyConfig, objectManager);

        // 8. UI
        codeEditorManager.Initialize(luaSandbox, uiManager);
        codeEditorManager.SetEnergyMax(
            ((IPlayerDataProvider)playerConfig).BaseEnergyLimit);
        uiManager.Initialize(codeEditorManager.TopBar);
    }

    private GameObject SpawnPlayer(Vector2Int tile)
    {
        Vector3 worldPos = tilemapGenerator.TileIndexToWorld(tile.x, tile.y);
        return Instantiate(playerPrefab, worldPos, Quaternion.identity);
    }
}