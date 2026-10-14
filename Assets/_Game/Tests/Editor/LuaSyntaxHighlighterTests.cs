using System;
using System.Text;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Подсветка кода: цвет закрытых ключевых слов, имена API, и главное — видимый текст подсветки
/// посимвольно совпадает с исходным (подсветка рисуется под прозрачным текстом поля).
/// </summary>
public class LuaSyntaxHighlighterTests
{
    private const string Keyword = "#0000FF";
    private const string Locked = "#FF0000";
    private const string Api = "#00FF00";
    private const string Default = "#FFFFFF";

    private CodeEditorTheme _theme;

    [SetUp]
    public void SetUp()
    {
        _theme = ScriptableObject.CreateInstance<CodeEditorTheme>();
        _theme.keywordColor = Color.blue;
        _theme.lockedKeywordColor = Color.red;
        _theme.apiColor = Color.green;
        _theme.defaultColor = Color.white;
        _theme.stringColor = new Color(1f, 1f, 0f);
        _theme.commentColor = new Color(0.5f, 0.5f, 0.5f);
        _theme.numberColor = new Color(1f, 0f, 1f);
    }

    [TearDown]
    public void TearDown()
    {
        UnityEngine.Object.DestroyImmediate(_theme);
    }

    private LuaSyntaxHighlighter Highlighter(Func<string, bool> isLocked = null, params string[] api)
        => new LuaSyntaxHighlighter(_theme, api, isLocked);

    private static string Colored(string color, string text) => $"<color={color}>{text}</color>";

    /// <summary>
    /// Видимый текст rich text: теги color убираются, содержимое noparse копируется как есть.
    /// Внутри noparse закрывающий тег экранирован нулевым пробелом (U+200B) — он невидим и без ширины.
    /// </summary>
    private static string VisibleText(string richText, bool dropZeroWidthSpace = true)
    {
        var sb = new StringBuilder();
        int pos = 0;
        while (pos < richText.Length)
        {
            if (string.CompareOrdinal(richText, pos, "<noparse>", 0, 9) == 0)
            {
                int end = richText.IndexOf("</noparse>", pos + 9, StringComparison.Ordinal);
                Assert.GreaterOrEqual(end, 0, "unterminated <noparse>");
                sb.Append(richText, pos + 9, end - pos - 9);
                pos = end + "</noparse>".Length;
                continue;
            }

            if (richText[pos] == '<')
            {
                int close = richText.IndexOf('>', pos);
                Assert.GreaterOrEqual(close, 0, "unterminated tag");
                string tag = richText.Substring(pos, close - pos + 1);
                Assert.IsTrue(tag.StartsWith("<color=") || tag == "</color>",
                    $"unexpected tag outside noparse: {tag}");
                pos = close + 1;
                continue;
            }

            sb.Append(richText[pos]);
            pos++;
        }

        string text = sb.ToString();
        return dropZeroWidthSpace ? text.Replace("​", "") : text;
    }

    // ─── Закрытые ключевые слова ──────────────────────────────

    [Test]
    public void Highlight_LockedKeyword_GetsLockedColor()
    {
        LuaSyntaxHighlighter highlighter = Highlighter(k => LuaConstructs.GetConstructForKeyword(k) == LuaConstructs.For);

        string result = highlighter.Highlight("for i = 1, 2 do end");

        StringAssert.Contains(Colored(Locked, "for"), result);
        StringAssert.Contains(Colored(Keyword, "do"), result);
        StringAssert.Contains(Colored(Keyword, "end"), result);
        StringAssert.DoesNotContain(Colored(Keyword, "for"), result);
    }

    [Test]
    public void Highlight_IfLocked_ElseAndElseifLockedToo()
    {
        LuaSyntaxHighlighter highlighter = Highlighter(k => LuaConstructs.GetConstructForKeyword(k) == LuaConstructs.If);

        string result = highlighter.Highlight("if a then b() elseif c then d() else e() end");

        StringAssert.Contains(Colored(Locked, "if"), result);
        StringAssert.Contains(Colored(Locked, "elseif"), result);
        StringAssert.Contains(Colored(Locked, "else"), result);
        StringAssert.Contains(Colored(Keyword, "then"), result);
        StringAssert.Contains(Colored(Keyword, "end"), result);
    }

    [Test]
    public void Highlight_LockedKeywordInStringOrComment_NotLockedColor()
    {
        LuaSyntaxHighlighter highlighter = Highlighter(_ => true);

        string result = highlighter.Highlight("x = \"for\" -- while\ny = [[repeat]]");

        StringAssert.DoesNotContain(Locked, result);
    }

    [Test]
    public void Highlight_NoLockPredicate_AllKeywordsNormalColor()
    {
        string result = Highlighter().Highlight("while true do break end");

        StringAssert.DoesNotContain(Locked, result);
        StringAssert.Contains(Colored(Keyword, "while"), result);
    }

    // ─── Имена API ────────────────────────────────────────────

    [Test]
    public void SetApiNames_ReplacesHighlightedCommands()
    {
        LuaSyntaxHighlighter highlighter = Highlighter(null, "move");
        Assert.That(highlighter.Highlight("mine()"), Does.Contain(Colored(Default, "mine")));
        Assert.That(highlighter.Highlight("move()"), Does.Contain(Colored(Api, "move")));

        highlighter.SetApiNames(new[] { "mine" });

        Assert.That(highlighter.Highlight("mine()"), Does.Contain(Colored(Api, "mine")));
        Assert.That(highlighter.Highlight("move()"), Does.Contain(Colored(Default, "move")));
    }

    // ─── Видимый текст совпадает с исходным ───────────────────

    [TestCase("for i = 1, 10 do\n  if i < 5 then move(\"right\") else break end\nend")]
    [TestCase("local s = \"<color=red>x</color>\" -- <b>bold</b>")]
    [TestCase("--[[ a < b\n while ]]\nx = [==[ <i> ]] ]==]")]
    [TestCase("x = a<b and b<=c or c<<2\r\n\ty = 'it''s'")]
    [TestCase("x = \"</noparse><b>\"")]
    [TestCase("  \n\n   ")]
    [TestCase("привет = 1 -- юникод")]
    public void Highlight_LockedKeywords_VisibleTextEqualsSource(string code)
    {
        string result = Highlighter(_ => true, "move").Highlight(code);
        Assert.AreEqual(code, VisibleText(result));
    }

    [Test]
    public void Highlight_PlainCode_NoZeroWidthCharactersAdded()
    {
        // U+200B добавляется только для экранирования "</noparse>" внутри токена
        string code = "if a < b then return \"<x>\" end";
        Assert.AreEqual(code, VisibleText(Highlighter(_ => true).Highlight(code), dropZeroWidthSpace: false));
    }

    [Test]
    public void Highlight_EmptyOrNull_ReturnsEmpty()
    {
        Assert.AreEqual(string.Empty, Highlighter().Highlight(null));
        Assert.AreEqual(string.Empty, Highlighter().Highlight(""));
    }

    [Test]
    public void Highlight_UnterminatedStringEndingWithBackslash_DoesNotThrow()
    {
        // Игрок набирает `x = "abc\` — подсветка пересчитывается на каждый символ и не должна падать
        string code = "x = \"abc\\";
        string result = null;
        Assert.DoesNotThrow(() => result = Highlighter().Highlight(code));
        Assert.AreEqual(code, VisibleText(result));
    }

    [Test]
    public void Highlight_UnterminatedLongBracket_VisibleTextEqualsSource()
    {
        string code = "x = [==[ for\nwhile ]] ]=]";
        Assert.AreEqual(code, VisibleText(Highlighter(_ => true).Highlight(code)));
    }
}
