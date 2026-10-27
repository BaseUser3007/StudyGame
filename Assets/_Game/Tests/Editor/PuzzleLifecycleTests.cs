using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// T25: стартовая платформа и одна попытка — старт вне запуска и посреди него (прерывание), попытка в
/// следующем запуске, решение и провал (головоломка исчезает), запрет решения до старта, одна активная за раз,
/// расстановка платформ генератором (детерминизм, суша, не на чужой головоломке).
/// </summary>
public class PuzzleLifecycleTests
{
    private readonly List<Object> _created = new List<Object>();
    private readonly List<System.IDisposable> _disposables = new List<System.IDisposable>();
    private Vector2 _player;

    [SetUp]
    public void SetUp()
    {
        _player = Vector2.zero;
    }

    [TearDown]
    public void TearDown()
    {
        foreach (System.IDisposable disposable in _disposables)
            disposable.Dispose();
        _disposables.Clear();

        // Контейнеры "Puzzles" создаются генератором в активной сцене
        foreach (GameObject root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            if (root.name == "Puzzles" && !_created.Contains(root))
                _created.Add(root);

        foreach (Object obj in _created)
            if (obj != null) Object.DestroyImmediate(obj);
        _created.Clear();
    }

    // ─── Старт ────────────────────────────────────────────────

    [Test]
    public void StepOnPlatform_WithoutRun_Armed_NoInterrupt()
    {
        PuzzleManager manager = Manager(out TestHelpers.PuzzleRunControl run);
        CodeTaskPuzzle obelisk = CreateObelisk(manager, 1, new Vector2(10f, 10f), new Vector2Int(10, 9));
        var started = new List<PuzzleStructure>();
        manager.OnPuzzleStarted += started.Add;

        PlayerEventBus.PublishPlayerMoved(new Vector2Int(10, 8));
        Assert.AreEqual(PuzzleState.Waiting, obelisk.State, "not on the platform yet");

        PlayerEventBus.PublishPlayerMoved(new Vector2Int(10, 9));
        Assert.AreEqual(PuzzleState.Armed, obelisk.State);
        Assert.AreSame(obelisk, manager.ActivePuzzle);
        Assert.AreEqual(0, run.InterruptCount, "no code running — nothing to interrupt");
        Assert.AreEqual(1, started.Count);

        // Следующий запуск — попытка
        run.SetRunning(true);
        Assert.AreEqual(PuzzleState.Attempt, obelisk.State);
    }

    [Test]
    public void StepOnPlatform_DuringRun_Interrupts_InterruptedRunEndKeepsArmed()
    {
        PuzzleManager manager = Manager(out TestHelpers.PuzzleRunControl run);
        CodeTaskPuzzle obelisk = CreateObelisk(manager, 1, new Vector2(10f, 10f), new Vector2Int(10, 9));

        run.SetRunning(true);
        PlayerEventBus.PublishPlayerMoved(new Vector2Int(10, 9));
        Assert.AreEqual(1, run.InterruptCount);
        Assert.AreEqual(PuzzleState.Armed, obelisk.State);

        // Прерванный запуск закончился — попытка не потрачена
        run.SetRunning(false);
        Assert.AreEqual(PuzzleState.Armed, obelisk.State);
        Assert.AreSame(obelisk, manager.GetById(1));

        run.SetRunning(true);
        Assert.AreEqual(PuzzleState.Attempt, obelisk.State);
    }

    // ─── Попытка ──────────────────────────────────────────────

    [Test]
    public void Attempt_Solved_GivesReward_ClearsActive()
    {
        var playerConfig = ScriptableObject.CreateInstance<PlayerConfig>();
        var puzzleConfig = ScriptableObject.CreateInstance<PuzzleConfig>();
        _created.Add(playerConfig);
        _created.Add(puzzleConfig);
        RunModifiers modifiers = RunModifiers.Create(playerConfig, new MetaProgress());
        var energy = new PlayerEnergy(modifiers.BaseEnergyLimit);
        var health = new PlayerHealth(modifiers.MaxHealth);
        var rewards = new PuzzleRewards(puzzleConfig, modifiers, energy, health, null, new PlayerInventory(4), null);
        float limit = modifiers.BaseEnergyLimit;

        PuzzleManager manager = Manager(out TestHelpers.PuzzleRunControl run, rewards);
        CodeTaskPuzzle obelisk = CreateObelisk(manager, 1, Vector2.zero, new Vector2Int(0, -1));
        _player = new Vector2(0f, -1f);

        PuzzleBriefing briefing = manager.GetBriefing(obelisk);
        Assert.IsFalse(string.IsNullOrEmpty(briefing.Title));
        Assert.IsFalse(string.IsNullOrEmpty(briefing.Description));
        StringAssert.Contains("10", briefing.Reward, "sum: +10 max energy");

        TestHelpers.StartAttempt(manager, run, obelisk);
        Assert.AreEqual(PuzzleSolveResult.Correct, manager.TrySolveCodeTask(obelisk, obelisk.GetTask().Answer));
        Assert.AreEqual(PuzzleState.Solved, obelisk.State);
        Assert.IsNull(manager.ActivePuzzle);
        Assert.AreEqual(limit + 10f, modifiers.BaseEnergyLimit, 0.001f);

        run.SetRunning(false);
        Assert.AreEqual(PuzzleState.Solved, obelisk.State);
        Assert.AreSame(obelisk, manager.GetById(1), "solved puzzle stays");

        // Платформа решённой больше не срабатывает
        PlayerEventBus.PublishPlayerMoved(new Vector2Int(0, -2));
        PlayerEventBus.PublishPlayerMoved(new Vector2Int(0, -1));
        Assert.IsNull(manager.ActivePuzzle);
    }

    [Test]
    public void Attempt_EndsUnsolved_FailedAndRemoved()
    {
        PuzzleManager manager = Manager(out TestHelpers.PuzzleRunControl run);
        PlatesPuzzle plates = CreatePlates(manager, 3, new Vector2Int(20, 20));
        var platform = new GameObject("Platform");
        _created.Add(platform);
        plates.SetStartPlatform(new Vector2Int(22, 19), platform);
        var failed = new List<PuzzleStructure>();
        manager.OnPuzzleFailed += failed.Add;

        PlayerEventBus.PublishPlayerMoved(new Vector2Int(22, 19));
        run.SetRunning(true);
        Assert.AreEqual(PuzzleState.Attempt, plates.State);

        run.SetRunning(false);

        Assert.AreEqual(1, failed.Count);
        Assert.AreEqual(PuzzleState.Failed, plates.State);
        Assert.IsNull(manager.ActivePuzzle);
        Assert.IsNull(manager.GetById(3));
        Assert.AreEqual(0, manager.Puzzles.Count);
        Assert.IsNull(manager.GetPuzzleAt(new Vector2Int(22, 22)));
        Assert.IsTrue(plates == null, "puzzle object destroyed");
        Assert.IsTrue(platform == null, "platform destroyed");

        // Следующие запуски ничего не меняют
        run.SetRunning(true);
        run.SetRunning(false);
        Assert.AreEqual(1, failed.Count);
    }

    // ─── Запрет решения до старта ─────────────────────────────

    [Test]
    public void Gates_NothingSolvesBeforeAttempt()
    {
        PuzzleManager manager = Manager(out TestHelpers.PuzzleRunControl run);
        CodeTaskPuzzle obelisk = CreateObelisk(manager, 1, Vector2.zero, new Vector2Int(0, -1));
        PlatesPuzzle plates = CreatePlates(manager, 2, new Vector2Int(20, 20));
        MazePuzzle maze = CreateMaze(manager, 3, new Vector2Int(40, 40));

        Assert.AreEqual(PuzzleSolveResult.NotStarted, manager.TrySolveCodeTask(obelisk, obelisk.GetTask().Answer));
        Assert.AreEqual("puzzle is not started — step on its start platform",
            PuzzleManager.Describe(PuzzleSolveResult.NotStarted));

        Vector2Int firstPlate = FirstPlateTile(plates);
        Assert.IsFalse(manager.StepOnPlates(plates, firstPlate));
        PlayerEventBus.PublishPlayerMoved(firstPlate);
        Assert.AreEqual(0, plates.PressedCount);

        PlayerEventBus.PublishPlayerMoved(maze.CenterTile);
        Assert.IsFalse(maze.IsSolved);

        // Начат (Armed), но попытка ещё не идёт — тоже нет
        Assert.IsTrue(manager.TryStart(maze));
        PlayerEventBus.PublishPlayerMoved(maze.CenterTile);
        Assert.IsFalse(maze.IsSolved);

        run.SetRunning(true);
        PlayerEventBus.PublishPlayerMoved(maze.CenterTile);
        Assert.IsTrue(maze.IsSolved);
        Assert.IsFalse(obelisk.IsSolved);
        Assert.AreEqual(0, plates.PressedCount);
    }

    [Test]
    public void SecondPlatform_IgnoredWhileOneIsActive()
    {
        PuzzleManager manager = Manager(out TestHelpers.PuzzleRunControl run);
        CodeTaskPuzzle first = CreateObelisk(manager, 1, new Vector2(10f, 10f), new Vector2Int(10, 9));
        CodeTaskPuzzle second = CreateObelisk(manager, 2, new Vector2(30f, 10f), new Vector2Int(30, 9));

        PlayerEventBus.PublishPlayerMoved(new Vector2Int(10, 9));
        PlayerEventBus.PublishPlayerMoved(new Vector2Int(30, 9));
        Assert.AreSame(first, manager.ActivePuzzle);
        Assert.AreEqual(PuzzleState.Waiting, second.State);
        Assert.IsFalse(manager.TryStart(second));

        // Первая провалена — вторую можно начать
        run.SetRunning(true);
        run.SetRunning(false);
        Assert.AreEqual(PuzzleState.Failed, first.State);
        PlayerEventBus.PublishPlayerMoved(new Vector2Int(30, 8));
        PlayerEventBus.PublishPlayerMoved(new Vector2Int(30, 9));
        Assert.AreSame(second, manager.ActivePuzzle);
        Assert.AreEqual(PuzzleState.Armed, second.State);
    }

    // ─── Генератор: платформы ─────────────────────────────────

    [Test]
    public void Generator_StartPlatforms_DeterministicOnLandOutsideOtherPuzzles()
    {
        List<Vector2Int> a = GenerateAll(seed: 17, out PuzzleManager manager, out IMapDataProvider map);
        List<Vector2Int> b = GenerateAll(seed: 17, out _, out _);

        Assert.AreEqual(4, manager.Puzzles.Count, "maze + plates + 2 obelisks");
        CollectionAssert.AreEqual(a, b, "same seed — same platforms");

        foreach (PuzzleStructure puzzle in manager.Puzzles)
        {
            Assert.IsTrue(puzzle.HasStartTile, puzzle.name);
            Vector2Int start = puzzle.StartTile;
            Assert.IsTrue(map.IsWalkable(start.x, start.y), $"{puzzle.name}: platform on land");

            foreach (PuzzleStructure other in manager.Puzzles)
                if (other != puzzle)
                {
                    Assert.IsFalse(other.ContainsTile(start), $"{puzzle.name}: platform on {other.name}");
                    Assert.AreNotEqual(other.StartTile, start);
                }

            if (puzzle is MazePuzzle maze)
            {
                Assert.AreEqual(maze.EntranceTile, start, "maze platform = entrance");
            }
            else
            {
                Assert.IsFalse(puzzle.ContainsTile(start), $"{puzzle.name}: platform outside the area");
                Assert.LessOrEqual(puzzle.DistanceTo(new Vector2(start.x, start.y)), 1f,
                    $"{puzzle.name}: platform next to the area");
            }
        }
    }

    // ─── Helpers ──────────────────────────────────────────────

    private PuzzleManager Manager(out TestHelpers.PuzzleRunControl run, PuzzleRewards rewards = null)
    {
        var manager = new PuzzleManager(() => _player, 1.5f, rewards);
        run = new TestHelpers.PuzzleRunControl();
        manager.AttachRunControl(run);
        _disposables.Add(manager);
        return manager;
    }

    private CodeTaskPuzzle CreateObelisk(PuzzleManager manager, int id, Vector2 position, Vector2Int start)
    {
        var go = new GameObject("Obelisk");
        _created.Add(go);
        CodeTaskPuzzle obelisk = go.AddComponent<CodeTaskPuzzle>();
        obelisk.Setup(id, position);
        obelisk.Initialize(CodeTaskKind.Sum, 5, Rect.zero);
        obelisk.SetStartPlatform(start, null);
        manager.Register(obelisk);
        return obelisk;
    }

    private PlatesPuzzle CreatePlates(PuzzleManager manager, int id, Vector2Int origin)
    {
        var go = new GameObject("Plates");
        _created.Add(go);
        PlatesPuzzle plates = go.AddComponent<PlatesPuzzle>();
        plates.Setup(id, origin + PlatesLayout.Center);
        plates.Build(PlatesLayout.Generate(new System.Random(5)), origin, GrassMap.Instance, null);
        manager.Register(plates);
        return plates;
    }

    private MazePuzzle CreateMaze(PuzzleManager manager, int id, Vector2Int origin)
    {
        PuzzleConfig config = MazeConfig();
        var go = new GameObject("Maze");
        _created.Add(go);
        MazePuzzle maze = go.AddComponent<MazePuzzle>();
        MazeLayout layout = MazeLayout.Generate(new System.Random(7));
        maze.Build(layout, origin, GrassMap.Instance, config);
        maze.Setup(id, origin + layout.Center);
        manager.Register(maze);
        return maze;
    }

    private PuzzleConfig MazeConfig()
    {
        var wallPrefab = new GameObject("WallPrefab");
        wallPrefab.AddComponent<BoxCollider2D>();
        var config = ScriptableObject.CreateInstance<PuzzleConfig>();
        config.mazeWallPrefab = wallPrefab;
        _created.Add(wallPrefab);
        _created.Add(config);
        return config;
    }

    private static Vector2Int FirstPlateTile(PlatesPuzzle plates)
    {
        int first = 0;
        for (int i = 1; i < plates.Layout.Count; i++)
            if (plates.Layout.Numbers[i] < plates.Layout.Numbers[first]) first = i;
        return plates.Origin + plates.Layout.Tiles[first];
    }

    /// <summary>Все типы открыты; возвращает платформы в порядке головоломок.</summary>
    private List<Vector2Int> GenerateAll(int seed, out PuzzleManager manager, out IMapDataProvider map)
    {
        PuzzleConfig config = MazeConfig();
        config.platesCount = 1;
        config.codeTaskCount = 2;
        config.obeliskPrefab = new GameObject("ObeliskPrefab");
        config.obeliskPrefab.AddComponent<BoxCollider2D>();
        _created.Add(config.obeliskPrefab);

        manager = new PuzzleManager(() => Vector2.zero, 1.5f, null);
        _disposables.Add(manager);
        map = new GrassMap(seed);
        PuzzleGenerator.Generate(config, map, null, null, null, Vector2Int.zero, id => true, manager, 0);

        var starts = new List<Vector2Int>();
        foreach (PuzzleStructure puzzle in manager.Puzzles) starts.Add(puzzle.StartTile);
        return starts;
    }

    /// <summary>Карта 64×64 из травы; тайл (x, y) — мировая точка (x, y).</summary>
    private sealed class GrassMap : IMapDataProvider
    {
        public static readonly GrassMap Instance = new GrassMap(1);

        private readonly int _seed;
        public GrassMap(int seed) { _seed = seed; }

        public int MapWidth => 64;
        public int MapHeight => 64;
        public int Seed => _seed;
        public bool IsWalkable(int x, int y) => IsInBounds(x, y);
        public bool IsInBounds(int x, int y) => x >= 0 && y >= 0 && x < MapWidth && y < MapHeight;
        public TileType GetTileType(int x, int y) => TileType.Grass;
        public Biome GetBiome(int x, int y) => null;
        public Vector3 TileIndexToWorld(int x, int y) => new Vector3(x, y, 0f);
        public Vector2Int WorldToTileIndex(Vector3 worldPos) => MapPosition.ToTile(worldPos);
        public Vector3 MapToWorld(Vector2 mapPos) => new Vector3(mapPos.x, mapPos.y, 0f);
    }
}
