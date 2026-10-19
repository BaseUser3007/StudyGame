using System.Collections.Generic;

/// <summary>
/// Проверка перед запуском: код не использует закрытые конструкции языка (<see cref="LuaConstructs"/>).
/// Ищутся только токены-ключевые слова — текст в строках, комментариях и длинных скобках не считается.
/// Проверяется исходный код окон (без служебной обёртки `function Title(...)` доп. окон),
/// поэтому номера строк совпадают с редактором.
/// Тот же проход собирает id всех конструкций в коде (открытых тоже) — для события ConstructUsed.
/// </summary>
public class LuaConstructGuard
{
    private readonly LuaTokenizer _tokenizer = new LuaTokenizer();
    private readonly UnlockRegistry _unlocks;

    public LuaConstructGuard(UnlockRegistry unlocks)
    {
        _unlocks = unlocks;
    }

    // ─── Public API ───────────────────────────────────────────

    /// <summary>
    /// Сообщение для игрока о первой закрытой конструкции или null, если всё открыто.
    /// Пример: "loop `for` is not unlocked yet (Main, line 3)".
    /// </summary>
    /// <param name="used">Если задан — сюда добавляются id всех конструкций в коде (при отказе — неполный список).</param>
    public string Check(
        string mainCode, IReadOnlyDictionary<string, string> auxiliaryWindows, string mainName,
        ISet<string> used = null)
    {
        if (auxiliaryWindows != null)
        {
            // Доп. окна с кодом — это уже «классы», даже если в них нет закрытых слов
            bool classesUnlocked = _unlocks.IsConstructUnlocked(LuaConstructs.Classes);
            foreach (var kvp in auxiliaryWindows)
            {
                if (!HasCode(kvp.Value)) continue;
                if (!classesUnlocked)
                    return $"{LuaConstructs.GetDisplayName(LuaConstructs.Classes)} are not unlocked yet " +
                           $"(window {kvp.Key}): move the code into {mainName} or clear the window";
                used?.Add(LuaConstructs.Classes);
            }
        }

        string error = CheckChunk(mainCode, mainName, used);
        if (error != null || auxiliaryWindows == null) return error;

        foreach (var kvp in auxiliaryWindows)
        {
            error = CheckChunk(kvp.Value, kvp.Key, used);
            if (error != null) return error;
        }

        return null;
    }

    /// <summary>
    /// Первая закрытая конструкция в одном окне (номер строки — в этом окне) или null.
    /// </summary>
    /// <param name="used">Если задан — сюда добавляются id конструкций, встреченных в окне.</param>
    public string CheckChunk(string code, string chunkName, ISet<string> used = null)
    {
        if (string.IsNullOrEmpty(code)) return null;

        List<LuaTokenizer.Token> tokens = _tokenizer.Tokenize(code);

        // Синтаксис к этому моменту уже проверен Lua, но если разбор неточный — не угадываем
        if (!LuaTokenizer.Reassembles(tokens, code))
            return $"code could not be checked for locked constructs ({chunkName})";

        for (int i = 0; i < tokens.Count; i++)
        {
            LuaTokenizer.Token token = tokens[i];

            // Метка ::name:: — два ':' подряд (вызов метода — одиночный ':'). Запрещена навсегда, как и goto
            if (token.Type == LuaTokenizer.TokenType.Operator && token.Value == ":" && i + 1 < tokens.Count
                && tokens[i + 1].Type == LuaTokenizer.TokenType.Operator && tokens[i + 1].Value == ":")
                return $"labels `::name::` are not available, use loops instead ({chunkName}, line {token.Line})";

            if (token.Type != LuaTokenizer.TokenType.Keyword) continue;

            if (LuaConstructs.IsForbiddenKeyword(token.Value))
                return $"`{token.Value}` is not available, use loops instead ({chunkName}, line {token.Line})";

            string id = LuaConstructs.GetConstructForKeyword(token.Value);
            if (id == null) continue;
            if (_unlocks.IsConstructUnlocked(id))
            {
                used?.Add(id);
                continue;
            }

            return FormatLocked(token.Value, id, chunkName, token.Line);
        }

        return null;
    }

    // ─── Helpers ──────────────────────────────────────────────

    private static string FormatLocked(string keyword, string id, string chunkName, int line)
    {
        string name = LuaConstructs.GetDisplayName(id);

        // else/elseif/until — называем и само слово, и конструкцию, к которой оно относится
        string what = keyword == id ? name : $"`{keyword}` ({name})";
        return $"{what} is not unlocked yet ({chunkName}, line {line})";
    }

    /// <summary>
    /// В окне есть что-то кроме пробелов и комментариев.
    /// </summary>
    private bool HasCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;

        foreach (LuaTokenizer.Token token in _tokenizer.Tokenize(code))
        {
            switch (token.Type)
            {
                case LuaTokenizer.TokenType.Whitespace:
                case LuaTokenizer.TokenType.Newline:
                case LuaTokenizer.TokenType.Comment:
                    continue;
                default:
                    return true;
            }
        }

        return false;
    }
}
