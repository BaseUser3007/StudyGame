using System.Collections.Generic;

public class EnergyCounter
{
    private readonly LuaTokenizer _tokenizer;
    private readonly EnergyConfig _config;
    private readonly LuaCommandRegistry _registry;

    public EnergyCounter(EnergyConfig config, LuaCommandRegistry registry)
    {
        _tokenizer = new LuaTokenizer();
        _config = config;
        _registry = registry;
    }

    // ─── Public API ───────────────────────────────────────────

    public float CalculateTotalCost(string code)
    {
        if (string.IsNullOrEmpty(code)) return 0f;

        List<LuaTokenizer.Token> tokens = _tokenizer.Tokenize(code);
        float total = 0f;

        for (int i = 0; i < tokens.Count; i++)
            total += GetTokenCost(tokens, i);

        return total;
    }

    public Dictionary<int, float> CalculateLineCosts(string code)
    {
        Dictionary<int, float> lineCosts = new Dictionary<int, float>();

        if (string.IsNullOrEmpty(code)) return lineCosts;

        List<LuaTokenizer.Token> tokens = _tokenizer.Tokenize(code);

        for (int i = 0; i < tokens.Count; i++)
        {
            LuaTokenizer.Token token = tokens[i];
            if (token.Type == LuaTokenizer.TokenType.Newline ||
                token.Type == LuaTokenizer.TokenType.Whitespace)
                continue;

            float cost = GetTokenCost(tokens, i);
            if (cost <= 0f) continue;

            if (!lineCosts.ContainsKey(token.Line))
                lineCosts[token.Line] = 0f;

            lineCosts[token.Line] += cost;
        }

        return lineCosts;
    }

    // ─── Cost Calculation ─────────────────────────────────────

    private float GetTokenCost(List<LuaTokenizer.Token> tokens, int index)
    {
        LuaTokenizer.Token token = tokens[index];

        if (token.Type == LuaTokenizer.TokenType.Keyword)
            return GetKeywordCost(token.Value);

        if (token.Type == LuaTokenizer.TokenType.Identifier)
        {
            // Поле таблицы с именем команды (info.puzzle у check) — не вызов команды, во время выполнения
            // за него ничего не списывается
            if (_registry.HasCommand(token.Value))
                return IsFieldAccess(tokens, index) ? 0f : _registry.GetEnergyCost(token.Value);

            // Методы массива findObjects (getNear, where, sortBy …)
            return LuaObjectList.IsMethod(token.Value) ? LuaObjectList.MethodCost : 0f;
        }

        return 0f;
    }

    /// <summary>
    /// Идентификатор после «.» или «:» (a.puzzle, a:puzzle) — обращение к полю, а не глобальная команда.
    /// «..» — конкатенация ("x" .. puzzle(obj)), там команда настоящая.
    /// </summary>
    private static bool IsFieldAccess(List<LuaTokenizer.Token> tokens, int index)
    {
        int prev = PreviousSignificant(tokens, index);
        if (prev < 0 || tokens[prev].Type != LuaTokenizer.TokenType.Operator) return false;
        if (tokens[prev].Value == ":") return true;
        if (tokens[prev].Value != ".") return false;

        // Токенайзер отдаёт операторы по одному символу: «..» — это две точки подряд
        return prev == 0 || tokens[prev - 1].Type != LuaTokenizer.TokenType.Operator || tokens[prev - 1].Value != ".";
    }

    private static int PreviousSignificant(List<LuaTokenizer.Token> tokens, int index)
    {
        for (int i = index - 1; i >= 0; i--)
        {
            LuaTokenizer.TokenType type = tokens[i].Type;
            if (type != LuaTokenizer.TokenType.Whitespace && type != LuaTokenizer.TokenType.Newline
                && type != LuaTokenizer.TokenType.Comment)
                return i;
        }
        return -1;
    }

    private float GetKeywordCost(string keyword)
    {
        switch (keyword)
        {
            case "if": return _config.ifCost;
            case "elseif": return _config.elseifCost;
            case "else": return _config.elseCost;
            case "for": return _config.forCost;
            case "while": return _config.whileCost;
            case "repeat": return _config.repeatCost;
            case "function": return _config.functionCost;
            case "local": return _config.localCost;
            case "return": return _config.returnCost;
            default: return 0f;
        }
    }
}