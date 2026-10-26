using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Инструмент тестировщика (только редактор, в сборку не попадает): выдать предмет, энергия/здоровье,
/// конец дня, открытия, телепорт, выполнение достижения. Работает в Play Mode через публичные свойства Level.
/// </summary>
public class DebugWindow : EditorWindow
{
    // Радиус поиска свободного тайла рядом с целью телепорта (головоломка, лагерь, клик)
    private const int FreeTileSearchRadius = 6;
    // Радиус проверки «на тайле нет твёрдого коллайдера» (в мировых единицах, центр тайла)
    private const float FreeTileProbeRadius = 0.3f;
    private const float DamageStep = 10f;

    private enum UnlockTab { Command, Construct, Recipe, HubBuilding, MapEvent }

    // ─── Состояние окна ───────────────────────────────────────

    private Vector2 _scroll;
    private string _status = "";

    private ResourceItem[] _items;
    private string[] _itemNames;
    private int _itemIndex;
    private int _itemAmount = 5;

    private UnlockTab _unlockTab;
    private int _unlockIndex;

    private int _achievementIndex;

    private bool _teleportOnClick;

    // Кэш ассетов для списков открытий (рецепты и постройки — из ассетов проекта)
    private string[] _recipeKeys;
    private string[] _hubBuildingIds;

    [MenuItem("CodeRogue/Debug")]
    private static void Open()
    {
        GetWindow<DebugWindow>("CodeRogue Debug");
    }

    // ─── Unity ────────────────────────────────────────────────

    private void OnEnable()
    {
        SceneView.duringSceneGui += HandleSceneGui;
        LoadAssets();
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= HandleSceneGui;
    }

    private void OnInspectorUpdate()
    {
        // Числа энергии, здоровья и дня меняются во время игры
        if (EditorApplication.isPlaying) Repaint();
    }

    private void OnGUI()
    {
        if (!EditorApplication.isPlaying)
        {
            _teleportOnClick = false;
            EditorGUILayout.HelpBox("Enter Play Mode", MessageType.Info);
            return;
        }

        Level level = FindAnyObjectByType<Level>();
        // После перезагрузки домена в Play Mode несериализуемые ссылки Level пусты — до перезапуска сцены
        if (level == null || level.Executor == null || level.Energy == null || level.Health == null
            || level.DayCycle == null || level.Inventory == null)
        {
            EditorGUILayout.HelpBox("Level is not found or not initialized (restart Play Mode after a script reload)",
                MessageType.Warning);
            return;
        }

        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        DrawStatusLine(level);
        DrawItems(level);
        DrawEnergyHealthDay(level);
        DrawUnlocks(level);
        DrawTeleport(level);
        DrawAchievements(level);

        EditorGUILayout.EndScrollView();

        if (!string.IsNullOrEmpty(_status))
            EditorGUILayout.HelpBox(_status, MessageType.None);
    }

    // ─── Разделы ──────────────────────────────────────────────

    private void DrawStatusLine(Level level)
    {
        bool running = level.Sandbox != null && level.Sandbox.IsRunning;
        string mode = level.HubService != null ? "Hub" : "Run";
        EditorGUILayout.LabelField(
            $"{mode} · tile {level.Executor.CurrentTile} · code {(running ? "RUNNING" : "idle")}",
            EditorStyles.miniLabel);
    }

    private void DrawItems(Level level)
    {
        Header("Give item");
        if (_items == null || _items.Length == 0)
        {
            EditorGUILayout.LabelField("No ResourceItem assets found");
            if (GUILayout.Button("Reload assets")) LoadAssets();
            return;
        }

        _itemIndex = EditorGUILayout.Popup("Item", Mathf.Clamp(_itemIndex, 0, _items.Length - 1), _itemNames);
        _itemAmount = Mathf.Max(1, EditorGUILayout.IntField("Amount", _itemAmount));
        ResourceItem item = _items[_itemIndex];

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("To run inventory"))
        {
            // recycled — отладочная выдача не двигает достижения
            int added = level.Inventory.Add(item, _itemAmount, recycled: true, fromThisRun: false);
            SetStatus($"Inventory: added {added} / {_itemAmount} × {item.name}");
        }
        if (GUILayout.Button("To hub stock"))
        {
            MetaProgress progress = GameSession.Progress;
            progress.AddMaterial(item, _itemAmount);
            progress.Save();
            if (level.HubService != null && level.CodeEditorManager != null && level.CodeEditorManager.RunScreens != null)
                level.CodeEditorManager.RunScreens.RefreshHub(progress);
            SetStatus($"Hub stock: +{_itemAmount} × {MetaProgress.GetItemKey(item)} (saved)");
        }
        EditorGUILayout.EndHorizontal();
        if (!item.IsMaterial)
            EditorGUILayout.LabelField("Not a material: hub stock keeps it, but runs only bring materials home",
                EditorStyles.miniLabel);
    }

    private void DrawEnergyHealthDay(Level level)
    {
        Header("Energy · Health · Day");

        PlayerEnergy energy = level.Energy;
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField($"Energy {energy.Current:0.#} / {energy.Max:0.#}", GUILayout.Width(170));
        if (GUILayout.Button("Fill")) energy.Restore(energy.Max - energy.Current);
        if (GUILayout.Button("Zero")) energy.TrySpend(energy.Current);
        EditorGUILayout.EndHorizontal();

        PlayerHealth health = level.Health;
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField($"Health {health.Current:0.#} / {health.Max:0.#}", GUILayout.Width(170));
        if (GUILayout.Button("Fill")) health.Heal(health.Max);
        if (GUILayout.Button($"−{DamageStep:0}")) health.TakeDamage(DamageStep);
        EditorGUILayout.EndHorizontal();

        DayCycle day = level.DayCycle;
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(
            $"Day {day.Day} · runs {day.RunsUsed}/{day.RunsPerDay} · threat day {day.ThreatDay}",
            GUILayout.Width(250));
        if (GUILayout.Button("End day"))
            SetStatus(day.TryEndDay() ? $"Day ended, now day {day.Day}" : "Can't end the day while code runs");
        EditorGUILayout.EndHorizontal();
    }

    private void DrawUnlocks(Level level)
    {
        Header("Unlocks");
        EditorGUILayout.LabelField("Writes to meta_progress.json (unlocks are permanent)", EditorStyles.boldLabel);

        UnlockRegistry unlocks = level.Unlocks;
        UnlockTab tab = (UnlockTab)GUILayout.Toolbar((int)_unlockTab,
            new[] { "Command", "Construct", "Recipe", "Hub bld", "Map event" });
        if (tab != _unlockTab)
        {
            _unlockTab = tab;
            _unlockIndex = 0;
        }

        string[] ids = GetUnlockIds(level, _unlockTab);
        if (ids.Length == 0)
        {
            EditorGUILayout.LabelField("Nothing to unlock here");
        }
        else
        {
            string[] labels = ids
                .Select(id => (IsUnlocked(unlocks, _unlockTab, id) ? "✓ " : "   ") + id)
                .ToArray();
            _unlockIndex = EditorGUILayout.Popup("Id", Mathf.Clamp(_unlockIndex, 0, ids.Length - 1), labels);
            if (GUILayout.Button("Unlock"))
            {
                string id = ids[_unlockIndex];
                bool first = Unlock(unlocks, _unlockTab, id);
                SetStatus($"{_unlockTab} '{id}': {(first ? "unlocked" : "already unlocked / deferred")}" +
                          RunningSuffix(level));
            }
        }

        if (GUILayout.Button("Unlock all (commands, constructs, recipes, hub buildings, map events)"))
        {
            int count = 0;
            foreach (UnlockTab kind in System.Enum.GetValues(typeof(UnlockTab)))
                foreach (string id in GetUnlockIds(level, kind))
                    if (Unlock(unlocks, kind, id)) count++;
            SetStatus($"Unlocked {count} new entries" + RunningSuffix(level));
        }
    }

    private void DrawTeleport(Level level)
    {
        Header("Teleport player");
        bool running = level.Sandbox != null && level.Sandbox.IsRunning;
        if (running)
            EditorGUILayout.LabelField("Not while code runs", EditorStyles.miniLabel);

        using (new EditorGUI.DisabledScope(running))
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Nearest puzzle")) TeleportToNearestPuzzle(level);
            if (GUILayout.Button("Nearest enemy camp")) TeleportToNearestCamp(level);
            EditorGUILayout.EndHorizontal();

            _teleportOnClick = GUILayout.Toggle(_teleportOnClick, "Teleport on next left click in Scene view", "Button");
        }
        if (running) _teleportOnClick = false;
    }

    private void DrawAchievements(Level level)
    {
        Header("Achievements");
        AchievementTracker tracker = level.AchievementTracker;
        if (tracker == null || tracker.Definitions.Count == 0)
        {
            EditorGUILayout.LabelField("No achievements");
            return;
        }

        string[] labels = tracker.Definitions
            .Select(d => (tracker.IsCompleted(d) ? "✓ " : "   ") + d.Id)
            .ToArray();
        _achievementIndex = EditorGUILayout.Popup("Id",
            Mathf.Clamp(_achievementIndex, 0, labels.Length - 1), labels);
        if (GUILayout.Button("Complete (grants rewards, saves)"))
        {
            string id = tracker.Definitions[_achievementIndex].Id;
            SetStatus(tracker.DebugComplete(id)
                ? $"Achievement '{id}' completed" + RunningSuffix(level)
                : $"Achievement '{id}' is already completed");
        }
    }

    // ─── Открытия ─────────────────────────────────────────────

    private string[] GetUnlockIds(Level level, UnlockTab tab)
    {
        switch (tab)
        {
            case UnlockTab.Command:
                return level.Sandbox != null
                    ? level.Sandbox.AllCommands.Select(c => c.Name).OrderBy(n => n).ToArray()
                    : new string[0];
            case UnlockTab.Construct:
                return LuaConstructs.All.Select(c => c.Id).ToArray();
            case UnlockTab.Recipe:
                return _recipeKeys ?? new string[0];
            case UnlockTab.HubBuilding:
                return _hubBuildingIds ?? new string[0];
            case UnlockTab.MapEvent:
                return PuzzleTypes.All;
        }
        return new string[0];
    }

    private static bool IsUnlocked(UnlockRegistry unlocks, UnlockTab tab, string id)
    {
        switch (tab)
        {
            case UnlockTab.Command: return unlocks.IsCommandUnlocked(id);
            case UnlockTab.Construct: return unlocks.IsConstructUnlocked(id);
            case UnlockTab.Recipe: return unlocks.IsRecipeUnlocked(id);
            case UnlockTab.HubBuilding: return unlocks.IsHubBuildingUnlocked(id);
            case UnlockTab.MapEvent: return unlocks.IsMapEventUnlocked(id);
        }
        return false;
    }

    private static bool Unlock(UnlockRegistry unlocks, UnlockTab tab, string id)
    {
        switch (tab)
        {
            case UnlockTab.Command: return unlocks.UnlockCommand(id);
            case UnlockTab.Construct: return unlocks.UnlockConstruct(id);
            case UnlockTab.Recipe: return unlocks.UnlockRecipe(id);
            case UnlockTab.HubBuilding: return unlocks.UnlockHubBuilding(id);
            case UnlockTab.MapEvent: return unlocks.UnlockMapEvent(id);
        }
        return false;
    }

    private static string RunningSuffix(Level level) =>
        level.Sandbox != null && level.Sandbox.IsRunning ? " (applies after the run ends)" : "";

    // ─── Телепорт ─────────────────────────────────────────────

    private void TeleportToNearestPuzzle(Level level)
    {
        PuzzleManager puzzles = level.Puzzles;
        if (puzzles == null || puzzles.Puzzles.Count == 0)
        {
            SetStatus("No puzzles on this map");
            return;
        }

        Vector2 player = level.Executor.Position;
        PuzzleStructure nearest = puzzles.Puzzles
            .Where(p => p != null)
            .OrderBy(p => (p.Position - player).sqrMagnitude)
            .FirstOrDefault();
        if (nearest == null)
        {
            SetStatus("No puzzles on this map");
            return;
        }

        // К лабиринту — на тайл у входа, к обелиску — на свободный тайл рядом
        Vector2Int target = nearest is MazePuzzle maze ? maze.ApproachTile : nearest.Tile;
        TeleportNear(level, target, $"puzzle {nearest.TypeId} #{nearest.Id}");
    }

    private void TeleportToNearestCamp(Level level)
    {
        EnemyManager enemies = level.Enemies;
        if (enemies == null || enemies.CampCenters.Count == 0)
        {
            SetStatus("No enemy camps on this map");
            return;
        }

        Vector2 player = level.Executor.Position;
        Vector2 camp = enemies.CampCenters.OrderBy(c => (c - player).sqrMagnitude).First();
        TeleportNear(level, MapPosition.ToTile(camp), "enemy camp");
    }

    private void HandleSceneGui(SceneView sceneView)
    {
        if (!_teleportOnClick || !EditorApplication.isPlaying) return;

        Event e = Event.current;
        // Перехватываем выбор объектов кликом, пока режим включён
        if (e.type == EventType.Layout)
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
        if (e.type != EventType.MouseDown || e.button != 0) return;

        Level level = FindAnyObjectByType<Level>();
        if (level == null || level.Executor == null) return;

        Vector3 world = HandleUtility.GUIPointToWorldRay(e.mousePosition).origin;
        Vector2Int tile = level.MapData.WorldToTileIndex(world);
        _teleportOnClick = false;
        e.Use();

        TeleportNear(level, tile, $"click {tile}");
        Repaint();
    }

    private void TeleportNear(Level level, Vector2Int target, string what)
    {
        if (level.Sandbox != null && level.Sandbox.IsRunning)
        {
            SetStatus("Can't teleport while code runs");
            return;
        }

        if (!TryFindFreeTile(level, target, out Vector2Int tile))
        {
            SetStatus($"No free land near {what}");
            return;
        }

        if (!level.Executor.Teleport(tile))
        {
            SetStatus($"Teleport to {tile} refused (water or the player is moving)");
            return;
        }

        // Камера в режиме панорамы за игроком не следует — переводим её на новое место
        CameraController cameraController = FindAnyObjectByType<CameraController>();
        if (cameraController != null)
            cameraController.FocusOn(level.Executor.transform.position);
        SetStatus($"Teleported to {tile} ({what})");
    }

    /// <summary>
    /// Ближайший к target тайл суши без стены лабиринта и без твёрдого коллайдера в центре.
    /// </summary>
    private static bool TryFindFreeTile(Level level, Vector2Int target, out Vector2Int result)
    {
        IMapDataProvider map = level.MapData;
        Transform player = level.Executor.transform;

        IEnumerable<Vector2Int> candidates =
            from dx in Enumerable.Range(-FreeTileSearchRadius, FreeTileSearchRadius * 2 + 1)
            from dy in Enumerable.Range(-FreeTileSearchRadius, FreeTileSearchRadius * 2 + 1)
            orderby dx * dx + dy * dy
            select new Vector2Int(target.x + dx, target.y + dy);

        foreach (Vector2Int tile in candidates)
        {
            if (!map.IsWalkable(tile.x, tile.y)) continue;
            if (level.Puzzles != null && level.Puzzles.IsMazeWall(tile)) continue;
            if (IsBlocked(map.TileIndexToWorld(tile.x, tile.y), player)) continue;

            result = tile;
            return true;
        }

        result = default;
        return false;
    }

    private static bool IsBlocked(Vector3 world, Transform player)
    {
        foreach (Collider2D collider in Physics2D.OverlapCircleAll(world, FreeTileProbeRadius))
        {
            if (collider.isTrigger) continue;
            if (collider.transform.IsChildOf(player)) continue;
            return true;
        }
        return false;
    }

    // ─── Helpers ──────────────────────────────────────────────

    private void LoadAssets()
    {
        _items = AssetDatabase.FindAssets("t:ResourceItem")
            .Select(guid => AssetDatabase.LoadAssetAtPath<ResourceItem>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(item => item != null)
            .OrderBy(item => item.name)
            .ToArray();
        _itemNames = _items
            .Select(item => $"{item.name} ({MetaProgress.GetItemKey(item)})")
            .ToArray();

        _recipeKeys = AssetDatabase.FindAssets("t:RecipeBook")
            .Select(guid => AssetDatabase.LoadAssetAtPath<RecipeBook>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(book => book != null)
            .SelectMany(book => book.Recipes)
            .Where(recipe => recipe != null && recipe.result != null && recipe.locked)
            .Select(recipe => recipe.Key)
            .Distinct()
            .OrderBy(key => key)
            .ToArray();

        _hubBuildingIds = AssetDatabase.FindAssets("t:HubBuildingDefinition")
            .Select(guid => AssetDatabase.LoadAssetAtPath<HubBuildingDefinition>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(def => def != null)
            .Select(def => def.Id)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();
    }

    private static void Header(string title)
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
    }

    private void SetStatus(string text)
    {
        _status = text;
        Debug.Log($"[Debug window] {text}");
    }
}
