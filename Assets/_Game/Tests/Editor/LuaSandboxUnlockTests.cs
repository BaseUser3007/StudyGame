using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Песочница без сцены: в реестр попадают только открытые команды, закрытые — заглушки с ошибкой,
/// закрытые конструкции и окна-«классы» отклоняются до выполнения и без траты энергии,
/// открытие команды подключает её сразу.
/// Мировые сервисы (исполнитель, карта, инвентарь...) не нужны: проверяется только код без действий в мире.
/// </summary>
public class LuaSandboxUnlockTests
{
    private const float StartEnergy = 100f;

    private TestHelpers.TempSaveFolder _saves;
    private UnlockConfig _config;
    private GameObject _host;
    private LuaSandbox _sandbox;
    private PlayerEnergy _energy;
    private UnlockRegistry _unlocks;
    private int _appliedCount;

    [SetUp]
    public void SetUp()
    {
        _saves = new TestHelpers.TempSaveFolder();

        _config = ScriptableObject.CreateInstance<UnlockConfig>();
        _config.startingCommands = new List<string> { "getPosition" };
        _config.startingConstructs = new List<string> { LuaConstructs.Local, LuaConstructs.If };
        _unlocks = new UnlockRegistry(_config, _saves.Progress());

        _energy = new PlayerEnergy(StartEnergy);
        _host = new GameObject("LuaSandboxTest");
        _sandbox = _host.AddComponent<LuaSandbox>();
        _sandbox.Initialize(null, null, null, TestHelpers.ProjectEnergyConfig(), null, _energy, null, null,
            null, null, 1f, null, _unlocks);

        _appliedCount = 0;
        _sandbox.OnUnlocksApplied += HandleApplied;
    }

    [TearDown]
    public void TearDown()
    {
        _sandbox.OnUnlocksApplied -= HandleApplied;
        Object.DestroyImmediate(_host);
        Object.DestroyImmediate(_config);
        _saves.Dispose();
    }

    private void HandleApplied() => _appliedCount++;

    /// <summary>Запускает код и ждёт результата (код без await завершается сразу).</summary>
    private IEnumerator Run(string main, Dictionary<string, string> aux, List<ExecutionResult> into)
    {
        Task<ExecutionResult> task = _sandbox.ExecuteAsync(main, aux ?? new Dictionary<string, string>()).AsTask();
        for (int frame = 0; frame < 300 && !task.IsCompleted; frame++)
            yield return null;

        Assert.IsTrue(task.IsCompleted, "execution did not finish");
        into.Add(task.Result);
    }

    // ─── Реестр команд ────────────────────────────────────────

    [Test]
    public void Initialize_OnlyUnlockedCommandsInRegistry()
    {
        Assert.IsTrue(_sandbox.Registry.HasCommand("getPosition"));
        Assert.IsFalse(_sandbox.Registry.HasCommand("move"));
        Assert.IsFalse(_sandbox.Registry.HasCommand("mine"));
        Assert.AreEqual(1, _sandbox.Registry.GetAllCommands().Count);
    }

    [Test]
    public void Initialize_AllCommandsListsLockedOnesToo()
    {
        var names = new List<string>();
        foreach (ILuaCommand command in _sandbox.AllCommands)
            names.Add(command.Name);

        CollectionAssert.Contains(names, "move");
        CollectionAssert.Contains(names, "mine");
        CollectionAssert.Contains(names, "getPosition");
        CollectionAssert.AllItemsAreUnique(names);
    }

    [Test]
    public void UnlockCommand_WhileIdle_RegisteredImmediatelyAndEventFired()
    {
        Assert.IsTrue(_unlocks.UnlockCommand("move"));

        Assert.IsTrue(_sandbox.Registry.HasCommand("move"));
        Assert.AreEqual(1, _appliedCount);
    }

    [Test]
    public void UnlockCommand_LockedCommandsStayOutOfEnergyCounter()
    {
        // Цена строки с закрытой командой не должна учитывать её стоимость (её нет в реестре)
        float locked = _sandbox.CalculateEnergyCost(new Dictionary<string, string> { { "Main", "move(\"right\")" } });
        _unlocks.UnlockCommand("move");
        float unlocked = _sandbox.CalculateEnergyCost(new Dictionary<string, string> { { "Main", "move(\"right\")" } });

        Assert.Greater(unlocked, locked);
    }

    // ─── Выполнение ───────────────────────────────────────────

    [UnityTest]
    public IEnumerator ExecuteAsync_LockedCommandCall_ErrorAndNoEnergySpent()
    {
        var results = new List<ExecutionResult>();
        yield return Run("x = 1\nmove(\"right\")", null, results);

        Assert.IsFalse(results[0].IsSuccess);
        StringAssert.Contains("command `move` is not unlocked yet", results[0].ErrorMessage);
        Assert.AreEqual(StartEnergy, _energy.Current, 0.001f);
    }

    [UnityTest]
    public IEnumerator ExecuteAsync_LockedConstruct_RefusedBeforeExecution()
    {
        var results = new List<ExecutionResult>();
        // Если бы код выполнился, if потратил бы энергию до цикла
        yield return Run("if true then x = 1 end\nfor i = 1, 3 do end", null, results);

        Assert.AreEqual("Runtime error: loop `for` is not unlocked yet (Main, line 2)", results[0].ErrorMessage);
        Assert.AreEqual(StartEnergy, _energy.Current, 0.001f, "nothing charged for refused code");
    }

    [UnityTest]
    public IEnumerator ExecuteAsync_SyntaxError_ReportedBeforeLockedConstruct()
    {
        var results = new List<ExecutionResult>();
        yield return Run("for i = 1, 3 do", null, results);

        StringAssert.StartsWith("Compile error:", results[0].ErrorMessage);
    }

    [UnityTest]
    public IEnumerator ExecuteAsync_AuxWindowWithClassesLocked_Refused()
    {
        var results = new List<ExecutionResult>();
        var aux = new Dictionary<string, string> { { "Helper", "return 1" } };
        yield return Run("x = Helper", aux, results);

        StringAssert.Contains("class windows are not unlocked yet (window Helper)", results[0].ErrorMessage);
        Assert.AreEqual(StartEnergy, _energy.Current, 0.001f);
    }

    [UnityTest]
    public IEnumerator ExecuteAsync_OpenConstructsOnly_Succeeds()
    {
        var results = new List<ExecutionResult>();
        var aux = new Dictionary<string, string> { { "Notes", "-- only a comment" } };
        yield return Run("local x = 1\nif x == 1 then x = 2 else x = 3 end", aux, results);

        Assert.IsTrue(results[0].IsSuccess, results[0].ErrorMessage);
    }

    [UnityTest]
    public IEnumerator ExecuteAsync_ConstructUnlockedBetweenRuns_SecondRunPasses()
    {
        var results = new List<ExecutionResult>();
        const string code = "local s = 0\nfor i = 1, 3 do s = s + i end";

        yield return Run(code, null, results);
        Assert.IsFalse(results[0].IsSuccess);

        _unlocks.UnlockConstruct(LuaConstructs.For);
        yield return Run(code, null, results);

        Assert.IsTrue(results[1].IsSuccess, results[1].ErrorMessage);
    }

    [UnityTest]
    public IEnumerator ExecuteAsync_DangerousStandardLibraries_Removed()
    {
        var results = new List<ExecutionResult>();
        // load обходил бы проверку конструкций и учёт энергии; io/os.exit трогают файлы и процесс
        const string code =
            "if load ~= nil or loadfile ~= nil or dofile ~= nil or require ~= nil then error(\"loaders\") end\n" +
            "if io ~= nil or debug ~= nil or package ~= nil or collectgarbage ~= nil then error(\"libs\") end\n" +
            "if os.exit ~= nil or os.remove ~= nil or os.execute ~= nil or os.getenv ~= nil then error(\"os\") end\n" +
            "if os.clock == nil then error(\"os.clock\") end\n" +
            "if pcall == nil or string == nil or table == nil or math == nil then error(\"basics\") end";
        yield return Run(code, null, results);

        Assert.IsTrue(results[0].IsSuccess, results[0].ErrorMessage);
    }

    [Test]
    public void UnlockCommand_UnknownName_DeferredFalseWithoutEvent()
    {
        // Этап 2: неизвестная песочнице команда (награда будущего этапа) не отвергается, а сохраняется
        // отложенной — без OnUnlocked и без подключения в песочницу
        Assert.IsFalse(_unlocks.UnlockCommand("mien"));
        Assert.AreEqual(0, _appliedCount, "no event for an unknown command");
        Assert.IsTrue(_saves.Progress().IsCommandUnlocked("mien"), "deferred unlock is saved");
    }

    [UnityTest]
    public IEnumerator ExecuteAsync_PcallLockedStub_ReturnsFalseWithMessage()
    {
        var results = new List<ExecutionResult>();
        // Заглушка закрытой команды — ошибка, а не тихий успех; pcall её ловит как обычную ошибку
        const string code = "local ok, err = pcall(mine)\n" +
                            "if ok then error(\"stub returned\") end\n" +
                            "if not string.find(tostring(err), \"not unlocked\", 1, true) then error(tostring(err)) end";
        yield return Run(code, null, results);

        Assert.IsTrue(results[0].IsSuccess, results[0].ErrorMessage);
    }
}
