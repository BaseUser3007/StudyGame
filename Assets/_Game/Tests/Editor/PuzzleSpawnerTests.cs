using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// T24: резерв мест головоломок для всех типов при старте, постройка ждущих при открытии события посреди
/// забега (на месте, если оно ни разу не открыто, иначе — перенос в неоткрытую часть), детерминизм.
/// </summary>
public class PuzzleSpawnerTests
{
    private readonly List<Object> _created = new List<Object>();
    private readonly List<System.IDisposable> _disposables = new List<System.IDisposable>();
    private TestHelpers.TempSaveFolder _saves;

    [SetUp]
    public void SetUp()
    {
        _saves = new TestHelpers.TempSaveFolder();
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
        _saves.Dispose();
    }

    // ─── Резерв ───────────────────────────────────────────────

    [Test]
    public void Generate_LockedType_ReservedAsPending()
    {
        PuzzleManager manager = Manager();
        PuzzlePlan plan = Generate(PlatesConfig(2), 5, new StubFog(), manager, id => false);

        Assert.AreEqual(0, manager.Puzzles.Count, "locked type is not built");
        Assert.AreEqual(2, plan.Pending.Count);
        foreach (PuzzleSlot slot in plan.Pending)
        {
            Assert.AreEqual(PuzzleTypes.Plates, slot.TypeId);
            Assert.AreEqual(PlatesLayout.Size, slot.Tiles.width);
        }
    }

    [Test]
    public void Generate_SlotsDoNotDependOnUnlocks_AndBuiltMatchSlots()
    {
        PuzzlePlan locked = Generate(PlatesConfig(2), 9, new StubFog(), Manager(), id => false);
        PuzzleManager manager = Manager();
        PuzzlePlan unlocked = Generate(PlatesConfig(2), 9, new StubFog(), manager, id => true);

        Assert.AreEqual(0, unlocked.Pending.Count);
        Assert.AreEqual(2, manager.Puzzles.Count);
        Assert.AreEqual(locked.Slots.Count, unlocked.Slots.Count);
        for (int i = 0; i < locked.Slots.Count; i++)
        {
            Assert.AreEqual(locked.Slots[i].Tiles, unlocked.Slots[i].Tiles);
            Assert.AreEqual(locked.Slots[i].LayoutSeed, unlocked.Slots[i].LayoutSeed);
            var plates = (PlatesPuzzle)manager.GetById(unlocked.Slots[i].Id);
            Assert.AreEqual(unlocked.Slots[i].Tiles.position, plates.Origin);
        }
    }

    [Test]
    public void Generate_SameSeed_SameSlots()
    {
        PuzzlePlan a = Generate(PlatesConfig(2), 21, new StubFog(), Manager(), id => false);
        PuzzlePlan b = Generate(PlatesConfig(2), 21, new StubFog(), Manager(), id => false);

        Assert.AreEqual(a.Slots.Count, b.Slots.Count);
        for (int i = 0; i < a.Slots.Count; i++)
        {
            Assert.AreEqual(a.Slots[i].Id, b.Slots[i].Id);
            Assert.AreEqual(a.Slots[i].Tiles, b.Slots[i].Tiles);
            Assert.AreEqual(a.Slots[i].LayoutSeed, b.Slots[i].LayoutSeed);
        }
    }

    [Test]
    public void Generate_IdsUnique_AcrossBuiltAndPending()
    {
        PuzzleConfig config = PlatesConfig(2);
        config.codeTaskCount = 2;
        config.obeliskPrefab = new GameObject("ObeliskPrefab");
        _created.Add(config.obeliskPrefab);

        PuzzleManager manager = Manager();
        PuzzlePlan plan = Generate(config, 3, new StubFog(), manager, id => id == PuzzleTypes.CodeTask);

        Assert.AreEqual(4, plan.Slots.Count);
        Assert.AreEqual(2, manager.Puzzles.Count, "obelisks are built");
        Assert.AreEqual(2, plan.Pending.Count, "plates wait");
        var ids = new HashSet<int>();
        foreach (PuzzleSlot slot in plan.Slots) Assert.IsTrue(ids.Add(slot.Id));
        foreach (PuzzleStructure puzzle in manager.Puzzles)
            Assert.AreEqual(PuzzleTypes.CodeTask, puzzle.TypeId);
    }

    // ─── Постройка по открытию ────────────────────────────────

    [Test]
    public void Unlock_BuildsPendingSlots_InPlace()
    {
        PuzzleManager manager = Manager();
        PuzzlePlan plan = Generate(PlatesConfig(2), 5, new StubFog(), manager, id => false);
        var expected = new List<PuzzleSlot>(plan.Pending);

        UnlockRegistry unlocks = Registry();
        _disposables.Add(new PuzzleSpawner(plan, unlocks, null));

        Assert.IsTrue(unlocks.UnlockMapEvent(PuzzleTypes.Plates));

        Assert.AreEqual(0, plan.Pending.Count);
        Assert.AreEqual(2, manager.Puzzles.Count);
        foreach (PuzzleSlot slot in expected)
        {
            var plates = manager.GetById(slot.Id) as PlatesPuzzle;
            Assert.IsNotNull(plates);
            Assert.AreEqual(slot.Tiles.position, plates.Origin);
            // T25: построенные посреди забега — тоже со стартовой платформой
            Assert.IsTrue(plates.HasStartTile);
            Assert.AreEqual(slot.StartTile, plates.StartTile);
            Assert.AreEqual(PuzzleState.Waiting, plates.State);
        }
    }

    [Test]
    public void Unlock_OtherType_DoesNotBuild()
    {
        PuzzleManager manager = Manager();
        PuzzlePlan plan = Generate(PlatesConfig(1), 5, new StubFog(), manager, id => false);
        UnlockRegistry unlocks = Registry();
        _disposables.Add(new PuzzleSpawner(plan, unlocks, null));

        unlocks.UnlockMapEvent(PuzzleTypes.Maze);

        Assert.AreEqual(1, plan.Pending.Count);
        Assert.AreEqual(0, manager.Puzzles.Count);
    }

    [Test]
    public void Spawn_ExploredSlot_RelocatedToUnexploredPart()
    {
        var fog = new StubFog();
        PuzzleManager manager = Manager();
        PuzzlePlan plan = Generate(PlatesConfig(1), 7, fog, manager, id => false);
        PuzzleSlot reserved = plan.Pending[0];

        // Игрок успел побывать на зарезервированном месте
        fog.Explore(PuzzleGenerator.Expand(reserved.Tiles, 1));
        Assert.IsFalse(plan.IsStillHidden(reserved, null));

        var spawner = new PuzzleSpawner(plan, null, null);
        _disposables.Add(spawner);
        List<PuzzleStructure> built = spawner.SpawnType(PuzzleTypes.Plates);

        Assert.AreEqual(1, built.Count);
        var plates = (PlatesPuzzle)built[0];
        Assert.AreEqual(reserved.Id, plates.Id, "id is kept");
        Assert.AreNotEqual(reserved.Tiles.position, plates.Origin);
        Assert.AreSame(plates, manager.GetById(reserved.Id));
        RectInt area = PuzzleGenerator.Expand(plates.TileArea, 1);
        foreach (Vector2Int tile in area.allPositionsWithin)
            Assert.IsFalse(fog.IsExplored(tile.x, tile.y), $"tile {tile} was explored");
    }

    [Test]
    public void Spawn_EnemyInArea_BlocksInPlace()
    {
        PuzzlePlan plan = Generate(PlatesConfig(1), 7, new StubFog(), Manager(), id => false);

        Assert.IsTrue(plan.IsStillHidden(plan.Pending[0], null));
        Assert.IsFalse(plan.IsStillHidden(plan.Pending[0], area => true));
    }

    [Test]
    public void Spawn_WholeIslandExplored_SkippedWithWarning()
    {
        var fog = new StubFog();
        PuzzleManager manager = Manager();
        PuzzlePlan plan = Generate(PlatesConfig(1), 7, fog, manager, id => false);
        fog.Explore(new RectInt(-2, -2, 70, 70));

        var spawner = new PuzzleSpawner(plan, null, null);
        _disposables.Add(spawner);
        LogAssert.Expect(LogType.Warning, new Regex("no unexplored place"));
        List<PuzzleStructure> built = spawner.SpawnType(PuzzleTypes.Plates);

        Assert.AreEqual(0, built.Count);
        Assert.AreEqual(0, manager.Puzzles.Count);
        Assert.AreEqual(1, plan.Pending.Count, "slot keeps waiting");
    }

    [Test]
    public void Relocate_SameFog_SamePlace()
    {
        PuzzleSlot a = RelocateOnce(11);
        PuzzleSlot b = RelocateOnce(11);

        Assert.IsNotNull(a);
        Assert.IsNotNull(b);
        Assert.AreEqual(a.Tiles, b.Tiles);
        Assert.AreEqual(a.LayoutSeed, b.LayoutSeed);
    }

    // ─── Helpers ──────────────────────────────────────────────

    private PuzzleSlot RelocateOnce(int seed)
    {
        var fog = new StubFog();
        PuzzlePlan plan = Generate(PlatesConfig(1), seed, fog, Manager(), id => false);
        fog.Explore(PuzzleGenerator.Expand(plan.Pending[0].Tiles, 1));
        return plan.Relocate(plan.Pending[0], null);
    }

    private PuzzleConfig PlatesConfig(int plates)
    {
        var config = ScriptableObject.CreateInstance<PuzzleConfig>();
        config.mazeCount = 0;
        config.codeTaskCount = 0;
        config.platesCount = plates;
        _created.Add(config);
        return config;
    }

    private PuzzleManager Manager()
    {
        var manager = new PuzzleManager(() => Vector2.zero, 1.5f, null);
        _disposables.Add(manager);
        return manager;
    }

    private UnlockRegistry Registry()
    {
        var config = ScriptableObject.CreateInstance<UnlockConfig>();
        config.startingCommands = new List<string>();
        config.startingConstructs = new List<string>();
        config.unlockAll = false;
        _created.Add(config);

        var registry = new UnlockRegistry(config, _saves.Progress());
        registry.SetKnownMapEvents(PuzzleTypes.All);
        return registry;
    }

    private static PuzzlePlan Generate(
        PuzzleConfig config, int seed, IFogProvider fog, PuzzleManager manager, System.Func<string, bool> isUnlocked)
    {
        return PuzzleGenerator.Generate(config, new GrassMap(seed), fog, null, null, Vector2Int.zero,
            isUnlocked, manager, 0);
    }

    /// <summary>Туман: сейчас ничего не видно, открытые тайлы задаёт тест.</summary>
    private sealed class StubFog : IFogProvider
    {
        private readonly HashSet<Vector2Int> _explored = new HashSet<Vector2Int>();

        public void Explore(RectInt area)
        {
            foreach (Vector2Int tile in area.allPositionsWithin) _explored.Add(tile);
        }

        public bool IsVisible(int x, int y) => false;
        public bool IsExplored(int x, int y) => _explored.Contains(new Vector2Int(x, y));
    }

    /// <summary>Карта 64×64 из травы; тайл (x, y) — мировая точка (x, y).</summary>
    private sealed class GrassMap : IMapDataProvider
    {
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
