using System;
using System.Collections.Generic;

/// <summary>
/// Открываемые конструкции языка: id (хранится в MetaProgress и UnlockConfig), читаемое имя
/// для сообщений и ключевые слова, которые конструкция покрывает.
/// Не закрываются никогда: break (разрешён в любом открытом цикле), return, and/or/not, do,
/// вызовы функций и обычные выражения. Запрещены навсегда: goto и метки ::name:: — с ними цикл
/// получился бы без открытого цикла и без платы за итерации.
/// </summary>
public static class LuaConstructs
{
    public const string Local = "local";
    /// <summary>if / elseif / else.</summary>
    public const string If = "if";
    public const string For = "for";
    public const string While = "while";
    /// <summary>repeat ... until.</summary>
    public const string Repeat = "repeat";
    /// <summary>Объявление функций (именованных и анонимных).</summary>
    public const string Function = "function";
    /// <summary>Доп. окна-«классы» (глобальные функции по заголовку окна). Ключевых слов нет.</summary>
    public const string Classes = "classes";

    public readonly struct Info
    {
        public readonly string Id;
        /// <summary>Читаемое имя для сообщений игроку, например "loop `for`".</summary>
        public readonly string DisplayName;
        /// <summary>Ключевые слова Lua, которые закрыты вместе с конструкцией.</summary>
        public readonly IReadOnlyList<string> Keywords;

        public Info(string id, string displayName, params string[] keywords)
        {
            Id = id;
            DisplayName = displayName;
            Keywords = keywords;
        }
    }

    /// <summary>Все открываемые конструкции в порядке прогрессии (для справки и UI).</summary>
    public static readonly IReadOnlyList<Info> All = new[]
    {
        new Info(Local, "variable `local`", "local"),
        new Info(If, "condition `if`", "if", "elseif", "else"),
        new Info(For, "loop `for`", "for"),
        new Info(While, "loop `while`", "while"),
        new Info(Repeat, "loop `repeat`", "repeat", "until"),
        new Info(Function, "function definition `function`", "function"),
        new Info(Classes, "class windows")
    };

    private static readonly Dictionary<string, Info> ById = BuildById();
    private static readonly Dictionary<string, string> ByKeyword = BuildByKeyword();

    // ─── Public API ───────────────────────────────────────────

    public static bool IsKnown(string id) => id != null && ById.ContainsKey(id);

    public static bool TryGet(string id, out Info info)
    {
        if (id != null && ById.TryGetValue(id, out info)) return true;
        info = default;
        return false;
    }

    /// <summary>
    /// Id конструкции, к которой относится ключевое слово (null — слово не закрывается).
    /// </summary>
    public static string GetConstructForKeyword(string keyword)
    {
        return keyword != null && ByKeyword.TryGetValue(keyword, out string id) ? id : null;
    }

    public static string GetDisplayName(string id)
    {
        return TryGet(id, out Info info) ? info.DisplayName : id;
    }

    /// <summary>Ключевое слово запрещено навсегда (не открывается): goto.</summary>
    public const string GotoKeyword = "goto";

    /// <summary>Запрещённые навсегда ключевые слова — для справки и подсветки.</summary>
    public static readonly IReadOnlyList<string> ForbiddenKeywords = new[] { GotoKeyword };

    public static bool IsForbiddenKeyword(string keyword) => keyword == GotoKeyword;

    // ─── Helpers ──────────────────────────────────────────────

    private static Dictionary<string, Info> BuildById()
    {
        var map = new Dictionary<string, Info>(StringComparer.Ordinal);
        foreach (Info info in All)
            map[info.Id] = info;
        return map;
    }

    private static Dictionary<string, string> BuildByKeyword()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Info info in All)
            foreach (string keyword in info.Keywords)
                map[keyword] = info.Id;
        return map;
    }
}
