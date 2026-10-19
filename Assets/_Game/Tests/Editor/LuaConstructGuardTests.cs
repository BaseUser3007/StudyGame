using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Проверка закрытых конструкций языка перед запуском: какие слова закрываются, что игнорируется
/// (строки, комментарии, длинные скобки), сообщения с окном и номером строки, окна-«классы».
/// </summary>
public class LuaConstructGuardTests
{
    private TestHelpers.TempSaveFolder _saves;
    private UnlockConfig _config;

    [SetUp]
    public void SetUp()
    {
        _saves = new TestHelpers.TempSaveFolder();
    }

    [TearDown]
    public void TearDown()
    {
        if (_config != null) Object.DestroyImmediate(_config);
        _saves.Dispose();
    }

    /// <summary>Защита, у которой открыты только перечисленные конструкции.</summary>
    private LuaConstructGuard Guard(params string[] openConstructs)
    {
        if (_config != null) Object.DestroyImmediate(_config);
        _config = ScriptableObject.CreateInstance<UnlockConfig>();
        _config.startingCommands = new List<string>();
        _config.startingConstructs = new List<string>(openConstructs);
        return new LuaConstructGuard(new UnlockRegistry(_config, _saves.Progress()));
    }

    private static readonly string[] AllButClasses =
    {
        LuaConstructs.Local, LuaConstructs.If, LuaConstructs.For, LuaConstructs.While,
        LuaConstructs.Repeat, LuaConstructs.Function
    };

    // ─── Закрытые конструкции ─────────────────────────────────

    [Test]
    public void CheckChunk_LockedFor_ReportsConstructAndLine()
    {
        LuaConstructGuard guard = Guard(LuaConstructs.Local, LuaConstructs.If);

        string error = guard.CheckChunk("local s = 0\n\nfor i = 1, 3 do s = s + i end", "Main");

        Assert.AreEqual("loop `for` is not unlocked yet (Main, line 3)", error);
    }

    [Test]
    public void CheckChunk_LockedWhile_ReportsWhile()
    {
        string error = Guard(LuaConstructs.Local).CheckChunk("local i = 0\nwhile i < 3 do i = i + 1 end", "Main");
        Assert.AreEqual("loop `while` is not unlocked yet (Main, line 2)", error);
    }

    [Test]
    public void CheckChunk_LockedRepeat_ReportsRepeatAtItsLine()
    {
        string error = Guard(LuaConstructs.Local).CheckChunk("local i = 0\nrepeat\n  i = i + 1\nuntil i > 3", "Main");
        Assert.AreEqual("loop `repeat` is not unlocked yet (Main, line 2)", error);
    }

    [Test]
    public void CheckChunk_LockedNamedFunction_ReportsFunction()
    {
        string error = Guard().CheckChunk("function f()\nend", "Main");
        Assert.AreEqual("function definition `function` is not unlocked yet (Main, line 1)", error);
    }

    [Test]
    public void CheckChunk_LockedAnonymousFunction_ReportsFunction()
    {
        string error = Guard().CheckChunk("x = 1\nf = function() return 1 end", "Main");
        Assert.AreEqual("function definition `function` is not unlocked yet (Main, line 2)", error);
    }

    [Test]
    public void CheckChunk_LockedLocal_ReportsLocal()
    {
        string error = Guard().CheckChunk("local x = 1", "Main");
        Assert.AreEqual("variable `local` is not unlocked yet (Main, line 1)", error);
    }

    [Test]
    public void CheckChunk_SeveralLocked_ReportsFirstInCode()
    {
        string error = Guard().CheckChunk("x = 1\nwhile x < 2 do x = x + 1 end\nfor i = 1, 2 do end", "Main");
        StringAssert.Contains("`while`", error);
        StringAssert.Contains("line 2", error);
    }

    [TestCase("else", "`else` (condition `if`)")]
    [TestCase("elseif", "`elseif` (condition `if`)")]
    [TestCase("until", "`until` (loop `repeat`)")]
    public void CheckChunk_DependentKeywordAlone_NamesKeywordAndConstruct(string keyword, string expected)
    {
        // Защита смотрит только на токены: само слово закрыто вместе со своей конструкцией
        string error = Guard().CheckChunk($"x = 1\n{keyword}", "Main");
        Assert.AreEqual($"{expected} is not unlocked yet (Main, line 2)", error);
    }

    [Test]
    public void CheckChunk_IfOpen_ElseAndElseifPass()
    {
        string code = "x = 2\nif x == 1 then y = 1\nelseif x == 2 then y = 2\nelse y = 3 end";
        Assert.IsNull(Guard(LuaConstructs.If).CheckChunk(code, "Main"));
    }

    [Test]
    public void CheckChunk_RepeatOpen_UntilPasses()
    {
        Assert.IsNull(Guard(LuaConstructs.Repeat).CheckChunk("x = 0 repeat x = x + 1 until x > 3", "Main"));
    }

    [TestCase("else", LuaConstructs.If)]
    [TestCase("elseif", LuaConstructs.If)]
    [TestCase("if", LuaConstructs.If)]
    [TestCase("until", LuaConstructs.Repeat)]
    [TestCase("repeat", LuaConstructs.Repeat)]
    [TestCase("for", LuaConstructs.For)]
    [TestCase("while", LuaConstructs.While)]
    [TestCase("function", LuaConstructs.Function)]
    [TestCase("local", LuaConstructs.Local)]
    public void GetConstructForKeyword_LockableKeyword_ReturnsConstruct(string keyword, string expected)
    {
        Assert.AreEqual(expected, LuaConstructs.GetConstructForKeyword(keyword));
    }

    [TestCase("break")]
    [TestCase("return")]
    [TestCase("do")]
    [TestCase("end")]
    [TestCase("then")]
    [TestCase("in")]
    [TestCase("and")]
    [TestCase("or")]
    [TestCase("not")]
    [TestCase("nil")]
    public void GetConstructForKeyword_NeverLockedKeyword_ReturnsNull(string keyword)
    {
        Assert.IsNull(LuaConstructs.GetConstructForKeyword(keyword));
    }

    [Test]
    public void CheckChunk_NeverLockedKeywords_PassWithEverythingLocked()
    {
        string code = "do x = 1 end\nbreak\nx = not (x and true or nil)\nreturn x";
        Assert.IsNull(Guard().CheckChunk(code, "Main"));
    }

    // ─── goto и метки: запрещены навсегда ─────────────────────

    [Test]
    public void GotoKeyword_ForbiddenAndNotUnlockable()
    {
        Assert.IsTrue(LuaConstructs.IsForbiddenKeyword("goto"));
        Assert.IsNull(LuaConstructs.GetConstructForKeyword("goto"));
        Assert.IsFalse(LuaConstructs.IsKnown("goto"));
    }

    [Test]
    public void CheckChunk_Goto_RefusedEvenWithEverythingOpen()
    {
        string error = Guard(AllButClasses).CheckChunk("x = 1\ngoto done\nx = 2", "Main");
        Assert.AreEqual("`goto` is not available, use loops instead (Main, line 2)", error);
    }

    [Test]
    public void CheckChunk_Label_RefusedEvenWithEverythingOpen()
    {
        string error = Guard(AllButClasses).CheckChunk("x = 1\n\n::again::\nx = 2", "Main");
        Assert.AreEqual("labels `::name::` are not available, use loops instead (Main, line 3)", error);
    }

    [Test]
    public void CheckChunk_GotoLoop_RefusedAtFirstForbiddenToken()
    {
        StringAssert.Contains("(Helper, line 1)", Guard().CheckChunk("::a:: x = 1 goto a", "Helper"));
    }

    [Test]
    public void CheckChunk_GotoAndLabelInStringsOrComments_Pass()
    {
        string code = "s = \"goto a ::a::\"\n-- goto a\n--[[ ::a:: ]]\nt = [[goto]]\nx = s:len()";
        Assert.IsNull(Guard().CheckChunk(code, "Main"));
    }

    [Test]
    public void CheckChunk_AllConstructsOpen_Passes()
    {
        string code = "local function f(n)\n  local s = 0\n  for i = 1, n do s = s + i end\n" +
                      "  while s > 100 do s = s - 1 end\n  repeat s = s + 1 until s > 0\n" +
                      "  if s > 1 then return s elseif s < 0 then return 0 else return 1 end\nend";
        Assert.IsNull(Guard(AllButClasses).CheckChunk(code, "Main"));
    }

    [Test]
    public void CheckChunk_EmptyOrNullCode_Passes()
    {
        LuaConstructGuard guard = Guard();
        Assert.IsNull(guard.CheckChunk(null, "Main"));
        Assert.IsNull(guard.CheckChunk("", "Main"));
    }

    // ─── Строки, комментарии и длинные скобки не считаются ────

    [TestCase("print(\"for while repeat until function local if else\")")]
    [TestCase("print('for i = 1, 2 do end')")]
    [TestCase("print(\"say \\\"for\\\" while\")")]
    [TestCase("-- for i = 1, 3 do while true do end end")]
    [TestCase("x = 1 -- function f() end")]
    [TestCase("--[[ for\nwhile\nrepeat until ]]")]
    [TestCase("--[==[ for ]] while ]=] function ]==]")]
    [TestCase("x = [[for while\nfunction]]")]
    [TestCase("x = [==[ for ]] while ]=] repeat ]==]")]
    [TestCase("x = [=[\nlocal if else\n]=]")]
    public void CheckChunk_KeywordsInStringsAndComments_Ignored(string code)
    {
        Assert.IsNull(Guard().CheckChunk(code, "Main"));
    }

    [TestCase("format = 1\nfortune = 2\nwhiled = 3\nfunctional = 4\nlocals = 5\n_if = 6\nfor_ = 7")]
    [TestCase("t = { untill = 1, elsewhere = 2, repeater = 3 }")]
    public void CheckChunk_IdentifiersContainingKeywords_Ignored(string code)
    {
        Assert.IsNull(Guard().CheckChunk(code, "Main"));
    }

    [Test]
    public void CheckChunk_KeywordAfterMultilineComment_ReportsRealLine()
    {
        string code = "--[[ first\nsecond\nthird ]]\nfor i = 1, 2 do end";
        Assert.AreEqual("loop `for` is not unlocked yet (Main, line 4)", Guard().CheckChunk(code, "Main"));
    }

    [Test]
    public void CheckChunk_KeywordAfterMultilineLongString_ReportsRealLine()
    {
        string code = "x = [==[\n]]\n]==] while x do end";
        Assert.AreEqual("loop `while` is not unlocked yet (Main, line 3)", Guard().CheckChunk(code, "Main"));
    }

    [Test]
    public void CheckChunk_KeywordAfterStringWithEscapedNewline_ReportsRealLine()
    {
        // В Lua "\<перевод строки>" — допустимая escape-последовательность: строка продолжается
        // на следующей строке. Цикл стоит на строке 3.
        string code = "s = \"a\\\nb\"\nfor i = 1, 2 do end";
        Assert.DoesNotThrow(() => Lua.LuaState.Create().Load(code.AsSpan(), "Main", null), "valid Lua");
        Assert.AreEqual("loop `for` is not unlocked yet (Main, line 3)", Guard().CheckChunk(code, "Main"));
    }

    [Test]
    public void CheckChunk_UnterminatedStringEndingWithBackslash_DoesNotThrow()
    {
        // Lua отвергнет такой код раньше, но токенайзер общий с подсветкой — падать он не должен
        LuaConstructGuard guard = Guard();
        Assert.DoesNotThrow(() => guard.CheckChunk("x = \"abc\\", "Main"));
    }

    // ─── Окна-«классы» ────────────────────────────────────────

    [Test]
    public void Check_AuxWindowWithCode_ClassesLocked_Refuses()
    {
        var aux = new Dictionary<string, string> { { "Helper", "x = 1" } };

        string error = Guard(AllButClasses).Check("y = 2", aux, "Main");

        Assert.AreEqual("class windows are not unlocked yet (window Helper): move the code into Main or clear the window",
            error);
    }

    [TestCase("")]
    [TestCase("   \n\t\n")]
    [TestCase("-- just a note\n--[[ long\ncomment ]]\n")]
    [TestCase(null)]
    public void Check_AuxWindowWithoutCode_ClassesLocked_Passes(string auxCode)
    {
        var aux = new Dictionary<string, string> { { "Helper", auxCode } };
        Assert.IsNull(Guard(AllButClasses).Check("y = 2", aux, "Main"));
    }

    [Test]
    public void Check_AuxWindowWithCode_ClassesOpen_Passes()
    {
        var aux = new Dictionary<string, string> { { "Helper", "x = 1" } };
        Assert.IsNull(Guard(LuaConstructs.Classes).Check("y = 2", aux, "Main"));
    }

    [Test]
    public void Check_LockedKeywordInAuxWindow_LineCountedInWindowSource()
    {
        var aux = new Dictionary<string, string> { { "Helper", "x = 1\n\nfor i = 1, 2 do end" } };

        string error = Guard(LuaConstructs.Classes).Check("y = 2", aux, "Main");

        // Без служебной обёртки `function Helper(...)` — строка 3, как в редакторе
        Assert.AreEqual("loop `for` is not unlocked yet (Helper, line 3)", error);
    }

    [Test]
    public void Check_LockedInMainAndAux_ReportsMainFirst()
    {
        var aux = new Dictionary<string, string> { { "Helper", "while true do end" } };

        string error = Guard(LuaConstructs.Classes).Check("x = 1\nfor i = 1, 2 do end", aux, "Main");

        Assert.AreEqual("loop `for` is not unlocked yet (Main, line 2)", error);
    }

    [Test]
    public void Check_NullAuxWindows_ChecksMainOnly()
    {
        LuaConstructGuard guard = Guard();
        Assert.IsNull(guard.Check("x = 1", null, "Main"));
        StringAssert.Contains("`for`", guard.Check("for i = 1, 2 do end", null, "Main"));
    }

    // ─── Сбор использованных конструкций (для ConstructUsed) ──

    private static readonly string[] AllConstructs =
    {
        LuaConstructs.Local, LuaConstructs.If, LuaConstructs.For, LuaConstructs.While,
        LuaConstructs.Repeat, LuaConstructs.Function, LuaConstructs.Classes
    };

    private HashSet<string> Used(string code)
    {
        var used = new HashSet<string>();
        Assert.IsNull(Guard(AllConstructs).CheckChunk(code, "Main", used));
        return used;
    }

    [TestCase("for i = 1, 3 do end", LuaConstructs.For)]
    [TestCase("while false do end", LuaConstructs.While)]
    [TestCase("repeat x = 1 until true", LuaConstructs.Repeat)]
    [TestCase("local x = 1", LuaConstructs.Local)]
    [TestCase("if x then y = 1 end", LuaConstructs.If)]
    [TestCase("function f() end", LuaConstructs.Function)]
    [TestCase("f = function() end", LuaConstructs.Function)]
    public void CheckChunk_Used_CollectsConstruct(string code, string expected)
    {
        CollectionAssert.AreEquivalent(new[] { expected }, Used(code));
    }

    [Test]
    public void CheckChunk_Used_LocalFunctionCollectsBoth()
    {
        CollectionAssert.AreEquivalent(new[] { LuaConstructs.Local, LuaConstructs.Function },
            Used("local function f() return 1 end"));
    }

    [Test]
    public void CheckChunk_Used_RepeatedConstructCollectedOnce()
    {
        HashSet<string> used = Used("for i = 1, 2 do end\nfor j = 1, 2 do for k = 1, 2 do end end");
        CollectionAssert.AreEquivalent(new[] { LuaConstructs.For }, used);
    }

    [Test]
    public void CheckChunk_Used_ElseifElseUntilMapToTheirConstructs()
    {
        CollectionAssert.AreEquivalent(new[] { LuaConstructs.If },
            Used("if a then b = 1 elseif c then b = 2 else b = 3 end"));
        CollectionAssert.AreEquivalent(new[] { LuaConstructs.Repeat }, Used("repeat a = 1 until a"));
    }

    [Test]
    public void CheckChunk_Used_KeywordsInStringsAndCommentsNotCollected()
    {
        HashSet<string> used = Used("s = \"for while\" -- function\n--[[ repeat until ]]\nt = [[local if]]\nx = s");
        CollectionAssert.IsEmpty(used);
    }

    [Test]
    public void CheckChunk_Used_NoConstructs_Empty()
    {
        CollectionAssert.IsEmpty(Used("x = 1\nprint(x and not y or 2)\ndo x = 3 end"));
    }

    [Test]
    public void Check_Used_AuxWindowWithCode_AddsClassesAndItsConstructs()
    {
        var aux = new Dictionary<string, string> { { "Helper", "while false do end" } };
        var used = new HashSet<string>();

        Assert.IsNull(Guard(AllConstructs).Check("for i = 1, 2 do Helper() end", aux, "Main", used));

        CollectionAssert.AreEquivalent(new[] { LuaConstructs.Classes, LuaConstructs.For, LuaConstructs.While }, used);
    }

    [Test]
    public void Check_Used_CommentOnlyAuxWindow_NoClasses()
    {
        var aux = new Dictionary<string, string> { { "Helper", "-- later\n" }, { "Empty", "" } };
        var used = new HashSet<string>();

        Assert.IsNull(Guard(AllConstructs).Check("x = 1", aux, "Main", used));

        CollectionAssert.IsEmpty(used);
    }

    [Test]
    public void Check_Used_NullSet_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => Guard(AllConstructs).Check("for i = 1, 2 do end", null, "Main"));
    }
}
