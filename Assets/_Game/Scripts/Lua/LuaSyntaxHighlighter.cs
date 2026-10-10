using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Превращает Lua код в rich text с цветами из CodeEditorTheme.
/// Текст после подсветки должен совпадать с исходным символ в символ —
/// он рисуется под прозрачным текстом поля ввода.
/// </summary>
public class LuaSyntaxHighlighter
{
    private readonly HashSet<string> _apiNames;

    private readonly string _keywordColor;
    private readonly string _apiColor;
    private readonly string _stringColor;
    private readonly string _commentColor;
    private readonly string _numberColor;
    private readonly string _defaultColor;

    private readonly LuaTokenizer _tokenizer = new LuaTokenizer();

    public LuaSyntaxHighlighter(CodeEditorTheme theme, IEnumerable<string> apiNames)
    {
        _apiNames = new HashSet<string>(apiNames);

        _keywordColor = ColorToHex(theme.keywordColor);
        _apiColor = ColorToHex(theme.apiColor);
        _stringColor = ColorToHex(theme.stringColor);
        _commentColor = ColorToHex(theme.commentColor);
        _numberColor = ColorToHex(theme.numberColor);
        _defaultColor = ColorToHex(theme.defaultColor);
    }

    // ─── Public API ───────────────────────────────────────────

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
            case LuaTokenizer.TokenType.Keyword: return _keywordColor;
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
