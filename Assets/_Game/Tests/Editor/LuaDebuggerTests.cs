using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lua;
using NUnit.Framework;
using UnityEngine.TestTools;

/// <summary>
/// Точки останова и шаги: LuaDebugger останавливает код в точках, где он платит энергию.
/// </summary>
public class LuaDebuggerTests
{
    private const string Main = "Main";

    private TestHelpers.LuaHost _lua;
    private LuaDebugger _debugger;
    private HashSet<int> _breakpoints;

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
        _lua.Meter.LineTracker = new LuaLineTracker(Main);
        _debugger = new LuaDebugger();
        _lua.Meter.Debugger = _debugger;
        _breakpoints = new HashSet<int>();
        _debugger.BreakpointProvider = (chunk, line) => chunk == Main && _breakpoints.Contains(line);
        new ProbeCommand().Register(_lua.State, _lua.Meter);

        // Как в LuaSandbox: списание за конструкции — тоже точка проверки (строка цикла, условия)
        _lua.State.Environment[EnergyInstrumenter.ChargeFunction] = new LuaFunction(async (context, ct) =>
        {
            double amount = context.ArgumentCount > 0 && context.GetArgument(0).TryRead(out double n) ? n : 0d;
            await _lua.Meter.CheckpointAsync(context.State, ct);
            _lua.Meter.Charge((float)amount, "code");
            context.Return(true);
            return 1;
        });
    }

    private Task Start(string code, CancellationToken ct = default)
    {
        return _lua.State.DoStringAsync(_lua.Instrumenter.Instrument(code), Main, ct).AsTask();
    }

    [Test]
    public void Breakpoint_PausesUntilContinue()
    {
        _breakpoints.Add(2);

        Task run = Start("probe()\nprobe()\nprobe()");

        Assert.IsFalse(run.IsCompleted);
        Assert.IsTrue(_debugger.IsPaused);
        Assert.AreEqual(2, _debugger.PausedLine);
        // Вызов на строке 2 ещё не оплачен: на паузе энергия не тратится
        Assert.AreEqual(1f, _lua.Spent, 0.001f);

        _debugger.Continue();

        Assert.IsTrue(run.IsCompleted);
        Assert.IsFalse(_debugger.IsPaused);
        Assert.AreEqual(3f, _lua.Spent, 0.001f);
    }

    [Test]
    public void Step_PausesOnEveryNextLine()
    {
        _breakpoints.Add(1);

        Task run = Start("probe()\nprobe()\nprobe()");
        Assert.AreEqual(1, _debugger.PausedLine);

        _debugger.Step();
        Assert.AreEqual(2, _debugger.PausedLine);

        _debugger.Step();
        Assert.AreEqual(3, _debugger.PausedLine);

        _debugger.Continue();
        Assert.IsTrue(run.IsCompleted);
    }

    [Test]
    public void BreakpointInLoop_PausesOnEveryIteration()
    {
        _breakpoints.Add(2);
        int pauses = 0;
        _debugger.OnPausedChanged += (chunk, line) => { if (line > 0) pauses++; };

        Task run = Start("for i = 1, 3 do\n  probe()\nend");
        for (int i = 0; i < 3 && !run.IsCompleted; i++)
            _debugger.Continue();

        Assert.IsTrue(run.IsCompleted);
        Assert.AreEqual(3, pauses);
    }

    [Test]
    public void StepWhileNotPaused_IsIgnored()
    {
        _debugger.Step();

        Task run = Start("probe()\nprobe()");

        Assert.IsTrue(run.IsCompleted);
        Assert.IsFalse(_debugger.IsPaused);
    }

    [Test]
    public void NoBreakpoints_RunsWithoutPausing()
    {
        Task run = Start("probe()\nprobe()");

        Assert.IsTrue(run.IsCompleted);
        Assert.IsFalse(_debugger.IsPaused);
    }

    [UnityTest]
    public IEnumerator Cancel_WhilePaused_StopsRun()
    {
        _breakpoints.Add(1);
        var cts = new CancellationTokenSource();

        Task run = Start("probe()\nprobe()", cts.Token);
        Assert.IsTrue(_debugger.IsPaused);

        cts.Cancel();

        // Отмена ожидания проходит через UniTask и Lua-CSharp — запуск завершается в следующих кадрах
        for (int frame = 0; frame < 100 && !run.IsCompleted; frame++)
            yield return null;
        cts.Dispose();

        Assert.IsTrue(run.IsCompleted, $"status {run.Status}");
        Assert.IsTrue(run.IsCanceled || run.IsFaulted);
        Assert.IsFalse(_debugger.IsPaused);
        Assert.AreEqual(0f, _lua.Spent, 0.001f);
    }

    [Test]
    public void Reset_ClearsStepMode()
    {
        _breakpoints.Add(1);
        Task run = Start("probe()\nprobe()");
        _debugger.Step();
        Assert.AreEqual(2, _debugger.PausedLine);

        _debugger.Reset();

        Assert.IsTrue(run.IsCompleted);
        Assert.IsFalse(_debugger.ShouldPause(Main, 5));
    }
}
