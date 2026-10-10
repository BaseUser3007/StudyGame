using System.Collections.Generic;
using System.Text;
using UnityEngine;

public class LuaSyntaxHighlighter
{
    private static readonly HashSet<string> ApiMethods = new HashSet<string>
    {
        "move", "moveTo", "chop", "mine", "harvest",
        "hunt", "pickup", "eat", "sleep", "craft",
        "getPosition", "distanceTo", "getInventory",
        "hasItem", "itemCount", "getTool",
        "findObjects", "getNear",
        "autoChop", "autoMine", "autoHarvest",
        "autoExplore", "autoEat"
    };

    private readonly string _keywordColor;
    private readonly string _apiColor;
    private readonly string _stringColor;
    private readonly string _commentColor;
    private readonly string _numberColor;
    private readonly string _defaultColor;

    private readonly LuaTokenizer _tokenizer;

    public LuaSyntaxHighlighter(
        Color keywordColor,
        Color apiColor,
        Color stringColor,
        Color commentColor,
        Color numberColor,
        Color defaultColor)
    {
        _keywordColor = ColorToHex(keywordColor);
        _apiColor = ColorToHex(apiColor);
        _stringColor = ColorToHex(stringColor);
        _commentColor = ColorToHex(commentColor);
        _numberColor = ColorToHex(numberColor);
        _defaultColor = ColorToHex(defaultColor);
        _tokenizer = new LuaTokenizer();
    }

    // ─── Public API ───────────────────────────────────────────

    public string Highlight(string code)
    {
        if (string.IsNullOrEmpty(code)) return code;

        List<LuaTokenizer.Token> tokens = _tokenizer.Tokenize(code);
        return BuildRichText(tokens);
    }

    // ─── RichText ─────────────────────────────────────────────

    private string BuildRichText(List<LuaTokenizer.Token> tokens)
    {
        StringBuilder sb = new StringBuilder();

        foreach (LuaTokenizer.Token token in tokens)
        {
            string color = GetColor(token);
            string escaped = EscapeRichText(token.Value);
            sb.Append($"<color={color}>{escaped}</color>");
        }

        return sb.ToString();
    }

    private string GetColor(LuaTokenizer.Token token)
    {
        switch (token.Type)
        {
            case LuaTokenizer.TokenType.Keyword: return _keywordColor;
            case LuaTokenizer.TokenType.String: return _stringColor;
            case LuaTokenizer.TokenType.Comment: return _commentColor;
            case LuaTokenizer.TokenType.Number: return _numberColor;
            case LuaTokenizer.TokenType.Identifier:
                return ApiMethods.Contains(token.Value) ? _apiColor : _defaultColor;
            default:
                return _defaultColor;
        }
    }

    private string EscapeRichText(string text)
    {
        return text
            .Replace("<", "<\u200B")
            .Replace(">", "\u200B>");
    }

    private string ColorToHex(Color color)
    {
        return $"#{ColorUtility.ToHtmlStringRGB(color)}";
    }
}