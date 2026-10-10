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

            // Комментарий --
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
}