using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lua;
using NUnit.Framework;

/// <summary>
/// Подсветка выполняемой строки: LuaLineTracker определяет окно и строку вызова команды.
/// </summary>
public class LuaLineTrackerTests
{
    private const string Main = "Main";

    private TestHelpers.LuaHost _lua;
    private LuaLineTracker _tracker;
    private List<string> _events;

    private sealed class ProbeCommand : LuaCommandBase
    {
        public override string Name => "probe";
        public override float EnergyCost => 1f;
        public override string Signature => "probe()";
        public override string Description => "test";

        protected override ValueTask<int> ExecuteAsync(LuaFunctionExecutionContext context, CancellationToken ct)
        {
            return new ValueTask<int>(0);
        }
    }

    [SetUp]
    public void SetUp()
    {
        _lua = new TestHelpers.LuaHost(TestHelpers.ProjectEnergyConfig(), 1000f);
        _tracker = new LuaLineTracker(Main);
        _lua.Meter.LineTracker = _tracker;
        _events = new List<string>();
        _tracker.OnLineChanged += (chunk, line) => _events.Add($"{chunk}:{line}");
        new ProbeCommand().Register(_lua.State, _lua.Meter);
    }

    private void Run(string code, string chunk)
    {
        _lua.State.DoStringAsync(code, chunk).AsTask().GetAwaiter().GetResult();
    }

    [Test]
    public void CommandCall_ReportsItsLine()
    {
        Run("local a = 1\nprobe()\n\nprobe()\n", Main);

        CollectionAssert.AreEqual(new[] { "Main:2", "Main:4" }, _events);
    }

    [Test]
    public void SameLineTwice_ReportedOnce()
    {
        Run("probe() probe()\nprobe()", Main);

        CollectionAssert.AreEqual(new[] { "Main:1", "Main:2" }, _events);
    }

    [Test]
    public void LoopBody_ReportsLineInsideLoop()
    {
        Run("for i = 1, 3 do\n  probe()\nend\nprobe()", Main);

        CollectionAssert.AreEqual(new[] { "Main:2", "Main:4" }, _events);
    }

    [Test]
    public void AuxiliaryWindow_LineShiftedByWrapper()
    {
        // Как в LuaSandbox: код окна обёрнут в `function Title(...)` отдельной первой строкой
        Run("function Helper(...)\nlocal x = 1\nprobe()\nend", "Helper");
        Run("Helper()\nprobe()", Main);

        CollectionAssert.AreEqual(new[] { "Helper:2", "Main:2" }, _events);
    }

    [Test]
    public void CallThroughPcall_ReportsPlayerLine()
    {
        // Между кодом игрока и командой — C#-функция pcall
        Run("local a = 1\npcall(probe)", Main);

        CollectionAssert.AreEqual(new[] { "Main:2" }, _events);
    }

    [Test]
    public void Clear_ReportsEndOfRun()
    {
        Run("probe()", Main);
        _tracker.Clear();

        CollectionAssert.AreEqual(new[] { "Main:1", ":0" }, _events);
        Assert.IsNull(_tracker.Chunk);
    }

    [Test]
    public void InstrumentedLoop_TracksEnergyCharges()
    {
        // __energy в LuaHost не вызывает Track — проверяем, что команда внутри размеченного кода
        // по-прежнему даёт строку игрока (инструментатор не добавляет переводов строк)
        _lua.State.DoStringAsync(_lua.Instrumenter.Instrument("local n = 0\nwhile n < 2 do\n  n = n + 1\n  probe()\nend"), Main)
            .AsTask().GetAwaiter().GetResult();

        CollectionAssert.AreEqual(new[] { "Main:4" }, _events);
    }
}
