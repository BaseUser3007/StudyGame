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

        foreach (LuaTokenizer.Token token in tokens)
            total += GetTokenCost(token);

        return total;
    }

    public Dictionary<int, float> CalculateLineCosts(string code)
    {
        Dictionary<int, float> lineCosts = new Dictionary<int, float>();

        if (string.IsNullOrEmpty(code)) return lineCosts;

        List<LuaTokenizer.Token> tokens = _tokenizer.Tokenize(code);

        foreach (LuaTokenizer.Token token in tokens)
        {
            if (token.Type == LuaTokenizer.TokenType.Newline ||
                token.Type == LuaTokenizer.TokenType.Whitespace)
                continue;

            float cost = GetTokenCost(token);
            if (cost <= 0f) continue;

            if (!lineCosts.ContainsKey(token.Line))
                lineCosts[token.Line] = 0f;

            lineCosts[token.Line] += cost;
        }

        return lineCosts;
    }

    // ─── Cost Calculation ─────────────────────────────────────

    private float GetTokenCost(LuaTokenizer.Token token)
    {
        if (token.Type == LuaTokenizer.TokenType.Keyword)
            return GetKeywordCost(token.Value);

        if (token.Type == LuaTokenizer.TokenType.Identifier)
        {
            if (_registry.HasCommand(token.Value))
                return _registry.GetEnergyCost(token.Value);

            // Методы массива findObjects (getNear, where, sortBy …)
            return LuaObjectList.IsMethod(token.Value) ? LuaObjectList.MethodCost : 0f;
        }

        return 0f;
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