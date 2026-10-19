using System;
using System.Collections.Generic;
using Lua;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Lua-сторона головоломок (этап 6): puzzle()/solve() — цена, досягаемость, ответ, вычисленный кодом в том же
/// запуске, ошибки nil + причина; findObjects("maze") под туманом; check на стене лабиринта; EnergyCounter
/// не считает поле info.puzzle вызовом команды.
/// </summary>
public class PuzzleCommandTests
{
    private const int ObeliskId = 1;
    private const int MazeId = 2;
    private static readonly Vector2Int MazeOrigin = new Vector2Int(20, 20);

    private readonly List<Object> _created = new List<Object>();
    private TestHelpers.LuaHost _lua;
    private PuzzleManager _manager;
    private Vector2 _player;
    private CodeTaskPuzzle _obelisk;

    [SetUp]
    public void SetUp()
    {
        _lua = new TestHelpers.LuaHost(TestHelpers.ProjectEnergyConfig(), 100f);
        _player = new Vector2(1f, 0f);
        // Награды не нужны: PuzzleManager работает и без PuzzleRewards
        _manager = new PuzzleManager(() => _player, 1.5f, null);

        var go = new GameObject("Obelisk");
        _created.Add(go);
        _obelisk = go.AddComponent<CodeTaskPuzzle>();
        _obelisk.Setup(ObeliskId, Vector2.zero);
        _obelisk.Initialize(CodeTaskKind.Sum, 5, Rect.zero);
        _manager.Register(_obelisk);

        new PuzzleCommand(null, null, _manager).Register(_lua.State, _lua.Meter);
        new SolveCommand(_manager).Register(_lua.State, _lua.Meter);
    }

    [TearDown]
    public void TearDown()
    {
        _manager.Dispose();
        foreach (Object obj in _created)
            if (obj != null) Object.DestroyImmediate(obj);
        _created.Clear();
    }

    private LuaValue Global(string name) => _lua.State.Environment[name];

    private static string ObeliskTable => $"{{ kind = \"puzzle\", id = {LuaObjectList.PuzzleIdOffset + ObeliskId} }}";

    // ─── puzzle() ─────────────────────────────────────────────

    [Test]
    public void Puzzle_ReturnsTaskOfThisRun_AndCostsOne()
    {
        _lua.Run($"t = puzzle({ObeliskTable})");

        Assert.AreEqual(1f, _lua.Spent, 0.001f);
        Assert.IsTrue(Global("t").TryRead(out LuaTable t));

        CodeTask task = _obelisk.GetTask();
        Assert.AreEqual("sum", t["kind"].Read<string>());
        Assert.AreEqual(task.Text, t["text"].Read<string>());
        Assert.AreEqual(0d, t["limit"].Read<double>());
        Assert.IsFalse(t["solved"].Read<bool>());

        LuaTable input = t["input"].Read<LuaTable>();
        Assert.AreEqual(CodeTask.InputLength, input.ArrayLength);
        for (int i = 0; i < task.Input.Count; i++)
            Assert.AreEqual(task.Input[i], input[i + 1].Read<double>());
    }

    [Test]
    public void Puzzle_ByPointAndNearest_FindTheSameObelisk()
    {
        _lua.Run("a = puzzle(0, 0) b = puzzle()");

        Assert.AreEqual("sum", Global("a").Read<LuaTable>()["kind"].Read<string>());
        Assert.AreEqual("sum", Global("b").Read<LuaTable>()["kind"].Read<string>());
        Assert.AreEqual(2f, _lua.Spent, 0.001f);
    }

    [Test]
    public void Puzzle_TooFar_OrMissing_ReturnsNilAndReason_ButCallIsPaid()
    {
        _player = new Vector2(10f, 0f);
        _lua.Run($"a, b = puzzle({ObeliskTable}) c, d = puzzle(50, 50) e, f = puzzle()");

        Assert.AreEqual(LuaValueType.Nil, Global("a").Type);
        Assert.AreEqual("too far from the puzzle", Global("b").Read<string>());
        Assert.AreEqual(LuaValueType.Nil, Global("c").Type);
        Assert.AreEqual("no puzzle at (50, 50)", Global("d").Read<string>());
        Assert.AreEqual(LuaValueType.Nil, Global("e").Type);
        Assert.AreEqual("no puzzle within reach", Global("f").Read<string>());
        Assert.AreEqual(3f, _lua.Spent, 0.001f);
    }

    // ─── solve() ──────────────────────────────────────────────

    [Test]
    public void Solve_AnswerComputedInCode_SolvesOnce()
    {
        var solved = new List<GameEvent>();
        void OnEvent(GameEvent e) { if (e.Type == GameEventType.PuzzleSolved) solved.Add(e); }
        GameEventBus.OnGameEvent += OnEvent;
        try
        {
            _lua.Run(
                $"local obj = {ObeliskTable}\n" +
                "local t = puzzle(obj)\n" +
                "local s = 0\n" +
                "for i = 1, #t.input do s = s + t.input[i] end\n" +
                "ok, err = solve(obj, s)\n" +
                "ok2, err2 = solve(obj, s)\n" +
                "after = puzzle(obj).solved");
        }
        finally
        {
            GameEventBus.OnGameEvent -= OnEvent;
        }

        Assert.IsTrue(Global("ok").Read<bool>());
        Assert.AreEqual(LuaValueType.Nil, Global("err").Type);
        Assert.AreEqual(LuaValueType.Nil, Global("ok2").Type);
        Assert.AreEqual("puzzle is already solved", Global("err2").Read<string>());
        Assert.IsTrue(Global("after").Read<bool>());
        Assert.IsTrue(_obelisk.IsSolved);
        Assert.AreEqual(1, solved.Count);
        Assert.AreEqual(PuzzleTypes.CodeTask, solved[0].Id);
    }

    [Test]
    public void Solve_WrongAnswer_NilAndReason_CostsTwo_AttemptsUnlimited()
    {
        int wrong = _obelisk.GetTask().Answer + 1;
        _lua.Run($"a, b = solve({ObeliskTable}, {wrong})");

        Assert.AreEqual(LuaValueType.Nil, Global("a").Type);
        Assert.AreEqual("wrong answer", Global("b").Read<string>());
        Assert.AreEqual(2f, _lua.Spent, 0.001f);
        Assert.IsFalse(_obelisk.IsSolved);

        _lua.Run($"c = solve({ObeliskTable}, {_obelisk.GetTask().Answer})");
        Assert.IsTrue(Global("c").Read<bool>());
        Assert.AreEqual(4f, _lua.Spent, 0.001f);
    }

    [Test]
    public void Solve_Errors_AreNilAndReason()
    {
        _lua.Run($"a, b = solve({ObeliskTable}, \"ten\") c, d = solve({{ kind = \"puzzle\", id = 5 }}, 1)");

        Assert.AreEqual(LuaValueType.Nil, Global("a").Type);
        Assert.AreEqual("answer must be a number", Global("b").Read<string>());
        Assert.AreEqual(LuaValueType.Nil, Global("c").Type);
        Assert.AreEqual("puzzle is gone", Global("d").Read<string>());

        _player = new Vector2(10f, 0f);
        _lua.Run($"e, f = solve({ObeliskTable}, {_obelisk.GetTask().Answer})");
        Assert.AreEqual("too far from the puzzle", Global("f").Read<string>());
        Assert.IsFalse(_obelisk.IsSolved);
    }

    // ─── Лабиринт ─────────────────────────────────────────────

    [Test]
    public void Maze_PuzzleDescribesGoal_SolveRefuses()
    {
        MazePuzzle maze = CreateMaze();
        _player = maze.EntranceTile;
        string mazeTable = $"{{ kind = \"puzzle\", id = {LuaObjectList.PuzzleIdOffset + MazeId} }}";

        _lua.Run($"t = puzzle({mazeTable}) a, b = solve({mazeTable}, 1)");

        LuaTable t = Global("t").Read<LuaTable>();
        Assert.AreEqual("maze", t["kind"].Read<string>());
        Assert.AreEqual(LuaPuzzles.MazeText, t["text"].Read<string>());
        Assert.AreEqual(0, t["input"].Read<LuaTable>().ArrayLength);
        Assert.AreEqual(LuaValueType.Nil, Global("a").Type);
        Assert.AreEqual("not a code puzzle", Global("b").Read<string>());
    }

    [Test]
    public void FindObjects_Maze_OnlyWhenNotUnderFog()
    {
        MazePuzzle maze = CreateMaze();
        PlayerCommandExecutor executor = CreateExecutor(maze.ApproachTile);
        var fog = new StubFog { Visible = false };
        new FindObjectsCommand(executor, StubMap.Instance, null, null, fog, _manager).Register(_lua.State, _lua.Meter);

        _lua.Run("hidden = findObjects(\"maze\").count");
        Assert.AreEqual(0d, Global("hidden").Read<double>());

        fog.Visible = true;
        _lua.Run("list = findObjects(\"maze\") m = list[1] all = findObjects(\"puzzle\").count");

        Assert.AreEqual(1d, Global("list").Read<LuaTable>()["count"].Read<double>());
        LuaTable m = Global("m").Read<LuaTable>();
        Assert.AreEqual(LuaObjectList.KindPuzzle, m["kind"].Read<string>());
        Assert.AreEqual("maze", m["type"].Read<string>());
        Assert.AreEqual((double)(LuaObjectList.PuzzleIdOffset + MazeId), m["id"].Read<double>());
        Assert.AreEqual((double)maze.CenterTile.x, m["x"].Read<double>());
        Assert.AreEqual((double)maze.ApproachTile.x, m["entranceX"].Read<double>());
        Assert.IsFalse(m["solved"].Read<bool>());
        Assert.AreEqual(2d, Global("all").Read<double>());
    }

    [Test]
    public void Check_MazeWall_IsNotWalkable_AndReportsPuzzle()
    {
        MazePuzzle maze = CreateMaze();
        PlayerCommandExecutor executor = CreateExecutor(maze.ApproachTile);
        new CheckCommand(executor, StubMap.Instance, new StubFog { Visible = true }, null, null, null, _manager)
            .Register(_lua.State, _lua.Meter);

        Vector2Int wall = FindTile(maze, wantWall: true);
        Vector2Int open = FindTile(maze, wantWall: false);
        Vector2Int center = maze.CenterTile;
        _lua.Run($"w = check({wall.x}, {wall.y}) o = check({open.x}, {open.y}) c = check({center.x}, {center.y}) " +
                 "outside = check(0, 10)");

        LuaTable w = Global("w").Read<LuaTable>();
        Assert.IsFalse(w["walkable"].Read<bool>());
        Assert.AreEqual(LuaPuzzles.WallName, w["type"].Read<string>());
        Assert.AreEqual(LuaPuzzles.WallName, w["structure"].Read<string>());
        Assert.AreEqual("maze", w["puzzle"].Read<LuaTable>()["type"].Read<string>());

        LuaTable o = Global("o").Read<LuaTable>();
        Assert.IsTrue(o["walkable"].Read<bool>());
        Assert.AreEqual("empty", o["type"].Read<string>());
        Assert.AreEqual(LuaObjectList.KindPuzzle, o["puzzle"].Read<LuaTable>()["kind"].Read<string>());

        Assert.AreEqual(LuaPuzzles.ChestName, Global("c").Read<LuaTable>()["type"].Read<string>());
        Assert.AreEqual(LuaValueType.Nil, Global("outside").Read<LuaTable>()["puzzle"].Type);
    }

    // ─── EnergyCounter ────────────────────────────────────────

    [Test]
    public void EnergyCounter_FieldNamedLikeCommand_IsFree()
    {
        var registry = new LuaCommandRegistry();
        registry.RegisterCommand(new PuzzleCommand(null, null, _manager));
        var counter = new EnergyCounter(TestHelpers.ProjectEnergyConfig(), registry);

        Assert.AreEqual(1f, counter.CalculateTotalCost("t = puzzle(obj)"), 0.001f);
        Assert.AreEqual(0f, counter.CalculateTotalCost("p = info.puzzle"), 0.001f);
        Assert.AreEqual(0f, counter.CalculateTotalCost("p = info:puzzle()"), 0.001f);
        // «..» — конкатенация, команда настоящая
        Assert.AreEqual(1f, counter.CalculateTotalCost("s = \"x\" .. puzzle(obj).text"), 0.001f);
    }

    // ─── Helpers ──────────────────────────────────────────────

    private MazePuzzle CreateMaze()
    {
        var wallPrefab = new GameObject("WallPrefab");
        wallPrefab.AddComponent<BoxCollider2D>();
        var config = ScriptableObject.CreateInstance<PuzzleConfig>();
        config.mazeWallPrefab = wallPrefab;
        _created.Add(wallPrefab);
        _created.Add(config);

        var go = new GameObject("Maze");
        _created.Add(go);
        MazePuzzle maze = go.AddComponent<MazePuzzle>();
        MazeLayout layout = MazeLayout.Generate(new System.Random(7));
        maze.Build(layout, MazeOrigin, StubMap.Instance, config);
        maze.Setup(MazeId, MazeOrigin + layout.Center);
        _manager.Register(maze);
        return maze;
    }

    private PlayerCommandExecutor CreateExecutor(Vector2Int tile)
    {
        var config = ScriptableObject.CreateInstance<PlayerConfig>();
        var go = new GameObject("Player");
        _created.Add(config);
        _created.Add(go);

        PlayerCommandExecutor executor = go.AddComponent<PlayerCommandExecutor>();
        executor.Initialize(StubMap.Instance, config, tile);
        go.transform.position = StubMap.Instance.TileIndexToWorld(tile.x, tile.y);
        return executor;
    }

    private static Vector2Int FindTile(MazePuzzle maze, bool wantWall)
    {
        RectInt inner = maze.InnerArea;
        for (int x = inner.xMin; x < inner.xMax; x++)
            for (int y = inner.yMin; y < inner.yMax; y++)
            {
                var tile = new Vector2Int(x, y);
                if (maze.IsWall(tile) == wantWall && tile != maze.CenterTile) return tile;
            }
        throw new InvalidOperationException("no suitable maze tile");
    }

    /// <summary>Карта 64×64 из травы; тайл (x, y) — мировая точка (x, y).</summary>
    private sealed class StubMap : IMapDataProvider
    {
        public static readonly StubMap Instance = new StubMap();

        public int MapWidth => 64;
        public int MapHeight => 64;
        public int Seed => 1;
        public bool IsWalkable(int x, int y) => IsInBounds(x, y);
        public bool IsInBounds(int x, int y) => x >= 0 && y >= 0 && x < MapWidth && y < MapHeight;
        public TileType GetTileType(int x, int y) => TileType.Grass;
        public Biome GetBiome(int x, int y) => null;
        public Vector3 TileIndexToWorld(int x, int y) => new Vector3(x, y, 0f);
        public Vector2Int WorldToTileIndex(Vector3 worldPos) => MapPosition.ToTile(worldPos);
        public Vector3 MapToWorld(Vector2 mapPos) => new Vector3(mapPos.x, mapPos.y, 0f);
    }

    private sealed class StubFog : IFogProvider
    {
        public bool Visible;
        public bool IsVisible(int x, int y) => Visible;
    }
}
