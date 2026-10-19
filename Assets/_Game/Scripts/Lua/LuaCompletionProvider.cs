using System;
using System.Collections.Generic;
using System.Linq;

public enum CompletionKind
{
    Command,
    Function,
    Keyword,
    Snippet,
    Variable,
    Member
}

public class CompletionItem
{
    public string Label;
    /// <summary>Вставляемый текст. "$0" — где окажется курсор, "\n" — перенос с отступом текущей строки.</summary>
    public string InsertText;
    public string Detail;
    public string Description;
    public CompletionKind Kind;
    public float EnergyCost;
}

/// <summary>
/// Подбирает варианты автодополнения для позиции курсора в Lua коде.
/// Не зависит от UI — только код, курсор и список команд.
/// </summary>
public class LuaCompletionProvider
{
    private const int MaxItems = 8;

    private static readonly string[] Keywords =
    {
        "and", "break", "do", "else", "elseif", "end", "false", "for", "function",
        "if", "in", "local", "nil", "not", "or", "repeat", "return", "then",
        "true", "until", "while"
    };

    private static readonly (string label, string insert, string detail)[] Snippets =
    {
        ("for",      "for i = 1, $0 do\n    \nend",          "for i = 1, n do ... end"),
        ("for in",   "for i, v in ipairs($0) do\n    \nend",  "for i, v in ipairs(t) do ... end"),
        ("if",       "if $0 then\n    \nend",                 "if ... then ... end"),
        ("while",    "while $0 do\n    \nend",                "while ... do ... end"),
        ("function", "function $0()\n    \nend",              "function name() ... end"),
    };

    private static readonly (string name, string signature)[] StandardFunctions =
    {
        // print — команда (PrintCommand), приходит из реестра, только когда открыта
        ("tostring", "tostring(v)"), ("tonumber", "tonumber(v)"),
        ("type", "type(v)"), ("pairs", "pairs(t)"), ("ipairs", "ipairs(t)"),
        ("select", "select(n, ...)"), ("error", "error(message)"), ("assert", "assert(v, message)"),
        ("math", "math.*"), ("string", "string.*"), ("table", "table.*")
    };

    // Поля таблиц, которые возвращают команды
    private static readonly (string name, string detail)[] Fields =
    {
        ("x", "координата x"),
        ("y", "координата y"),
        ("id", "уникальный номер объекта — для сравнения a.id == b.id"),
        ("type", "тип объекта"),
        ("distance", "расстояние до персонажа"),
        ("tool", "нужный инструмент или nil"),
        ("durability", "энергия на добычу / прочность инструмента"),
        ("count", "количество элементов"),
        ("free", "свободных ячеек инвентаря"),
        ("name", "название предмета"),
        ("slot", "номер ячейки инвентаря"),
        ("maxDurability", "максимальная прочность инструмента"),
        ("spent", "getStatus(): энергия, потраченная с начала запуска")
    };

    // Методы и поля таблиц, которые возвращают команды (методы — массива findObjects)
    private static readonly (string name, string detail, bool isMethod, bool hasArgs)[] Members = BuildMembers();

    private static (string, string, bool, bool)[] BuildMembers()
    {
        var members = new List<(string, string, bool, bool)>();
        foreach (var method in LuaObjectList.Methods)
            members.Add((method.Name, $"{method.Signature} — {method.Description}", true,
                !method.Signature.EndsWith("()")));
        foreach (var field in Fields)
            members.Add((field.name, field.detail, false, false));
        return members.ToArray();
    }

    private readonly LuaTokenizer _tokenizer = new LuaTokenizer();
    private readonly Func<IEnumerable<ILuaCommand>> _commands;
    private readonly Func<IEnumerable<string>> _windowFunctions;

    public LuaCompletionProvider(
        Func<IEnumerable<ILuaCommand>> commands,
        Func<IEnumerable<string>> windowFunctions)
    {
        _commands = commands;
        _windowFunctions = windowFunctions;
    }

    // ─── Public API ───────────────────────────────────────────

    /// <summary>
    /// Возвращает слово перед курсором (то, что дополняем) и его начало.
    /// </summary>
    public static string GetPrefix(string code, int cursor, out int start)
    {
        cursor = Math.Max(0, Math.Min(cursor, code.Length));
        start = cursor;
        while (start > 0 && IsIdentifierChar(code[start - 1]))
            start--;
        return code.Substring(start, cursor - start);
    }

    public List<CompletionItem> GetCompletions(string code, int cursor, bool forced)
    {
        code ??= string.Empty;
        string prefix = GetPrefix(code, cursor, out int start);

        if (IsInsideStringOrComment(code, start)) return new List<CompletionItem>();
        if (prefix.Length == 0 && !forced) return new List<CompletionItem>();
        if (prefix.Length > 0 && char.IsDigit(prefix[0])) return new List<CompletionItem>();

        bool afterDot = start > 0 && (code[start - 1] == '.' || code[start - 1] == ':');

        IEnumerable<CompletionItem> candidates = afterDot
            ? MemberItems(code[start - 1] == ':')
            : GlobalItems(code, start, prefix);

        List<CompletionItem> result = candidates
            .Where(i => Matches(i.Label, prefix))
            .GroupBy(i => i.Label).Select(g => g.First())
            .OrderBy(i => i.Label.StartsWith(prefix, StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(i => KindOrder(i.Kind))
            .ThenBy(i => i.Label, StringComparer.Ordinal)
            .Take(MaxItems)
            .ToList();

        // Слово уже дописано полностью — подсказывать нечего
        if (result.Count == 1 && result[0].Label == prefix && result[0].Kind != CompletionKind.Snippet)
            result.Clear();

        return result;
    }

    // ─── Candidates ───────────────────────────────────────────

    private IEnumerable<CompletionItem> GlobalItems(string code, int prefixStart, string prefix)
    {
        foreach (ILuaCommand command in _commands())
        {
            bool hasArgs = !command.Signature.EndsWith("()");
            yield return new CompletionItem
            {
                Label = command.Name,
                InsertText = command.Name + (hasArgs ? "($0)" : "()$0"),
                Detail = command.Signature,
                Description = command.Description,
                Kind = CompletionKind.Command,
                EnergyCost = command.EnergyCost
            };
        }

        foreach (string window in _windowFunctions())
            yield return new CompletionItem
            {
                Label = window,
                InsertText = window + "($0)",
                Detail = window + "(...)",
                Description = "Функция из окна " + window,
                Kind = CompletionKind.Function
            };

        foreach (var s in Snippets)
            yield return new CompletionItem
            {
                Label = s.label,
                InsertText = s.insert,
                Detail = s.detail,
                Kind = CompletionKind.Snippet
            };

        foreach (string keyword in Keywords)
            yield return new CompletionItem
            {
                Label = keyword,
                InsertText = keyword + "$0",
                Kind = CompletionKind.Keyword
            };

        foreach (var f in StandardFunctions)
            yield return new CompletionItem
            {
                Label = f.name,
                InsertText = f.signature.EndsWith("*") ? f.name + ".$0" : f.name + "($0)",
                Detail = f.signature,
                Kind = CompletionKind.Function
            };

        foreach (string variable in CollectIdentifiers(code, prefixStart, prefix))
            yield return new CompletionItem
            {
                Label = variable,
                InsertText = variable + "$0",
                Kind = CompletionKind.Variable
            };
    }

    private static IEnumerable<CompletionItem> MemberItems(bool methodCall)
    {
        foreach (var m in Members)
        {
            if (methodCall && !m.isMethod) continue;
            yield return new CompletionItem
            {
                Label = m.name,
                // С аргументами курсор встаёт внутрь скобок
                InsertText = m.name + (!m.isMethod ? "$0" : m.hasArgs ? "($0)" : "()$0"),
                Detail = m.detail,
                Kind = CompletionKind.Member
            };
        }
    }

    /// <summary>
    /// Идентификаторы, уже встречающиеся в коде (переменные игрока).
    /// Слово под курсором пропускается.
    /// </summary>
    private IEnumerable<string> CollectIdentifiers(string code, int prefixStart, string prefix)
    {
        var seen = new HashSet<string>();
        int index = 0;

        foreach (LuaTokenizer.Token token in _tokenizer.Tokenize(code))
        {
            int tokenStart = index;
            index += token.Value.Length;

            if (token.Type != LuaTokenizer.TokenType.Identifier) continue;
            if (tokenStart == prefixStart && token.Value.Length >= prefix.Length) continue;
            if (tokenStart > 0 && (code[tokenStart - 1] == '.' || code[tokenStart - 1] == ':')) continue;

            if (seen.Add(token.Value))
                yield return token.Value;
        }
    }

    // ─── Helpers ──────────────────────────────────────────────

    private bool IsInsideStringOrComment(string code, int position)
    {
        int index = 0;
        foreach (LuaTokenizer.Token token in _tokenizer.Tokenize(code))
        {
            int tokenStart = index;
            index += token.Value.Length;

            if (position <= tokenStart) return false;
            if (position > index) continue;

            if (token.Type == LuaTokenizer.TokenType.Comment) return true;
            if (token.Type == LuaTokenizer.TokenType.String)
            {
                // Курсор сразу за закрывающей кавычкой — уже вне строки
                bool closed = token.Value.Length > 1 && token.Value[token.Value.Length - 1] == token.Value[0];
                return position < index || !closed;
            }
            return false;
        }
        return false;
    }

    private static bool Matches(string label, string prefix)
    {
        return prefix.Length == 0 || label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static int KindOrder(CompletionKind kind)
    {
        switch (kind)
        {
            case CompletionKind.Variable: return 0;
            case CompletionKind.Command: return 1;
            case CompletionKind.Member: return 1;
            case CompletionKind.Function: return 2;
            case CompletionKind.Snippet: return 3;
            default: return 4;
        }
    }

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}
