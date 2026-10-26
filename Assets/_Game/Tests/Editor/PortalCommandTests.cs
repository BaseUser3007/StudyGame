using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Lua;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

/// <summary>
/// Портал забега в Lua: enterPortal() (цена, досягаемость, лагерь без портала, отказ RunController,
/// конец запуска с итогом «ok» — не ошибка, код после вызова не выполняется даже под pcall),
/// findObjects("portal") (в findObjects() портала нет), moveTo(portal) — к точке подхода, check на портале, отказы harvest/mine/attack,
/// методы списка с порталом, объяснение ошибки «portal is too far».
/// </summary>
public class PortalCommandTests
{
    // Портал 2×2: тайлы (10..11, 7..8), края — x 9.5..11.5, y 6.5..8.5
    private static readonly RectInt PortalArea = new RectInt(10, 7, 2, 2);
    private static readonly Vector2Int NearTile = new Vector2Int(10, 5);   // до края 1.5
    private static readonly Vector2Int FarTile = new Vector2Int(10, 2);    // до края 4.5
    private const float Reach = 1.5f;

    private readonly List<Object> _created = new List<Object>();
    private TestHelpers.LuaHost _lua;
    private RunPortal _portal;
    private int _exitCalls;
    private bool _exitAllowed;

    [SetUp]
    public void SetUp()
    {
        ResetBus();
        _lua = new TestHelpers.LuaHost(TestHelpers.ProjectEnergyConfig(), 100f);
        _portal = RunPortal.Create(null, StubMap.Instance, new RunLocation(1, NearTile, PortalArea));
        _created.Add(_portal.gameObject);
        _exitCalls = 0;
        _exitAllowed = true;
    }

    [TearDown]
    public void TearDown()
    {
        foreach (Object obj in _created)
            if (obj != null) Object.DestroyImmediate(obj);
        _created.Clear();
        ResetBus();
    }

    private LuaValue Global(string name) => _lua.State.Environment[name];

    private bool FakeExit()
    {
        _exitCalls++;
        return _exitAllowed;
    }

    private void RegisterEnterPortal(PlayerCommandExecutor executor, RunPortal portal)
    {
        new EnterPortalCommand(executor, StubMap.Instance, portal, Reach, FakeExit).Register(_lua.State, _lua.Meter);
    }

    // ─── enterPortal() ────────────────────────────────────────

    [Test]
    public void EnterPortal_TooFar_NilAndReason_CallIsPaid()
    {
        RegisterEnterPortal(CreateExecutor(FarTile), _portal);
        _lua.Run("a, b = enterPortal()");

        Assert.AreEqual(LuaValueType.Nil, Global("a").Type);
        Assert.AreEqual(EnterPortalCommand.TooFarError, Global("b").Read<string>());
        Assert.AreEqual("portal is too far", EnterPortalCommand.TooFarError, "Lua compares this text");
        Assert.AreEqual(0, _exitCalls, "too far — no exit request");
        Assert.AreEqual(1f, _lua.Spent, 0.001f);
    }

    [Test]
    public void EnterPortal_InHub_NoPortal()
    {
        RegisterEnterPortal(CreateExecutor(NearTile), null);
        _lua.Run("a, b = enterPortal()");

        Assert.AreEqual(LuaValueType.Nil, Global("a").Type);
        Assert.AreEqual(EnterPortalCommand.NoPortalError, Global("b").Read<string>());
        Assert.AreEqual(0, _exitCalls);
    }

    [Test]
    public void EnterPortal_ExitRefused_NilAndReason()
    {
        _exitAllowed = false;
        RegisterEnterPortal(CreateExecutor(NearTile), _portal);
        _lua.Run("a, b = enterPortal()");

        Assert.AreEqual(LuaValueType.Nil, Global("a").Type);
        Assert.AreEqual(EnterPortalCommand.CantLeaveError, Global("b").Read<string>());
        Assert.AreEqual(1, _exitCalls);
    }

    [Test]
    public void EnterPortal_Near_RequestsExit_AndStopsTheCode()
    {
        RegisterEnterPortal(CreateExecutor(NearTile), _portal);

        Assert.That(() => _lua.Run("enterPortal()\nafter = 1"), Throws.Exception);
        Assert.AreEqual(1, _exitCalls);
        Assert.AreEqual(LuaValueType.Nil, Global("after").Type, "code after enterPortal() must not run");
        Assert.AreEqual(1f, _lua.Spent, 0.001f, "no refund");
    }

    // ─── Запуск в песочнице: итог «ok» ────────────────────────

    [UnityTest]
    public IEnumerator Sandbox_EnterPortal_EndsRunWithOkOutcome_EvenUnderPcall()
    {
        var tags = new List<string>();
        void OnEvent(GameEvent e) { if (e.Type == GameEventType.RunFinished) tags.Add(e.Tag); }
        GameEventBus.OnGameEvent += OnEvent;

        var saves = new TestHelpers.TempSaveFolder();
        var config = ScriptableObject.CreateInstance<UnlockConfig>();
        _created.Add(config);
        config.unlockAll = false;
        config.startingCommands = new List<string> { "enterPortal" };
        config.startingConstructs = new List<string> { LuaConstructs.For };
        var unlocks = new UnlockRegistry(config, saves.Progress());

        var energy = new PlayerEnergy(100f);
        var host = new GameObject("PortalSandboxTest");
        _created.Add(host);
        LuaSandbox sandbox = host.AddComponent<LuaSandbox>();
        sandbox.Initialize(CreateExecutor(NearTile), StubMap.Instance, null, TestHelpers.ProjectEnergyConfig(), null,
            energy, null, null, null, null, Reach, null, unlocks, null, null, null, _portal);
        var exit = new FakeExitService();
        sandbox.RunExit = exit;

        var results = new List<ExecutionResult>();
        try
        {
            yield return Run(sandbox, "enterPortal()\nafter = 1", results);
            yield return Run(sandbox, "pcall(enterPortal)\nfor i = 1, 3 do looped = i end", results);
        }
        finally
        {
            GameEventBus.OnGameEvent -= OnEvent;
            saves.Dispose();
        }

        Assert.IsTrue(results[0].IsSuccess, results[0].ErrorMessage);
        Assert.IsTrue(results[1].IsSuccess, results[1].ErrorMessage);
        Assert.IsFalse(results[1].IsCancelled);
        CollectionAssert.AreEqual(new[] { GameEventBus.RunTagOk, GameEventBus.RunTagOk }, tags,
            "leaving through the portal is not an error (first_bug) and not a Stop");
        Assert.AreEqual(2, exit.Calls);
        Assert.IsFalse(sandbox.IsRunning);
        // Код после выхода не выполнялся (в том числе цикл после pcall)
        Assert.AreEqual(1f + 1f, energy.TotalSpent, 0.001f, "only the two calls are paid");
    }

    [Test]
    public void Registry_EnterPortal_HasCostAndHelp()
    {
        var command = new EnterPortalCommand(null, null, null, Reach, null);
        Assert.AreEqual("enterPortal", command.Name);
        Assert.Greater(command.EnergyCost, 0f);
        Assert.AreEqual("enterPortal()", command.Signature);
        Assert.IsTrue(Loc.HasTranslation(command.Description), "no Russian translation of the description");

        var registry = new LuaCommandRegistry();
        registry.RegisterCommand(command);
        var counter = new EnergyCounter(TestHelpers.ProjectEnergyConfig(), registry);
        Assert.AreEqual(command.EnergyCost, counter.CalculateTotalCost("enterPortal()"), 0.001f,
            "static price = runtime price");
    }

    // ─── findObjects / check ──────────────────────────────────

    [Test]
    public void FindObjects_Portal_FieldsAndVisibility()
    {
        PlayerCommandExecutor executor = CreateExecutor(NearTile);
        var fog = new StubFog { Visible = false, Explored = false };
        new FindObjectsCommand(executor, StubMap.Instance, CreateObjectManager(), null, fog, null, _portal)
            .Register(_lua.State, _lua.Meter);

        _lua.Run("unseen = findObjects(\"portal\").count");
        Assert.AreEqual(0d, Global("unseen").Read<double>(), "never seen — not found");

        // Видели раньше, сейчас под туманом: по типу находится, в findObjects() — нет
        fog.Explored = true;
        _lua.Run("remembered = findObjects(\"PORTAL\").count all = findObjects().count");
        Assert.AreEqual(1d, Global("remembered").Read<double>());
        Assert.AreEqual(0d, Global("all").Read<double>());

        fog.Visible = true;
        // Виден сейчас: по типу находится, а findObjects() без аргумента портал не включает
        // (игрок появляется рядом, и findObjects():getNear() отдавал бы портал вместо ресурса)
        _lua.Run("list = findObjects(\"portal\") p = list[1] visible = findObjects().count");
        Assert.AreEqual(0d, Global("visible").Read<double>(), "findObjects() must not list the portal");

        LuaTable p = Global("p").Read<LuaTable>();
        Assert.AreEqual(LuaObjectList.KindPortal, p["kind"].Read<string>());
        Assert.AreEqual("portal", p["kind"].Read<string>());
        Assert.AreEqual("portal", p["type"].Read<string>());
        Assert.AreEqual(LuaPortal.PortalId, p["id"].Read<double>());
        Assert.AreEqual((double)_portal.Position.x, p["x"].Read<double>(), 0.001);
        Assert.AreEqual((double)_portal.Position.y, p["y"].Read<double>(), 0.001);
        Assert.AreEqual(1.5d, p["distance"].Read<double>(), 0.001, "distance — to the portal edge");
        Assert.AreEqual(0d, p["durability"].Read<double>());
        // Точка подхода — тайл появления (вплотную к порталу, проходимый)
        Assert.AreEqual((double)NearTile.x, p["approachX"].Read<double>(), 0.001);
        Assert.AreEqual((double)NearTile.y, p["approachY"].Read<double>(), 0.001);
        Assert.AreEqual(NearTile, _portal.ApproachTile);
    }

    [Test]
    public void MoveTo_PortalTable_UsesApproachPoint()
    {
        // moveTo читает цель так же: портал — точка подхода, остальное — x/y как раньше
        _lua.State.Environment["probe"] = new LuaFunction((context, ct) =>
        {
            bool ok = LuaPortal.TryReadApproach(context, out Vector2 point);
            context.Return(ok, (double)point.x, (double)point.y);
            return new ValueTask<int>(3);
        });

        _lua.Run("ok, ax, ay = probe({ kind = \"portal\", x = 10.5, y = 7.5, approachX = 10, approachY = 6 }) " +
                 "other = probe({ kind = \"object\", x = 3, y = 4, approachX = 1, approachY = 1 }) " +
                 "plain = probe(3, 4)");

        Assert.IsTrue(Global("ok").Read<bool>());
        Assert.AreEqual(10d, Global("ax").Read<double>(), 0.001);
        Assert.AreEqual(6d, Global("ay").Read<double>(), 0.001);
        Assert.IsFalse(Global("other").Read<bool>(), "only portal tables use the approach point");
        Assert.IsFalse(Global("plain").Read<bool>());
    }

    [Test]
    public void FindObjects_UnknownType_ListsPortal()
    {
        new FindObjectsCommand(CreateExecutor(NearTile), StubMap.Instance, CreateObjectManager(), null, null)
            .Register(_lua.State, _lua.Meter);

        var ex = Assert.Catch(() => _lua.Run("findObjects(\"portl\")"));
        StringAssert.Contains(", portal)", ex.ToString());
    }

    [Test]
    public void FindObjectsAll_ExcludesPortal_ListMethodsWorkWithPortal()
    {
        var manager = new PuzzleManager(() => (Vector2)NearTile, Reach, null);
        try
        {
            var go = new GameObject("Obelisk");
            _created.Add(go);
            CodeTaskPuzzle obelisk = go.AddComponent<CodeTaskPuzzle>();
            obelisk.Setup(1, new Vector2(4f, 5f));
            obelisk.Initialize(CodeTaskKind.Sum, 5, Rect.zero);
            manager.Register(obelisk);

            new FindObjectsCommand(CreateExecutor(NearTile), StubMap.Instance, CreateObjectManager(), null,
                new StubFog { Visible = true, Explored = true }, manager, _portal).Register(_lua.State, _lua.Meter);

            // findObjects() — без портала: getNear() даёт обелиск, хотя портал ближе (край 1.5 против ~6)
            _lua.Run("list = findObjects() n = list.count near = list:getNear().kind " +
                     "portals = list:where(\"kind\", \"==\", \"portal\").count " +
                     "plist = findObjects(\"portal\") pnear = plist:getNear().kind " +
                     "found = plist:find(\"type\", \"portal\").id has = plist:contains(plist[1]) " +
                     "sorted = plist:sortBy(\"distance\")[1].kind dist = plist:sum(\"distance\")");
        }
        finally
        {
            manager.Dispose();
        }

        Assert.AreEqual(1d, Global("n").Read<double>());
        Assert.AreEqual(LuaObjectList.KindPuzzle, Global("near").Read<string>(), "the portal is not in findObjects()");
        Assert.AreEqual(0d, Global("portals").Read<double>());
        Assert.AreEqual(LuaObjectList.KindPortal, Global("pnear").Read<string>());
        Assert.AreEqual(LuaPortal.PortalId, Global("found").Read<double>());
        Assert.IsTrue(Global("has").Read<bool>());
        Assert.AreEqual(LuaObjectList.KindPortal, Global("sorted").Read<string>());
        Assert.AreEqual(1.5d, Global("dist").Read<double>(), 0.001);
    }

    [Test]
    public void Check_PortalTile_TypePortal_NotWalkable()
    {
        new CheckCommand(CreateExecutor(NearTile), StubMap.Instance, new StubFog { Visible = true }, null, null, null,
            null, _portal).Register(_lua.State, _lua.Meter);

        _lua.Run("c = check(11, 8) o = check(12, 8)");

        LuaTable c = Global("c").Read<LuaTable>();
        Assert.AreEqual("portal", c["type"].Read<string>());
        Assert.AreEqual("portal", c["structure"].Read<string>());
        Assert.IsTrue(_portal.IsSolid);
        Assert.IsFalse(c["walkable"].Read<bool>());
        Assert.AreEqual(LuaPortal.PortalId, c["portal"].Read<LuaTable>()["id"].Read<double>());

        LuaTable o = Global("o").Read<LuaTable>();
        Assert.AreEqual("empty", o["type"].Read<string>());
        Assert.IsTrue(o["walkable"].Read<bool>());
        Assert.AreEqual(LuaValueType.Nil, o["portal"].Type);
    }

    // ─── harvest / mine / attack ──────────────────────────────

    [Test]
    public void HarvestMineAttack_OnPortal_Refused()
    {
        PlayerCommandExecutor executor = CreateExecutor(NearTile);
        new HarvestCommand(executor, StubMap.Instance, null, null, null, null, _portal).Register(_lua.State, _lua.Meter);
        new MineCommand(executor, StubMap.Instance, null, null, null, null, _portal).Register(_lua.State, _lua.Meter);
        new AttackCommand(executor, StubMap.Instance, null, null, null, _portal).Register(_lua.State, _lua.Meter);

        string portal = $"{{ kind = \"portal\", type = \"portal\", id = {LuaPortal.PortalId:0}, x = 10.5, y = 7.5 }}";
        _lua.Run($"h1, h2 = harvest({portal}) m1, m2 = mine(10, 7) a1, a2 = attack({portal}) b1, b2 = attack(11, 8)");

        Assert.IsFalse(Global("h1").Read<bool>());
        Assert.AreEqual("portal can't be harvested — use enterPortal()", Global("h2").Read<string>());
        Assert.IsFalse(Global("m1").Read<bool>());
        Assert.AreEqual("portal can't be mined — use enterPortal()", Global("m2").Read<string>());
        Assert.AreEqual(LuaValueType.Nil, Global("a1").Type);
        Assert.AreEqual("portal can't be attacked — use enterPortal()", Global("a2").Read<string>());
        Assert.AreEqual("portal can't be attacked — use enterPortal()", Global("b2").Read<string>());
    }

    // ─── Объяснение ошибки ────────────────────────────────────

    [Test]
    public void ErrorExplainer_PortalTooFar_IsExplained()
    {
        LuaErrorExplanation e = LuaErrorExplainer.Explain(
            "Runtime error: Lua-CSharp: [string \"Main\"]:3: portal is too far", Array.Empty<ILuaCommand>());

        Assert.AreEqual(3, e.Line);
        StringAssert.Contains("enterPortal()", e.Explanation);
        StringAssert.Contains("findObjects(\"portal\")", e.Suggestion);
    }

    // ─── Helpers ──────────────────────────────────────────────

    private static void ResetBus()
    {
        MethodInfo reset = typeof(GameEventBus).GetMethod("ResetStatics", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(reset, "GameEventBus.ResetStatics not found");
        reset.Invoke(null, null);
    }

    private static IEnumerator Run(LuaSandbox sandbox, string main, List<ExecutionResult> into)
    {
        Task<ExecutionResult> task = sandbox.ExecuteAsync(main, new Dictionary<string, string>()).AsTask();
        for (int frame = 0; frame < 600 && !task.IsCompleted; frame++)
            yield return null;

        Assert.IsTrue(task.IsCompleted, "execution did not finish");
        into.Add(task.Result);
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

    // Пустой менеджер объектов (без Initialize): findObjects() без аргумента не находит ресурсов
    private ObjectManager CreateObjectManager()
    {
        var go = new GameObject("Objects");
        _created.Add(go);
        return go.AddComponent<ObjectManager>();
    }

    private sealed class FakeExitService : IRunExitService
    {
        public int Calls;
        public bool IsExitPending { get; private set; }

        public bool RequestExitThroughPortal()
        {
            Calls++;
            IsExitPending = true;
            return true;
        }
    }

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
        public bool Explored;
        public bool IsVisible(int x, int y) => Visible;
        public bool IsExplored(int x, int y) => Explored || Visible;
    }
}
