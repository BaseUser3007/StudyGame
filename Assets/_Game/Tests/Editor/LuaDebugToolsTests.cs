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
/// Отладка и замер (GameDesign §6.2.6–6.2.7): Tag события RunFinished (ok/error/energy/stopped/locked),
/// команда print (цена 0,5, форматирование, закрыта до first_bug, вывод через OnLogMessage),
/// getStatus().spent и достижение first_bug от ошибки в коде до открытия print.
/// Песочница без сцены, как в LuaSandboxUnlockTests; GameEventBus сбрасывается до и после каждого теста.
/// </summary>
public class LuaDebugToolsTests
{
    private const float StartEnergy = 100f;

    private TestHelpers.TempSaveFolder _saves;
    private readonly List<Object> _created = new List<Object>();
    private UnlockConfig _config;
    private MetaProgress _progress;
    private UnlockRegistry _unlocks;
    private PlayerEnergy _energy;
    private GameObject _host;
    private LuaSandbox _sandbox;
    private AchievementTracker _tracker;

    private readonly List<string> _runTags = new List<string>();
    private readonly List<string> _runIds = new List<string>();
    private readonly List<(string Text, LogEntryType Type)> _log = new List<(string, LogEntryType)>();

    [SetUp]
    public void SetUp()
    {
        ResetBus();
        _saves = new TestHelpers.TempSaveFolder();
        _runTags.Clear();
        _runIds.Clear();
        _log.Clear();
        GameEventBus.OnGameEvent += RecordEvent;
    }

    [TearDown]
    public void TearDown()
    {
        _tracker?.Dispose();
        _tracker = null;
        if (_sandbox != null) _sandbox.OnLogMessage -= RecordLog;
        if (_host != null) Object.DestroyImmediate(_host);
        foreach (Object obj in _created)
            if (obj != null) Object.DestroyImmediate(obj);
        _created.Clear();
        ResetBus();
        _saves.Dispose();
    }

    // ─── Заготовки ────────────────────────────────────────────

    private static void ResetBus()
    {
        MethodInfo reset = typeof(GameEventBus).GetMethod("ResetStatics", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(reset, "GameEventBus.ResetStatics not found");
        reset.Invoke(null, null);
    }

    private static void SetPrivate(object target, string field, object value)
    {
        FieldInfo info = target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(info, $"{target.GetType().Name}.{field} not found");
        info.SetValue(target, value);
    }

    /// <summary>Песочница с открытыми командами и конструкциями (и, по желанию, каталогом достижений).</summary>
    private void CreateSandbox(
        IEnumerable<string> commands, float energy = StartEnergy, AchievementCatalog catalog = null,
        params string[] constructs)
    {
        _config = ScriptableObject.CreateInstance<UnlockConfig>();
        _created.Add(_config);
        _config.unlockAll = false;
        _config.startingCommands = new List<string>(commands);
        _config.startingConstructs = new List<string>(constructs);
        _progress = _saves.Progress();
        _unlocks = new UnlockRegistry(_config, _progress);

        _energy = new PlayerEnergy(energy);
        _host = new GameObject("LuaDebugToolsTest");
        _sandbox = _host.AddComponent<LuaSandbox>();
        _sandbox.Initialize(null, null, null, TestHelpers.ProjectEnergyConfig(), null, _energy, null, null,
            null, null, 1f, null, _unlocks, null, null);
        _sandbox.OnLogMessage += RecordLog;

        if (catalog != null)
            _tracker = new AchievementTracker(catalog, _progress, _unlocks, _energy);
    }

    private AchievementDefinition FirstBug()
    {
        AchievementDefinition definition = ScriptableObject.CreateInstance<AchievementDefinition>();
        _created.Add(definition);
        definition.name = "first_bug";
        SetPrivate(definition, "id", "first_bug");
        SetPrivate(definition, "title", "Bug Report");
        SetPrivate(definition, "condition", new AchievementCondition
        {
            type = AchievementConditionType.Counter, gameEvent = GameEventType.RunFinished,
            filter = GameEventBus.RunTagError, amount = 1
        });
        SetPrivate(definition, "rewards", new List<AchievementReward>
        {
            new AchievementReward { kind = AchievementRewardKind.Command, id = "print" }
        });
        return definition;
    }

    private AchievementCatalog Catalog(params AchievementDefinition[] definitions)
    {
        AchievementCatalog catalog = ScriptableObject.CreateInstance<AchievementCatalog>();
        _created.Add(catalog);
        SetPrivate(catalog, "achievements", new List<AchievementDefinition>(definitions));
        return catalog;
    }

    private void RecordEvent(GameEvent e)
    {
        if (e.Type != GameEventType.RunFinished) return;
        _runIds.Add(e.Id);
        _runTags.Add(e.Tag);
    }

    private void RecordLog(string text, LogEntryType type) => _log.Add((text, type));

    private IEnumerator Run(string main, List<ExecutionResult> into, Dictionary<string, string> aux = null)
    {
        Task<ExecutionResult> task = _sandbox.ExecuteAsync(main, aux ?? new Dictionary<string, string>()).AsTask();
        for (int frame = 0; frame < 600 && !task.IsCompleted; frame++)
            yield return null;

        Assert.IsTrue(task.IsCompleted, "execution did not finish");
        into.Add(task.Result);
    }

    private static string Format(params LuaValue[] values) => PrintCommand.Format(values);

    // ─── Tag у RunFinished ────────────────────────────────────

    [UnityTest]
    public IEnumerator RunFinished_Success_TagOk()
    {
        CreateSandbox(new[] { "getPosition" }, StartEnergy, null, LuaConstructs.Local);
        var results = new List<ExecutionResult>();
        yield return Run("local x = 1", results);

        Assert.IsTrue(results[0].IsSuccess, results[0].ErrorMessage);
        CollectionAssert.AreEqual(new[] { GameEventBus.RunSucceededId }, _runIds, "Id is unchanged");
        CollectionAssert.AreEqual(new[] { GameEventBus.RunTagOk }, _runTags);
    }

    [UnityTest]
    public IEnumerator RunFinished_RuntimeError_TagError()
    {
        CreateSandbox(new[] { "getPosition" });
        var results = new List<ExecutionResult>();
        yield return Run("error(\"boom\")", results);

        Assert.IsFalse(results[0].IsSuccess);
        CollectionAssert.AreEqual(new[] { GameEventBus.RunFailedId }, _runIds);
        CollectionAssert.AreEqual(new[] { GameEventBus.RunTagError }, _runTags);
    }

    [UnityTest]
    public IEnumerator RunFinished_CompileError_TagError()
    {
        CreateSandbox(new[] { "getPosition" });
        var results = new List<ExecutionResult>();
        yield return Run("x = ", results);

        StringAssert.StartsWith("Compile error:", results[0].ErrorMessage);
        CollectionAssert.AreEqual(new[] { GameEventBus.RunTagError }, _runTags);
    }

    [UnityTest]
    public IEnumerator RunFinished_OutOfEnergy_TagEnergy()
    {
        CreateSandbox(new[] { "getPosition" }, 5f, null, LuaConstructs.While);
        var results = new List<ExecutionResult>();
        yield return Run("while true do end", results);

        StringAssert.Contains("out of energy", results[0].ErrorMessage);
        CollectionAssert.AreEqual(new[] { GameEventBus.RunTagEnergy }, _runTags);
    }

    [UnityTest]
    public IEnumerator RunFinished_OutOfEnergyCaughtByPcall_StillTagEnergy()
    {
        CreateSandbox(new[] { "getPosition" }, 5f, null, LuaConstructs.While, LuaConstructs.Function);
        var results = new List<ExecutionResult>();
        // pcall ловит ошибку, но счётчик остаётся пустым: следующая проверка while снова её бросает
        yield return Run("pcall(function() while true do end end)\nwhile true do end", results);

        CollectionAssert.AreEqual(new[] { GameEventBus.RunTagEnergy }, _runTags);
    }

    [UnityTest]
    public IEnumerator RunFinished_LockedConstruct_TagLocked()
    {
        CreateSandbox(new[] { "getPosition" });
        var results = new List<ExecutionResult>();
        yield return Run("for i = 1, 2 do end", results);

        StringAssert.Contains("is not unlocked yet", results[0].ErrorMessage);
        CollectionAssert.AreEqual(new[] { GameEventBus.RunTagLocked }, _runTags);
    }

    [UnityTest]
    public IEnumerator RunFinished_LockedCommand_TagLocked()
    {
        CreateSandbox(new[] { "getPosition" });
        var results = new List<ExecutionResult>();
        yield return Run("move(\"right\")", results);

        StringAssert.Contains("command `move` is not unlocked yet", results[0].ErrorMessage);
        CollectionAssert.AreEqual(new[] { GameEventBus.RunTagLocked }, _runTags);
    }

    [UnityTest]
    public IEnumerator RunFinished_LockedCommandCaughtThenOtherError_TagError()
    {
        CreateSandbox(new[] { "getPosition" });
        var results = new List<ExecutionResult>();
        // Заглушку поймал pcall — до верха дошла другая ошибка, это ошибка кода
        yield return Run("pcall(move)\nerror(\"mine\")", results);

        CollectionAssert.AreEqual(new[] { GameEventBus.RunTagError }, _runTags);
    }

    [UnityTest]
    public IEnumerator RunFinished_Watchdog_TagStopped()
    {
        CreateSandbox(new[] { "getPosition" }, 1e8f, null, LuaConstructs.While);
        SetPrivate(_sandbox, "freezeTimeoutSeconds", 0.3f);
        var results = new List<ExecutionResult>();
        // Бесконечный цикл без await блокирует главный поток — watchdog отменяет запуск
        yield return Run("while true do end", results);

        StringAssert.Contains("infinite loop", results[0].ErrorMessage);
        CollectionAssert.AreEqual(new[] { GameEventBus.RunTagStopped }, _runTags);
    }

    // ─── first_bug: фильтр сравнивается с Tag ─────────────────

    [Test]
    public void FirstBug_CountsOnlyErrorTag()
    {
        CreateSandbox(new[] { "getPosition" }, StartEnergy, Catalog(FirstBug()));
        AchievementDefinition firstBug = _tracker.Definitions[0];

        foreach (string tag in new[]
                 {
                     GameEventBus.RunTagOk, GameEventBus.RunTagEnergy, GameEventBus.RunTagStopped,
                     GameEventBus.RunTagLocked, GameEventBus.RunTagPuzzle
                 })
        {
            GameEventBus.PublishRunStarted();
            GameEventBus.PublishRunFinished(new CodeRunReport(tag == GameEventBus.RunTagOk, true, 0f, null, tag));
        }
        Assert.IsFalse(_tracker.IsCompleted(firstBug), "only an error counts");

        GameEventBus.PublishRunStarted();
        GameEventBus.PublishRunFinished(new CodeRunReport(false, true, 0f, null, GameEventBus.RunTagError));
        Assert.IsTrue(_tracker.IsCompleted(firstBug));
    }

    [Test]
    public void CodeRunReport_DefaultOutcome_FromSucceeded()
    {
        Assert.AreEqual(GameEventBus.RunTagOk, new CodeRunReport(true, true, 0f, null).Outcome);
        Assert.AreEqual(GameEventBus.RunTagError, new CodeRunReport(false, true, 0f, null).Outcome);
    }

    [UnityTest]
    public IEnumerator FirstBug_ErrorInCode_UnlocksPrintAfterRun()
    {
        CreateSandbox(new[] { "getPosition" }, StartEnergy, Catalog(FirstBug()));
        var results = new List<ExecutionResult>();

        yield return Run("error(\"oops\")", results);
        Assert.IsTrue(_tracker.IsCompleted(_tracker.Definitions[0]));
        Assert.IsTrue(_sandbox.Registry.HasCommand("print"), "print is connected after the run");

        yield return Run("print(\"works\")", results);
        Assert.IsTrue(results[1].IsSuccess, results[1].ErrorMessage);
        Assert.AreEqual("works", _log[_log.Count - 1].Text);
    }

    // ─── print: закрыт / открыт ───────────────────────────────

    [UnityTest]
    public IEnumerator Print_Locked_StubErrorNoEnergyNoOutput()
    {
        CreateSandbox(new[] { "getPosition" });
        var results = new List<ExecutionResult>();
        yield return Run("print(\"hidden\")", results);

        StringAssert.Contains("command `print` is not unlocked yet", results[0].ErrorMessage);
        Assert.AreEqual(StartEnergy, _energy.Current, 0.001f);
        Assert.IsEmpty(_log);
        Assert.IsFalse(_sandbox.Registry.HasCommand("print"));
    }

    [UnityTest]
    public IEnumerator Print_Unlocked_CostsHalfAndPublishesInfo()
    {
        CreateSandbox(new[] { "print" });
        var results = new List<ExecutionResult>();
        yield return Run("print(\"a\", 1, nil, true, 2.5)", results);

        Assert.IsTrue(results[0].IsSuccess, results[0].ErrorMessage);
        Assert.AreEqual(StartEnergy - 0.5f, _energy.Current, 0.001f);
        Assert.AreEqual(1, _log.Count);
        Assert.AreEqual("a\t1\tnil\ttrue\t2.5", _log[0].Text);
        Assert.AreEqual(LogEntryType.Info, _log[0].Type);
    }

    [Test]
    public void Print_CostIsFractionalAndPositive()
    {
        var print = new PrintCommand(null);
        Assert.AreEqual(0.5f, print.EnergyCost, 0.0001f);
        Assert.Greater(print.EnergyCost, 0f);
        Assert.IsFalse(print.IsReadyMade);
    }

    [Test]
    public void Print_EnergyCounter_OnlyWhenUnlocked()
    {
        CreateSandbox(new[] { "getPosition" });
        var code = new Dictionary<string, string> { { LuaSandbox.MainChunkName, "print(1)" } };
        Assert.AreEqual(0f, _sandbox.CalculateEnergyCost(code), 0.0001f, "locked print is not a command");

        _unlocks.UnlockCommand("print");
        Assert.AreEqual(0.5f, _sandbox.CalculateEnergyCost(code), 0.0001f);
    }

    [Test]
    public void Print_NotAStandardCompletion()
    {
        // Пока закрыт — не предлагается; открытый приходит из реестра (как команда)
        var provider = new LuaCompletionProvider(() => new List<ILuaCommand>(), () => new List<string>());
        foreach (var item in provider.GetCompletions("pri", 3, true))
            Assert.AreNotEqual("print", item.Label);
    }

    // ─── print: форматирование ────────────────────────────────

    [Test]
    public void Format_Scalars()
    {
        Assert.AreEqual("", Format());
        Assert.AreEqual("nil", Format(LuaValue.Nil));
        Assert.AreEqual("3\t-0.25\t1e+20", Format(3d, -0.25d, 1e20d));
        Assert.AreEqual("text\tfalse", Format("text", false));
    }

    [Test]
    public void Format_TableOneLevel()
    {
        var single = new LuaTable();
        single["a"] = 1d;
        Assert.AreEqual("{a=1}", Format(single));

        var array = new LuaTable();
        array[1] = 1d;
        array[2] = "two";
        array[3] = true;
        Assert.AreEqual("{1, \"two\", true}", Format(array));

        // Вложенная таблица не раскрывается
        var nested = new LuaTable();
        nested["inner"] = array;
        Assert.AreEqual("{inner={...}}", Format(nested));

        var several = new LuaTable();
        several["x"] = 3.4d;
        several["type"] = "stick";
        string text = Format(several);
        StringAssert.Contains("x=3.4", text);
        StringAssert.Contains("type=\"stick\"", text);
    }

    [Test]
    public void Format_ObjectList_CountAndFirstThree()
    {
        var list = new LuaTable();
        for (int i = 1; i <= 5; i++)
        {
            var item = new LuaTable();
            item["id"] = (double)i;
            list[i] = item;
        }
        list["count"] = 5d;
        list["getNear"] = new LuaFunction((context, ct) => new System.Threading.Tasks.ValueTask<int>(0));

        Assert.AreEqual("list(count=5) {{id=1}, {id=2}, {id=3}, ...}", Format(list));

        LuaTable empty = new LuaObjectList(null).Build(new List<ResourceObject>(), null);
        Assert.AreEqual("list(count=0) {}", Format(empty));
    }

    [Test]
    public void Format_LongOutput_Truncated()
    {
        string text = Format(new string('a', 1000));
        Assert.AreEqual(PrintCommand.MaxLength, text.Length);
        StringAssert.EndsWith("…", text);
    }

    // ─── getStatus().spent ────────────────────────────────────

    [UnityTest]
    public IEnumerator GetStatusSpent_CountsFromRunStart()
    {
        CreateSandbox(new[] { "getStatus", "print" });
        var results = new List<ExecutionResult>();

        yield return Run("print(getStatus().spent)\nprint(getStatus().spent)", results);
        Assert.IsTrue(results[0].IsSuccess, results[0].ErrorMessage);
        // getStatus (1) списан до чтения; затем print (0,5) и второй getStatus (1)
        Assert.AreEqual("1", _log[0].Text);
        Assert.AreEqual("2.5", _log[1].Text);

        // Второй запуск считает заново
        yield return Run("print(getStatus().spent)", results);
        Assert.AreEqual("1", _log[2].Text);
        Assert.AreEqual(0f, _sandbox.EnergySpentThisRun, "nothing is running");
    }
}
