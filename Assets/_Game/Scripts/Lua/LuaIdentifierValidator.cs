using System.Collections.Generic;
using System.Text.RegularExpressions;

public class LuaIdentifierValidator : IIdentifierValidator
{
    private static readonly HashSet<string> ReservedWords = new HashSet<string>
    {
        "and", "break", "do", "else", "elseif", "end",
        "false", "for", "function", "if", "in",
        "local", "nil", "not", "or", "repeat",
        "return", "then", "true", "until", "while"
    };

    private static readonly Regex ValidPattern = new Regex(@"^[a-zA-Z_][a-zA-Z0-9_]*$");

    public bool IsValid(string identifier)
    {
        if (string.IsNullOrEmpty(identifier))
            return false;

        if (!ValidPattern.IsMatch(identifier))
            return false;

        if (ReservedWords.Contains(identifier))
            return false;

        return true;
    }

    public string GetErrorMessage(string identifier)
    {
        if (string.IsNullOrEmpty(identifier))
            return "Name cannot be empty";

        if (char.IsDigit(identifier[0]))
            return "Name cannot start with a digit";

        if (!ValidPattern.IsMatch(identifier))
            return "Only letters, digits and underscores are allowed";

        if (ReservedWords.Contains(identifier))
            return $"'{identifier}' is a reserved Lua keyword";

        return string.Empty;
    }
}