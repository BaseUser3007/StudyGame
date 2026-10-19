using System.Collections.Generic;
using Lua;
using NUnit.Framework;

/// <summary>
/// Объяснение ошибок Lua для новичков: реальные тексты ошибок (Lua-CSharp 0.5.5, EnergyMeter,
/// LuaConstructGuard, LuaSandbox, команды) → место (окно и строка), объяснение и подсказка
/// ближайшего имени.
/// </summary>
public class LuaErrorExplainerTests
{
    // ─── Фейковые команды ─────────────────────────────────────

    private sealed class FakeCommand : ILuaCommand
    {
        public FakeCommand(string name, string signature)
        {
            Name = name;
            Signature = signature;
        }

        public string Name { get; }
        public float EnergyCost => 1f;
        public string Signature { get; }
        public string Description => string.Empty;
        public bool IsReadyMade => false;
        public void Register(LuaState state, EnergyMeter meter) { }
    }

    private static readonly ILuaCommand[] Commands =
    {
        new FakeCommand("move", "move(direction, steps)"),
        new FakeCommand("moveTo", "moveTo(x, y)"),
        new FakeCommand("harvest", "harvest([x, y] | obj)"),
        new FakeCommand("findObjects", "findObjects([type])"),
        new FakeCommand("check", "check(x, y | obj | direction, [distance])")
    };

    private static LuaErrorExplanation Explain(string raw, params string[] windows) =>
        LuaErrorExplainer.Explain(raw, Commands, windows, "Main");

    // ─── Место ошибки ─────────────────────────────────────────

    [Test]
    public void RuntimeError_ParsesWindowAndLine()
    {
        var e = Explain("Runtime error: Lua-CSharp: [string \"Main\"]:5: attempt to index a nil value (local 'tree')");

        Assert.AreEqual("Main", e.Window);
        Assert.AreEqual(5, e.Line);
        Assert.AreEqual("Line 5 in Main", e.Location);
        StringAssert.Contains("`tree` is nil", e.Explanation);
        StringAssert.Contains("getNear()", e.Explanation);
        StringAssert.Contains("if tree then", e.Suggestion);
    }

    [Test]
    public void IndexNil_IfLocked_DoesNotSuggestIf()
    {
        var e = LuaErrorExplainer.Explain(
            "Runtime error: Lua-CSharp: [string \"Main\"]:5: attempt to index a nil value (local 'tree')",
            Commands, null, "Main", construct => construct != LuaConstructs.If);

        StringAssert.DoesNotContain("if tree then", e.Suggestion);
        StringAssert.Contains("findObjects", e.Suggestion);
    }

    [Test]
    public void DidYouMean_DoesNotOfferPrint_WhenItIsNotACommand()
    {
        // print — открываемая команда игры: «Did you mean» предлагает её только из реестра
        var e = Explain("Runtime error: Lua-CSharp: [string \"Main\"]:1: attempt to call a nil value (global 'prnt')");

        Assert.IsTrue(e.Suggestion == null || !e.Suggestion.Contains("`print`"));
    }

    [Test]
    public void AuxiliaryWindow_LineSkipsFunctionWrapper()
    {
        // Доп. окно выполняется как "function Helper(...)\n<код>\nend": строка 3 чанка — строка 2 окна
        var e = Explain("Runtime error: Lua-CSharp: [string \"Helper\"]:3: attempt to index a nil value (local 'z')", "Helper");

        Assert.AreEqual("Helper", e.Window);
        Assert.AreEqual(2, e.Line);
    }

    [Test]
    public void ClampLine_LimitsToWindowLength()
    {
        var e = Explain("Compile error: [string \"Helper\"]:5: 'end' expected (to close 'function' at line 1) near <eof>", "Helper");
        e.ClampLine(3);

        Assert.AreEqual(3, e.Line);
        // Строка 1 чанка — обёртка окна: про неё не говорим
        StringAssert.DoesNotContain("on line", e.Explanation);
    }

    [Test]
    public void ConstructGuard_TakesWindowAndLineFromMessage()
    {
        var e = Explain("Runtime error: loop `for` is not unlocked yet (Helper, line 3)", "Helper");

        Assert.AreEqual("Helper", e.Window);
        Assert.AreEqual(3, e.Line);
        StringAssert.Contains("loop `for`", e.Explanation);
        StringAssert.Contains("achievements", e.Suggestion);
    }

    // ─── Опечатки в именах ────────────────────────────────────

    [Test]
    public void CallNil_SuggestsClosestCommand()
    {
        var e = Explain("Runtime error: Lua-CSharp: [string \"Main\"]:2: attempt to call a nil value (global 'mvoe')");

        StringAssert.Contains("`mvoe`", e.Explanation);
        StringAssert.Contains("Did you mean `move`?", e.Suggestion);
        StringAssert.Contains("move(direction, steps)", e.Suggestion);
    }

    [Test]
    public void CallNil_WrongCase_MentionsCaseSensitivity()
    {
        var e = Explain("Runtime error: Lua-CSharp: [string \"Main\"]:1: attempt to call a nil value (global 'moveto')");

        StringAssert.Contains("Did you mean `moveTo`?", e.Suggestion);
        StringAssert.Contains("case-sensitive", e.Suggestion);
    }

    [Test]
    public void CallNil_Method_SuggestsListMethod()
    {
        var e = Explain("Runtime error: Lua-CSharp: [string \"Main\"]:2: attempt to call a nil value (method 'getNar')");

        StringAssert.Contains("Did you mean `getNear`?", e.Suggestion);
    }

    [Test]
    public void CallNil_WindowFunction_Suggested()
    {
        var e = Explain("Runtime error: Lua-CSharp: [string \"Main\"]:1: attempt to call a nil value (global 'Gather')", "Gatherer");

        StringAssert.Contains("Did you mean `Gatherer`?", e.Suggestion);
    }

    [Test]
    public void CallNil_NothingClose_GivesGeneralAdvice()
    {
        var e = Explain("Runtime error: Lua-CSharp: [string \"Main\"]:1: attempt to call a nil value (global 'xyzzyplugh')");

        Assert.IsTrue(e.HasExplanation);
        StringAssert.DoesNotContain("Did you mean", e.Suggestion);
        StringAssert.Contains("Help", e.Suggestion);
    }

    [Test]
    public void UnknownType_SuggestsClosestType()
    {
        var e = Explain("Runtime error: findObjects: unknown type 'tre' (known: tree, stone, berry_bush, enemy, goblin, obelisk)");

        StringAssert.Contains("\"tre\"", e.Explanation);
        StringAssert.Contains("Did you mean \"tree\"?", e.Suggestion);
    }

    [Test]
    public void FindClosest_UsesTranspositionAndLimit()
    {
        Assert.AreEqual("harvest", LuaErrorExplainer.FindClosest("havrest", new[] { "move", "harvest" }));
        Assert.IsNull(LuaErrorExplainer.FindClosest("abc", new[] { "harvest" }));
        Assert.AreEqual(1, LuaErrorExplainer.Distance("mvoe", "move"));
        Assert.AreEqual(3, LuaErrorExplainer.Distance("kitten", "sitting"));
    }

    // ─── Синтаксис ────────────────────────────────────────────

    [TestCase("Compile error: [string \"Main\"]:1: unexpected symbol near '1'", "didn't expect")]
    [TestCase("Compile error: [string \"Main\"]:1: unexpected symbol near '!'", "`~=`")]
    [TestCase("Compile error: [string \"Main\"]:1: unfinished string near '\"abc)'", "closing quote")]
    [TestCase("Compile error: [string \"Main\"]:1: '=' or 'in' expected near '1'", "`for` loop")]
    [TestCase("Compile error: [string \"Main\"]:1: syntax error near 'y'", "couldn't understand")]
    [TestCase("Compile error: [string \"Main\"]:1: ) expected near 'b'", "bracket")]
    [TestCase("Compile error: [string \"Main\"]:1: unfinished long comment near <eof>", "comment")]
    public void SyntaxErrors_AreExplained(string raw, string expected)
    {
        var e = Explain(raw);

        Assert.IsTrue(e.HasExplanation, raw);
        StringAssert.Contains(expected, e.Explanation + " " + e.Suggestion);
    }

    [Test]
    public void EndExpected_NamesOpeningLine()
    {
        var e = Explain("Compile error: [string \"Main\"]:3: 'end' expected (to close 'if' at line 1) near <eof>");

        Assert.AreEqual(3, e.Line);
        StringAssert.Contains("The `if` on line 1 has no `end`", e.Explanation);
    }

    [Test]
    public void KeywordTokenCode_IsDecoded()
    {
        // Lua-CSharp печатает ожидаемое ключевое слово кодом токена: 'then' = (char)274, 'do' = (char)258
        var then = Explain("Compile error: [string \"Main\"]:1: " + (char)274 + " expected near '='");
        var doWord = Explain("Compile error: [string \"Main\"]:2: " + (char)258 + " expected near 'print'");
        var eof = Explain("Compile error: [string \"Main\"]:2: " + (char)285 + " expected near 'end'");

        StringAssert.Contains("`then`", then.Explanation);
        StringAssert.Contains("`==`", then.Suggestion);
        StringAssert.Contains("`do`", doWord.Explanation);
        StringAssert.Contains("extra `end`", eof.Explanation);
    }

    // ─── Выполнение ───────────────────────────────────────────

    [TestCase("attempt to add a nil value (local 'n') with a number value", "`n`")]
    [TestCase("attempt to mul a number value with a nil value (local 'count')", "`count`")]
    [TestCase("attempt to perform arithmetic on a nil value", "nil")]
    public void Arithmetic_OnNil_NamesTheVariable(string message, string expected)
    {
        var e = Explain("Runtime error: Lua-CSharp: [string \"Main\"]:2: " + message);

        StringAssert.Contains("math", e.Explanation);
        StringAssert.Contains(expected, e.Explanation);
    }

    [Test]
    public void Arithmetic_OnString_SuggestsTonumber()
    {
        var e = Explain("Runtime error: Lua-CSharp: [string \"Main\"]:1: attempt to add a string value with a number value");

        StringAssert.Contains("tonumber", e.Suggestion);
    }

    [Test]
    public void CompareNilWithNumber_IsExplained()
    {
        var e = Explain("Runtime error: Lua-CSharp: [string \"Main\"]:2: attempt to compare a number value with a nil value");

        StringAssert.Contains("is nil", e.Explanation);
    }

    [Test]
    public void BadArgument_ShowsSignatureFromRegistry()
    {
        var missing = Explain("Runtime error: Lua-CSharp: [string \"Main\"]:1: bad argument #2 to 'move' (value expected)");
        var wrong = Explain("Runtime error: Lua-CSharp: [string \"Main\"]:1: bad argument #1 to 'move' (string expected, got number))");

        StringAssert.Contains("argument 2 is missing", missing.Explanation);
        StringAssert.Contains("move(direction, steps)", missing.Suggestion);
        StringAssert.Contains("string expected, got number", wrong.Explanation);
        StringAssert.DoesNotContain("))", wrong.Explanation);
    }

    [Test]
    public void CommandUsage_IsExplained()
    {
        var usage = Explain("Runtime error: moveTo: usage moveTo(x, y) or moveTo(obj)");
        var detail = Explain("Runtime error: check: distance must be a whole number >= 0");

        StringAssert.Contains("`moveTo`", usage.Explanation);
        StringAssert.Contains("moveTo(x, y) or moveTo(obj)", usage.Suggestion);
        StringAssert.Contains("`check` couldn't run", detail.Explanation);
    }

    [Test]
    public void OutOfEnergy_InfiniteLoop_StackOverflow_LockedCommand()
    {
        var energy = Explain("Runtime error: out of energy at moveTo(): need 2, have 1.5");
        var freeze = Explain("Runtime error: script blocked the game for more than 3s and was stopped (infinite loop?)");
        var stack = Explain("Runtime error: stack overflow");
        var locked = Explain("Runtime error: command `obtain` is not unlocked yet");

        StringAssert.Contains("moveTo()", energy.Explanation);
        StringAssert.Contains("End Day", energy.Suggestion);
        StringAssert.Contains("loop that never ends", freeze.Explanation);
        StringAssert.Contains("calling itself", stack.Explanation);
        StringAssert.Contains("`obtain`", locked.Explanation);
        StringAssert.Contains("Achievements", locked.Suggestion);
        Assert.IsNull(stack.Location);
    }

    [Test]
    public void LockedClasses_PointsAtWindow()
    {
        var e = Explain("Runtime error: class windows are not unlocked yet (window Helper): move the code into Main or clear the window", "Helper");

        Assert.AreEqual("Helper", e.Window);
        Assert.AreEqual("In window Helper", e.Location);
        Assert.IsTrue(e.HasExplanation);
    }

    [Test]
    public void UnknownMessage_KeepsRawAndLocationWithoutExplanation()
    {
        var e = Explain("Runtime error: Lua-CSharp: [string \"Main\"]:1: boom");

        Assert.IsFalse(e.HasExplanation);
        Assert.AreEqual(1, e.Line);
        StringAssert.Contains("boom", e.Raw);
    }

    [Test]
    public void RawOnly_HasNoExplanation()
    {
        var e = LuaErrorExplanation.RawOnly("Runtime error: stack overflow");

        Assert.IsFalse(e.HasExplanation);
        Assert.IsNull(e.Location);
        Assert.AreEqual("Runtime error: stack overflow", e.Raw);
    }
}
