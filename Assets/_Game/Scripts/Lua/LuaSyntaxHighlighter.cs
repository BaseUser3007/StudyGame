using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Превращает Lua код в rich text с цветами из CodeEditorTheme.
/// Текст после подсветки должен совпадать с исходным символ в символ —
/// он рисуется под прозрачным текстом поля ввода.
/// Ключевые слова закрытых конструкций языка рисуются отдельным цветом (lockedKeywordColor).
/// </summary>
public class LuaSyntaxHighlighter
{
    private HashSet<string> _apiNames;
    // Ключевое слово закрыто (конструкция ещё не открыта). Null — всё открыто
    private readonly Func<string, bool> _isKeywordLocked;

    private readonly string _keywordColor;
    private readonly string _lockedKeywordColor;
    private readonly string _apiColor;
    private readonly string _stringColor;
    private readonly string _commentColor;
    private readonly string _numberColor;
    private readonly string _defaultColor;

    private readonly LuaTokenizer _tokenizer = new LuaTokenizer();

    public LuaSyntaxHighlighter(
        CodeEditorTheme theme, IEnumerable<string> apiNames, Func<string, bool> isKeywordLocked = null)
    {
        _apiNames = new HashSet<string>(apiNames);
        _isKeywordLocked = isKeywordLocked;

        _keywordColor = ColorToHex(theme.keywordColor);
        _lockedKeywordColor = ColorToHex(theme.lockedKeywordColor);
        _apiColor = ColorToHex(theme.apiColor);
        _stringColor = ColorToHex(theme.stringColor);
        _commentColor = ColorToHex(theme.commentColor);
        _numberColor = ColorToHex(theme.numberColor);
        _defaultColor = ColorToHex(theme.defaultColor);
    }

    // ─── Public API ───────────────────────────────────────────

    /// <summary>
    /// Заменяет список имён API (открылись новые команды). Открытые окна нужно перерисовать.
    /// </summary>
    public void SetApiNames(IEnumerable<string> apiNames)
    {
        _apiNames = new HashSet<string>(apiNames);
    }

    public string Highlight(string code)
    {
        if (string.IsNullOrEmpty(code)) return string.Empty;

        StringBuilder sb = new StringBuilder(code.Length * 2);

        foreach (LuaTokenizer.Token token in _tokenizer.Tokenize(code))
        {
            if (token.Type == LuaTokenizer.TokenType.Whitespace ||
                token.Type == LuaTokenizer.TokenType.Newline)
            {
                sb.Append(token.Value);
                continue;
            }

            sb.Append("<color=").Append(GetColor(token)).Append('>');
            AppendEscaped(sb, token.Value);
            sb.Append("</color>");
        }

        return sb.ToString();
    }

    // ─── Helpers ──────────────────────────────────────────────

    private string GetColor(LuaTokenizer.Token token)
    {
        switch (token.Type)
        {
            case LuaTokenizer.TokenType.Keyword:
                return _isKeywordLocked != null && _isKeywordLocked(token.Value)
                    ? _lockedKeywordColor
                    : _keywordColor;
            case LuaTokenizer.TokenType.String: return _stringColor;
            case LuaTokenizer.TokenType.Comment: return _commentColor;
            case LuaTokenizer.TokenType.Number: return _numberColor;
            case LuaTokenizer.TokenType.Identifier:
                return _apiNames.Contains(token.Value) ? _apiColor : _defaultColor;
            default:
                return _defaultColor;
        }
    }

    /// <summary>
    /// Символ '<' может начать rich text тег, поэтому такие фрагменты
    /// оборачиваются в noparse — видимый текст не меняется.
    /// </summary>
    private static void AppendEscaped(StringBuilder sb, string text)
    {
        if (text.IndexOf('<') < 0)
        {
            sb.Append(text);
            return;
        }

        sb.Append("<noparse>").Append(text.Replace("</noparse>", "</no​parse>")).Append("</noparse>");
    }

    private static string ColorToHex(Color color)
    {
        return $"#{ColorUtility.ToHtmlStringRGB(color)}";
    }
}
