using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Lua;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
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
    private const int PlatesId = 3;
    private static readonly Vector2Int PlatesOrigin = new Vector2Int(40, 10);

    private readonly List<Object> _created = new List<Object>();
    private TestHelpers.LuaHost _lua;
    private PuzzleManager _manager;
    private Vector2 _player;
    private CodeTaskPuzzle _obelisk;
    // Запуски кода для попытки головоломки (T25): решать можно только в запуске-попытке
    private TestHelpers.PuzzleRunControl _run;

    [SetUp]
    public void SetUp()
    {
        _lua = new TestHelpers.LuaHost(TestHelpers.ProjectEnergyConfig(), 100f);
        _player = new Vector2(1f, 0f);
        // Награды не нужны: PuzzleManager работает и без PuzzleRewards
        _manager = new PuzzleManager(() => _player, 1.5f, null);
        _run = new TestHelpers.PuzzleRunControl();
        _manager.AttachRunControl(_run);

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
        TestHelpers.StartAttempt(_manager, _run, _obelisk);
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
    public void Solve_WrongAnswer_NilAndReason_CostsTwo_OneAttemptPerRun()
    {
        TestHelpers.StartAttempt(_manager, _run, _obelisk);
        int wrong = _obelisk.GetTask().Answer + 1;
        _lua.Run($"a, b = solve({ObeliskTable}, {wrong})");

        Assert.AreEqual(LuaValueType.Nil, Global("a").Type);
        Assert.AreEqual("wrong answer", Global("b").Read<string>());
        Assert.AreEqual(2f, _lua.Spent, 0.001f);
        Assert.IsFalse(_obelisk.IsSolved);

        // В том же запуске обелиск больше не принимает ответов (вызов всё равно оплачен)
        _lua.Run($"c, d = solve({ObeliskTable}, {_obelisk.GetTask().Answer})");
        Assert.AreEqual(LuaValueType.Nil, Global("c").Type);
        StringAssert.Contains("next run", Global("d").Read<string>());
        Assert.AreEqual(4f, _lua.Spent, 0.001f);
        Assert.IsFalse(_obelisk.IsSolved);

        // T25: попытка одна — запуск закончился без решения, обелиск исчез
        _run.SetRunning(false);
        GameEventBus.PublishRunStarted();
        _lua.Run($"e, f = solve({ObeliskTable}, 1)");
        Assert.AreEqual(LuaValueType.Nil, Global("e").Type);
        Assert.AreEqual("puzzle is gone", Global("f").Read<string>());
    }

    [Test]
    public void Solve_BeforeStart_NotStarted()
    {
        int answer = _obelisk.GetTask().Answer;
        _lua.Run($"a, b = solve({ObeliskTable}, {answer})");
        Assert.AreEqual(LuaValueType.Nil, Global("a").Type);
        Assert.AreEqual("puzzle is not started — step on its start platform", Global("b").Read<string>());

        // Начата, но запуск-попытка ещё не шёл — тоже нет
        Assert.IsTrue(_manager.TryStart(_obelisk));
        _lua.Run($"c, d = solve({ObeliskTable}, {answer})");
        Assert.AreEqual("puzzle is not started — step on its start platform", Global("d").Read<string>());
        Assert.IsFalse(_obelisk.IsSolved);
    }

    [Test]
    public void Solve_Errors_AreNilAndReason()
    {
        TestHelpers.StartAttempt(_manager, _run, _obelisk);
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

    // ─── Стартовая платформа (T25) ────────────────────────────

    private static readonly Vector2Int ObeliskStartTile = new Vector2Int(2, 0);

    [Test]
    public void Puzzle_StateAndStartTile_FollowTheAttempt()
    {
        _obelisk.SetStartPlatform(ObeliskStartTile, null);

        _lua.Run($"w = puzzle({ObeliskTable})");
        LuaTable w = Global("w").Read<LuaTable>();
        Assert.AreEqual("waiting", w["state"].Read<string>());
        Assert.AreEqual((double)ObeliskStartTile.x, w["startX"].Read<double>());
        Assert.AreEqual((double)ObeliskStartTile.y, w["startY"].Read<double>());

        Assert.IsTrue(_manager.TryStart(_obelisk));
        _lua.Run($"a = puzzle({ObeliskTable}).state");
        Assert.AreEqual("armed", Global("a").Read<string>());

        _run.SetRunning(true);
        _lua.Run($"t = puzzle({ObeliskTable}).state");
        Assert.AreEqual("attempt", Global("t").Read<string>());

        int answer = _obelisk.GetTask().Answer;
        _lua.Run($"solve({ObeliskTable}, {answer}) s = puzzle({ObeliskTable}).state");
        Assert.AreEqual("solved", Global("s").Read<string>());
    }

    [Test]
    public void FindObjects_And_Check_ShowStartPlatform()
    {
        _obelisk.SetStartPlatform(ObeliskStartTile, null);
        PlayerCommandExecutor executor = CreateExecutor(new Vector2Int(1, 0));
        var fog = new StubFog { Visible = true };
        new FindObjectsCommand(executor, StubMap.Instance, null, null, fog, _manager).Register(_lua.State, _lua.Meter);
        new CheckCommand(executor, StubMap.Instance, fog, null, null, null, _manager).Register(_lua.State, _lua.Meter);

        _lua.Run("o = findObjects(\"obelisk\")[1] " +
                 "p = check(o.startX, o.startY) n = check(3, 3) " +
                 "st = findObjects(\"obelisk\"):getNear().state");

        LuaTable o = Global("o").Read<LuaTable>();
        Assert.AreEqual((double)ObeliskStartTile.x, o["startX"].Read<double>());
        Assert.AreEqual((double)ObeliskStartTile.y, o["startY"].Read<double>());
        Assert.AreEqual("waiting", o["state"].Read<string>());
        Assert.AreEqual("waiting", Global("st").Read<string>());

        LuaTable p = Global("p").Read<LuaTable>();
        Assert.AreEqual(LuaPuzzles.StartPlatformName, p["type"].Read<string>());
        Assert.AreEqual(LuaPuzzles.StartPlatformName, p["structure"].Read<string>());
        Assert.IsTrue(p["walkable"].Read<bool>());
        LuaTable pz = p["puzzle"].Read<LuaTable>();
        Assert.AreEqual((double)(LuaObjectList.PuzzleIdOffset + ObeliskId), pz["id"].Read<double>());
        Assert.AreEqual(LuaPuzzles.ObeliskTypeName, pz["type"].Read<string>());

        Assert.AreEqual("empty", Global("n").Read<LuaTable>()["type"].Read<string>());

        // Решённая головоломка: платформа скрыта и больше не действует
        TestHelpers.StartAttempt(_manager, _run, _obelisk);
        Assert.AreEqual(PuzzleSolveResult.Correct, _manager.TrySolveCodeTask(_obelisk, _obelisk.GetTask().Answer));
        _lua.Run("q = check(o.startX, o.startY)");
        LuaTable q = Global("q").Read<LuaTable>();
        Assert.AreEqual("empty", q["type"].Read<string>());
        Assert.AreEqual(LuaValueType.Nil, q["puzzle"].Type);
    }

    // ─── Прерывание запуска стартом головоломки (T25) ─────────

    [UnityTest]
    public IEnumerator Sandbox_PuzzleStart_InterruptsRun_NotCountedAsDayRun_EvenUnderPcall()
    {
        var tags = new List<string>();
        void OnEvent(GameEvent e) { if (e.Type == GameEventType.RunFinished) tags.Add(e.Tag); }
        GameEventBus.OnGameEvent += OnEvent;

        var saves = new TestHelpers.TempSaveFolder();
        var unlockConfig = ScriptableObject.CreateInstance<UnlockConfig>();
        var playerConfig = ScriptableObject.CreateInstance<PlayerConfig>();
        _created.Add(unlockConfig);
        _created.Add(playerConfig);
        unlockConfig.unlockAll = false;
        unlockConfig.startingCommands = new List<string> { "print" };
        unlockConfig.startingConstructs = new List<string> { LuaConstructs.For };
        var unlocks = new UnlockRegistry(unlockConfig, saves.Progress());

        var energy = new PlayerEnergy(100f);
        var day = new DayCycle(playerConfig, RunModifiers.Create(playerConfig, new MetaProgress()), energy, null);
        var host = new GameObject("PuzzleSandboxTest");
        _created.Add(host);
        LuaSandbox sandbox = host.AddComponent<LuaSandbox>();
        sandbox.Initialize(CreateExecutor(new Vector2Int(1, 0)), StubMap.Instance, null, TestHelpers.ProjectEnergyConfig(),
            null, energy, null, null, null, null, 1.5f, day, unlocks, null, null, _manager);
        day.Attach(sandbox);
        _manager.AttachRunControl(sandbox);

        // Игрок «встаёт на платформу» посреди кода: print(...) → старт головоломки
        bool startOnPrint = true;
        int prints = 0;
        void OnLog(string text, LogEntryType type)
        {
            prints++;
            if (startOnPrint) _manager.TryStart(_obelisk);
        }
        sandbox.OnLogMessage += OnLog;

        var results = new List<ExecutionResult>();
        int runsAfterInterrupt;
        try
        {
            yield return Run(sandbox, "pcall(print, \"x\")\nfor i = 1, 3 do print(i) end", results);
            runsAfterInterrupt = day.RunsUsed;
            Assert.IsTrue(sandbox.LastRunInterruptedForPuzzle);
            Assert.AreEqual(PuzzleState.Armed, _obelisk.State, "the interrupted run is not the attempt");
            Assert.AreEqual(1, prints, "code after the start must not run (pcall can't swallow the interrupt)");

            // Следующий запуск — попытка: не решили → головоломка исчезла, запуск дня засчитан
            startOnPrint = false;
            yield return Run(sandbox, "for i = 1, 2 do x = i end", results);
        }
        finally
        {
            sandbox.OnLogMessage -= OnLog;
            GameEventBus.OnGameEvent -= OnEvent;
            saves.Dispose();
        }

        Assert.IsTrue(results[0].IsInterruptedForPuzzle);
        Assert.IsTrue(results[0].IsCancelled);
        Assert.IsFalse(results[0].IsSuccess);
        Assert.IsNull(results[0].ErrorMessage, "an interrupted run is not an error");
        Assert.AreEqual(0, runsAfterInterrupt, "the interrupted run does not use a day run");

        Assert.IsTrue(results[1].IsSuccess, results[1].ErrorMessage);
        Assert.IsFalse(sandbox.LastRunInterruptedForPuzzle);
        Assert.AreEqual(1, day.RunsUsed);
        Assert.AreEqual(PuzzleState.Failed, _obelisk.State);
        CollectionAssert.AreEqual(new[] { GameEventBus.RunTagPuzzle, GameEventBus.RunTagOk }, tags);
        Assert.IsFalse(sandbox.IsRunning);
    }

    [UnityTest]
    public IEnumerator Sandbox_InterruptForPuzzle_NotRunning_DoesNothing()
    {
        var saves = new TestHelpers.TempSaveFolder();
        var unlockConfig = ScriptableObject.CreateInstance<UnlockConfig>();
        _created.Add(unlockConfig);
        unlockConfig.unlockAll = true;
        var unlocks = new UnlockRegistry(unlockConfig, saves.Progress());
        var host = new GameObject("PuzzleSandboxIdle");
        _created.Add(host);
        LuaSandbox sandbox = host.AddComponent<LuaSandbox>();
        sandbox.Initialize(CreateExecutor(new Vector2Int(1, 0)), StubMap.Instance, null, TestHelpers.ProjectEnergyConfig(),
            null, new PlayerEnergy(100f), null, null, null, null, 1.5f, null, unlocks, null, null, _manager);

        var results = new List<ExecutionResult>();
        try
        {
            sandbox.InterruptForPuzzle();
            yield return Run(sandbox, "for i = 1, 2 do y = i end", results);
        }
        finally
        {
            saves.Dispose();
        }

        Assert.IsTrue(results[0].IsSuccess, results[0].ErrorMessage);
        Assert.IsFalse(sandbox.LastRunInterruptedForPuzzle);
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

    // ─── Плиты (T16) ──────────────────────────────────────────

    [Test]
    public void Plates_CheckShowsNumber_FindObjectsHasArea_PuzzleDescribes_SolveRefuses()
    {
        PlatesPuzzle plates = CreatePlates();
        PlayerCommandExecutor executor = CreateExecutor(plates.Origin + new Vector2Int(-1, 2));
        _player = new Vector2(plates.Origin.x - 1, plates.Origin.y + 2);
        var fog = new StubFog { Visible = true };
        new CheckCommand(executor, StubMap.Instance, fog, null, null, null, _manager).Register(_lua.State, _lua.Meter);
        new FindObjectsCommand(executor, StubMap.Instance, null, null, fog, _manager).Register(_lua.State, _lua.Meter);

        Vector2Int plateTile = plates.Origin + plates.Layout.Tiles[0];
        Vector2Int emptyTile = FindEmptyPlatesTile(plates);
        string platesTable = $"{{ kind = \"puzzle\", id = {LuaObjectList.PuzzleIdOffset + PlatesId} }}";
        _lua.Run($"p = check({plateTile.x}, {plateTile.y}) e = check({emptyTile.x}, {emptyTile.y}) " +
                 $"list = findObjects(\"plates\") f = list[1] all = findObjects(\"puzzle\").count " +
                 $"t = puzzle({platesTable}) a, b = solve({platesTable}, 1)");

        LuaTable p = Global("p").Read<LuaTable>();
        Assert.AreEqual((double)plates.Layout.Numbers[0], p["plate"].Read<double>());
        Assert.IsFalse(p["pressed"].Read<bool>());
        Assert.AreEqual(LuaPuzzles.PlateName, p["type"].Read<string>());
        Assert.IsTrue(p["walkable"].Read<bool>());
        Assert.AreEqual("plates", p["puzzle"].Read<LuaTable>()["type"].Read<string>());

        LuaTable e = Global("e").Read<LuaTable>();
        Assert.AreEqual(LuaValueType.Nil, e["plate"].Type, "only plate tiles have a number");
        Assert.AreEqual("empty", e["type"].Read<string>());

        Assert.AreEqual(1d, Global("list").Read<LuaTable>()["count"].Read<double>());
        LuaTable f = Global("f").Read<LuaTable>();
        Assert.AreEqual(LuaObjectList.KindPuzzle, f["kind"].Read<string>());
        Assert.AreEqual((double)(LuaObjectList.PuzzleIdOffset + PlatesId), f["id"].Read<double>());
        Assert.AreEqual((double)plates.Origin.x, f["minX"].Read<double>());
        Assert.AreEqual((double)(plates.Origin.y + PlatesLayout.Size - 1), f["maxY"].Read<double>());
        Assert.AreEqual(LuaValueType.Nil, f["plate"].Type, "numbers are not in findObjects");
        Assert.AreEqual(2d, Global("all").Read<double>(), "obelisk + plates");

        LuaTable t = Global("t").Read<LuaTable>();
        Assert.AreEqual("plates", t["kind"].Read<string>());
        Assert.AreEqual(LuaPuzzles.PlatesText, t["text"].Read<string>());
        Assert.AreEqual((double)plates.PlateCount, t["plates"].Read<double>());
        Assert.AreEqual(0d, t["pressedCount"].Read<double>());
        Assert.IsFalse(t["solved"].Read<bool>());

        Assert.AreEqual(LuaValueType.Nil, Global("a").Type);
        Assert.AreEqual(LuaPuzzles.PlatesSolveError, Global("b").Read<string>());
    }

    [Test]
    public void Plates_PressedPlate_ShownByCheck()
    {
        PlatesPuzzle plates = CreatePlates();
        PlayerCommandExecutor executor = CreateExecutor(plates.Origin + new Vector2Int(-1, 0));
        new CheckCommand(executor, StubMap.Instance, new StubFog { Visible = true }, null, null, null, _manager)
            .Register(_lua.State, _lua.Meter);

        // Наименьший номер — первая плита по порядку
        int first = 0;
        for (int i = 1; i < plates.Layout.Count; i++)
            if (plates.Layout.Numbers[i] < plates.Layout.Numbers[first]) first = i;
        Vector2Int tile = plates.Origin + plates.Layout.Tiles[first];
        TestHelpers.StartAttempt(_manager, _run, plates);
        Assert.IsTrue(_manager.StepOnPlates(plates, tile));

        _lua.Run($"p = check({tile.x}, {tile.y})");
        Assert.IsTrue(Global("p").Read<LuaTable>()["pressed"].Read<bool>());
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

    private static IEnumerator Run(LuaSandbox sandbox, string main, List<ExecutionResult> into)
    {
        Task<ExecutionResult> task = sandbox.ExecuteAsync(main, new Dictionary<string, string>()).AsTask();
        for (int frame = 0; frame < 600 && !task.IsCompleted; frame++)
            yield return null;

        Assert.IsTrue(task.IsCompleted, "execution did not finish");
        into.Add(task.Result);
    }

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

    private PlatesPuzzle CreatePlates()
    {
        var go = new GameObject("Plates");
        _created.Add(go);
        PlatesPuzzle plates = go.AddComponent<PlatesPuzzle>();
        plates.Setup(PlatesId, PlatesOrigin + PlatesLayout.Center);
        plates.Build(PlatesLayout.Generate(new System.Random(5)), PlatesOrigin, StubMap.Instance, null);
        _manager.Register(plates);
        return plates;
    }

    private static Vector2Int FindEmptyPlatesTile(PlatesPuzzle plates)
    {
        RectInt area = plates.TileArea;
        for (int x = area.xMin; x < area.xMax; x++)
            for (int y = area.yMin; y < area.yMax; y++)
                if (!plates.IsPlate(new Vector2Int(x, y))) return new Vector2Int(x, y);
        throw new InvalidOperationException("no empty plates tile");
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
        public bool IsExplored(int x, int y) => Visible;
    }
}
