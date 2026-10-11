using System.Collections.Generic;

public class LuaTokenizer
{
    // ─── Типы токенов ─────────────────────────────────────────

    public enum TokenType
    {
        Keyword,
        Identifier,
        Number,
        String,
        Comment,
        Operator,
        Whitespace,
        Newline,
        Unknown
    }

    // ─── Токен ────────────────────────────────────────────────

    public readonly struct Token
    {
        public readonly string Value;
        public readonly TokenType Type;
        public readonly int Line;
        public readonly int Column;

        public Token(string value, TokenType type, int line, int column)
        {
            Value = value;
            Type = type;
            Line = line;
            Column = column;
        }

        public override string ToString() => $"[{Type}] '{Value}' ({Line}:{Column})";
    }

    // ─── Ключевые слова Lua ───────────────────────────────────

    private static readonly HashSet<string> Keywords = new HashSet<string>
    {
        "if", "then", "else", "elseif", "end",
        "for", "while", "do", "repeat", "until",
        "function", "local", "return", "break",
        "and", "or", "not", "in", "nil",
        "true", "false"
    };

    // ─── Tokenize ─────────────────────────────────────────────

    public List<Token> Tokenize(string code)
    {
        List<Token> tokens = new List<Token>();

        if (string.IsNullOrEmpty(code))
            return tokens;

        int pos = 0;
        int line = 1;
        int column = 1;

        while (pos < code.Length)
        {
            // Перенос строки
            if (code[pos] == '\n')
            {
                tokens.Add(new Token("\n", TokenType.Newline, line, column));
                line++;
                column = 1;
                pos++;
                continue;
            }

            // Пробельные символы
            if (char.IsWhiteSpace(code[pos]))
            {
                int start = pos;
                while (pos < code.Length && char.IsWhiteSpace(code[pos]) && code[pos] != '\n')
                {
                    pos++;
                    column++;
                }
                tokens.Add(new Token(code.Substring(start, pos - start), TokenType.Whitespace, line, column));
                continue;
            }

            // Блочный комментарий --[[ ... ]] / --[==[ ... ]==] — может занимать несколько строк
            if (pos + 1 < code.Length && code[pos] == '-' && code[pos + 1] == '-'
                && LongBracketLevel(code, pos + 2) >= 0)
            {
                int end = FindLongBracketEnd(code, pos + 2, LongBracketLevel(code, pos + 2));
                AddMultilineToken(tokens, code.Substring(pos, end - pos), TokenType.Comment, ref line, ref column);
                pos = end;
                continue;
            }

            // Комментарий -- до конца строки
            if (pos + 1 < code.Length && code[pos] == '-' && code[pos + 1] == '-')
            {
                int startCol = column;
                int end = code.IndexOf('\n', pos);
                end = end == -1 ? code.Length : end;
                string comment = code.Substring(pos, end - pos);
                tokens.Add(new Token(comment, TokenType.Comment, line, startCol));
                column += comment.Length;
                pos = end;
                continue;
            }

            // Длинная строка [[ ... ]] / [==[ ... ]==]
            if (code[pos] == '[' && LongBracketLevel(code, pos) >= 0)
            {
                int end = FindLongBracketEnd(code, pos, LongBracketLevel(code, pos));
                AddMultilineToken(tokens, code.Substring(pos, end - pos), TokenType.String, ref line, ref column);
                pos = end;
                continue;
            }

            // Строка "..." или '...'
            if (code[pos] == '"' || code[pos] == '\'')
            {
                int startCol = column;
                char quote = code[pos];
                int end = pos + 1;

                while (end < code.Length && code[end] != quote)
                {
                    if (code[end] == '\\') end++;
                    end++;
                }
                end = end < code.Length ? end + 1 : end;

                string str = code.Substring(pos, end - pos);
                tokens.Add(new Token(str, TokenType.String, line, startCol));
                column += str.Length;
                pos = end;
                continue;
            }

            // Число
            if (char.IsDigit(code[pos]) ||
               (code[pos] == '.' && pos + 1 < code.Length && char.IsDigit(code[pos + 1])))
            {
                int startCol = column;
                int start = pos;

                while (pos < code.Length && (char.IsDigit(code[pos]) || code[pos] == '.'))
                {
                    pos++;
                    column++;
                }

                tokens.Add(new Token(code.Substring(start, pos - start), TokenType.Number, line, startCol));
                continue;
            }

            // Идентификатор или ключевое слово
            if (char.IsLetter(code[pos]) || code[pos] == '_')
            {
                int startCol = column;
                int start = pos;

                while (pos < code.Length && (char.IsLetterOrDigit(code[pos]) || code[pos] == '_'))
                {
                    pos++;
                    column++;
                }

                string word = code.Substring(start, pos - start);
                TokenType type = Keywords.Contains(word) ? TokenType.Keyword : TokenType.Identifier;
                tokens.Add(new Token(word, type, line, startCol));
                continue;
            }

            // Оператор или любой другой символ
            tokens.Add(new Token(code[pos].ToString(), TokenType.Operator, line, column));
            pos++;
            column++;
        }

        return tokens;
    }

    // ─── Длинные скобки ───────────────────────────────────────

    /// <summary>
    /// Если в pos начинается открывающая длинная скобка [[ или [==[ — её уровень (число '='), иначе -1.
    /// </summary>
    private static int LongBracketLevel(string code, int pos)
    {
        if (pos >= code.Length || code[pos] != '[') return -1;

        int level = 0;
        int i = pos + 1;
        while (i < code.Length && code[i] == '=')
        {
            level++;
            i++;
        }

        return i < code.Length && code[i] == '[' ? level : -1;
    }

    /// <summary>
    /// Позиция сразу после закрывающей скобки ]] / ]==] нужного уровня (или конец кода, если её нет).
    /// </summary>
    private static int FindLongBracketEnd(string code, int openPos, int level)
    {
        string closing = "]" + new string('=', level) + "]";
        int contentStart = openPos + level + 2;
        int end = code.IndexOf(closing, contentStart, System.StringComparison.Ordinal);
        return end == -1 ? code.Length : end + closing.Length;
    }

    /// <summary>
    /// Токен, который может содержать переводы строк: номер строки и колонка
    /// следующих токенов сдвигаются, как если бы переводы были отдельными токенами.
    /// </summary>
    private static void AddMultilineToken(List<Token> tokens, string value, TokenType type, ref int line, ref int column)
    {
        tokens.Add(new Token(value, type, line, column));

        int lastNewline = value.LastIndexOf('\n');
        if (lastNewline < 0)
        {
            column += value.Length;
            return;
        }

        foreach (char c in value)
            if (c == '\n') line++;
        column = value.Length - lastNewline;
    }
}