using System;
using System.Threading;
using System.Threading.Tasks;
using Lua;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Энергия за выполнение кода: цены конструкций из DefaultEnergyConfig, остановка при нехватке,
/// плата за вызов команды.
/// </summary>
public class EnergyTests
{
    private TestHelpers.LuaHost _lua;

    [SetUp]
    public void SetUp()
    {
        _lua = new TestHelpers.LuaHost(TestHelpers.ProjectEnergyConfig(), 1000f);
    }

    // ─── Цены конструкций (проектный конфиг) ──────────────────

    [Test]
    public void ForLoop_CostsOnePerIteration()
    {
        LuaValue result = _lua.Run("local s = 0 for i = 1, 100 do s = s + i end return s");

        Assert.AreEqual(5050, TestHelpers.Number(result));
        Assert.AreEqual(100f, _lua.Spent, 0.001f);
    }

    [Test]
    public void WhileLoop_CostsOnePerConditionCheck()
    {
        LuaValue result = _lua.Run("local i = 0 while i < 3 do i = i + 1 end return i");

        Assert.AreEqual(3, TestHelpers.Number(result));
        Assert.AreEqual(4f, _lua.Spent, 0.001f, "3 iterations + the final failing check");
    }

    [Test]
    public void RepeatUntil_CostsOnePerConditionCheck()
    {
        _lua.Run("local i = 0 repeat i = i + 1 until i >= 3");
        Assert.AreEqual(3f, _lua.Spent, 0.001f);
    }

    [Test]
    public void If_CostsHalfPerCheck_ElseIsFree()
    {
        LuaValue result = _lua.Run("local x = 2 if x == 1 then return 1 elseif x == 3 then return 3 else return 0 end");

        Assert.AreEqual(0, TestHelpers.Number(result));
        Assert.AreEqual(1f, _lua.Spent, 0.001f, "if 0.5 + elseif 0.5, else/local/return free");
    }

    [Test]
    public void FunctionsLocalsAndReturns_AreFree()
    {
        LuaValue result = _lua.Run("local function f(a) local b = a * 2 return b end return f(1) + f(2) + f(3)");

        Assert.AreEqual(12, TestHelpers.Number(result));
        Assert.AreEqual(0f, _lua.Spent, 0.001f);
    }

    [Test]
    public void KeywordsInStringsAndComments_AreNotCharged()
    {
        _lua.Run("local s = 'for while if' --[[ for i = 1, 100 do end ]] local t = [[while true do end]]");
        Assert.AreEqual(0f, _lua.Spent, 0.001f);
    }

    [Test]
    public void BlockComment_DoesNotHideCodeAfterIt()
    {
        // Раньше токенизатор считал --[[ ]] комментарием до конца строки, и код после него был бесплатным
        _lua.Run("--[[ comment ]] for i = 1, 10 do end");
        Assert.AreEqual(10f, _lua.Spent, 0.001f);
    }

    // ─── Нехватка энергии ─────────────────────────────────────

    [Test]
    public void InfiniteLoop_StopsWhenEnergyRunsOut()
    {
        var lua = new TestHelpers.LuaHost(TestHelpers.ProjectEnergyConfig(), 5f);

        Assert.Catch<Exception>(() => lua.Run("while true do end"));
        Assert.IsTrue(lua.Meter.IsExhausted);
        Assert.Less(lua.Energy.Current, 1f);
    }

    [Test]
    public void Pcall_CannotSwallowOutOfEnergy()
    {
        var lua = new TestHelpers.LuaHost(TestHelpers.ProjectEnergyConfig(), 5f);

        // pcall ловит первую ошибку, но счётчик остаётся «пустым» — следующий цикл снова падает
        Assert.Catch<Exception>(() =>
            lua.Run("local ok = pcall(function() while true do end end) for i = 1, 3 do end return ok"));
        Assert.IsTrue(lua.Meter.IsExhausted);
    }

    [Test]
    public void Meter_ResetAllowsNextRun()
    {
        var energy = new PlayerEnergy(10f);
        var meter = new EnergyMeter(energy);

        Assert.Throws<OutOfEnergyException>(() => meter.Charge(20f, "test"));
        Assert.IsTrue(meter.IsExhausted);

        meter.Reset();
        Assert.DoesNotThrow(() => meter.Charge(5f, "test"));
        Assert.AreEqual(5f, energy.Current, 0.001f);
    }

    // ─── Возврат энергии (PlayerEnergy.Refund) ────────────────

    [Test]
    public void Refund_AfterSpend_RestoresCurrentAndTotalSpent()
    {
        var energy = new PlayerEnergy(10f);
        Assert.IsTrue(energy.TrySpend(4f));
        Assert.IsTrue(energy.TrySpend(2f));

        energy.Refund(4f);

        Assert.AreEqual(8f, energy.Current, 0.001f);
        Assert.AreEqual(2f, energy.TotalSpent, 0.001f);
    }

    [Test]
    public void Refund_MoreThanSpent_ClampedToMaxAndZero()
    {
        var energy = new PlayerEnergy(10f);
        energy.TrySpend(3f);

        energy.Refund(50f);

        Assert.AreEqual(10f, energy.Current, 0.001f);
        Assert.AreEqual(0f, energy.TotalSpent, 0.001f);
    }

    [TestCase(0f)]
    [TestCase(-5f)]
    public void Refund_NonPositive_NoChange(float amount)
    {
        var energy = new PlayerEnergy(10f);
        energy.TrySpend(3f);

        energy.Refund(amount);

        Assert.AreEqual(7f, energy.Current, 0.001f);
        Assert.AreEqual(3f, energy.TotalSpent, 0.001f);
    }

    // ─── Плата за команды ─────────────────────────────────────

    private class PingCommand : LuaCommandBase
    {
        public override string Name => "ping";
        public override float EnergyCost => 2f;
        public override string Signature => "ping()";
        public override string Description => "test";

        protected override ValueTask<int> ExecuteAsync(LuaFunctionExecutionContext context, CancellationToken ct)
        {
            context.Return(true);
            return new ValueTask<int>(1);
        }
    }

    [Test]
    public void Command_ChargesEnergyCostOnEveryCall()
    {
        new PingCommand().Register(_lua.State, _lua.Meter);

        // 3 вызова × 2 + 3 итерации цикла × 1
        _lua.Run("for i = 1, 3 do ping() end");
        Assert.AreEqual(9f, _lua.Spent, 0.001f);
    }

    [Test]
    public void AllCommands_CostEnergy()
    {
        foreach (Type type in typeof(LuaCommandBase).Assembly.GetTypes())
        {
            if (type.IsAbstract || !typeof(LuaCommandBase).IsAssignableFrom(type)) continue;

            // Цена — константа в свойстве, конструктор с зависимостями не нужен
            var command = (LuaCommandBase)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(type);
            Assert.Greater(command.EnergyCost, 0f, $"command '{command.Name}' must not be free");
        }
    }
}
