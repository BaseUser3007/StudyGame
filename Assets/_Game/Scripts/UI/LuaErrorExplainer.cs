using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

/// <summary>
/// Разобранная ошибка запуска: сырой текст, где она (окно и строка) и объяснение для новичка.
/// </summary>
public sealed class LuaErrorExplanation
{
    /// <summary>Текст ошибки как есть (ExecutionResult.ErrorMessage).</summary>
    public string Raw { get; }
    /// <summary>Окно с ошибкой (Main или заголовок доп. окна); null — неизвестно.</summary>
    public string Window { get; }
    /// <summary>Строка в окне (с 1, уже без строки-обёртки доп. окна); 0 — неизвестна.</summary>
    public int Line { get; private set; }
    /// <summary>Что случилось — простыми словами; null — объяснения нет.</summary>
    public string Explanation { get; }
    /// <summary>Что попробовать; может быть null.</summary>
    public string Suggestion { get; }

    public bool HasExplanation => !string.IsNullOrEmpty(Explanation);
    public bool HasLine => Line > 0 && !string.IsNullOrEmpty(Window);

    /// <summary>«Line 5 in Main» / «In window Helper» / null.</summary>
    public string Location =>
        HasLine ? $"Line {Line} in {Window}"
        : !string.IsNullOrEmpty(Window) ? $"In window {Window}"
        : null;

    public LuaErrorExplanation(string raw, string window, int line, string explanation, string suggestion)
    {
        Raw = raw ?? string.Empty;
        Window = window;
        Line = Math.Max(0, line);
        Explanation = explanation;
        Suggestion = suggestion;
    }

    /// <summary>Только сырой текст — подсказки выключены.</summary>
    public static LuaErrorExplanation RawOnly(string raw) => new LuaErrorExplanation(raw, null, 0, null, null);

    /// <summary>
    /// Строка не дальше последней строки окна (ошибка «на конце файла» у доп. окна указывает
    /// на строку закрывающего end обёртки).
    /// </summary>
    public void ClampLine(int lineCount)
    {
        if (Line > 0 && lineCount > 0 && Line > lineCount) Line = lineCount;
    }
}

/// <summary>
/// Объяснение частых ошибок Lua для тех, кто никогда не программировал (Roadmap, этап 5).
/// Таблица шаблонов (регулярки по реальным текстам Lua-CSharp, EnergyMeter, LuaConstructGuard,
/// LuaSandbox и команд) → короткое объяснение на простом английском и что попробовать.
/// Для опечаток в именах подсказывает ближайшее известное имя (команды из реестра ILuaCommand,
/// методы списка findObjects, доп. окна, стандартные функции Lua) по расстоянию Левенштейна.
/// </summary>
public static class LuaErrorExplainer
{
    // ─── Константы ────────────────────────────────────────────

    private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.Singleline;

    // Префиксы ExecutionResult и Lua-CSharp перед текстом ошибки
    private static readonly Regex PrefixRegex = new Regex(
        @"^\s*(?:(?:Compile|Runtime) error:\s*)?(?:Lua-CSharp:\s*)?", Options);

    // [string "Main"]:5: … — место ошибки от Lua (у доп. окон строка 1 — обёртка function Title(...))
    private static readonly Regex LocationRegex = new Regex(
        @"^\[string\s+""(?<chunk>[^""]*)""\]:(?<line>\d+):\s*", Options);

    // Lua-CSharp печатает ожидаемое ключевое слово кодом токена (символ с кодом 256+) — порядок Lua 5.2
    private static readonly string[] TokenNames =
    {
        "and", "break", "do", "else", "elseif", "end", "false", "for", "function", "goto", "if", "in",
        "local", "nil", "not", "or", "repeat", "return", "then", "true", "until", "while",
        "..", "...", "==", ">=", "<=", "~=", "::", "<eof>", "<number>", "<name>", "<string>"
    };

    // Стандартные функции и библиотеки Lua — кандидаты для «Did you mean» (команды берутся из реестра;
    // print — тоже команда игры, открываемая, поэтому её здесь нет)
    private static readonly string[] LuaGlobals =
    {
        "pairs", "ipairs", "tostring", "tonumber", "type", "pcall", "error", "select", "next",
        "math", "string", "table", "os"
    };

    private static readonly string[] LuaLibraryFunctions =
    {
        "floor", "ceil", "abs", "max", "min", "random", "sqrt", "huge", "pi",
        "insert", "remove", "concat", "sort", "format", "len", "sub", "upper", "lower", "rep", "find",
        "clock", "time", "date"
    };

    // ─── Таблица шаблонов ─────────────────────────────────────

    private delegate (string Explanation, string Suggestion) Rule(Match match, Input input);

    private sealed class Input
    {
        public string Message;
        public string Window;
        public int Line;
        public string MainChunk;
        public int LineOffset;
        public List<string> CommandNames;
        public Dictionary<string, string> Signatures;
        public List<string> WindowNames;
        // Конструкция открыта (null — считаем открытыми все): подсказки не советуют закрытый `if`
        public Func<string, bool> IsConstructUnlocked;

        public bool IsUnlocked(string construct) => IsConstructUnlocked == null || IsConstructUnlocked(construct);
    }

    private static readonly List<(Regex Pattern, Rule Explain)> Rules = new List<(Regex, Rule)>
    {
        // ── Энергия, зависание, переполнение стека ──
        (R(@"out of energy at (?<src>.+?): need (?<need>[\d.,]+), have (?<have>[\d.,]+)"), OutOfEnergy),
        (R(@"blocked the game for more than"), InfiniteLoop),
        (R(@"stack overflow"), StackOverflow),

        // ── Открытия ──
        (R(@"command `(?<name>[^`]+)` is not unlocked yet"), LockedCommand),
        (R(@"are not unlocked yet \(window (?<win>[A-Za-z_][A-Za-z0-9_]*)\)"), LockedClasses),
        (R(@"is not available, use loops instead \((?<chunk>[A-Za-z_][A-Za-z0-9_]*), line (?<line>\d+)\)"), ForbiddenGoto),
        (R(@"^(?<what>.+?) is not unlocked yet \((?<chunk>[A-Za-z_][A-Za-z0-9_]*), line (?<line>\d+)\)"), LockedConstruct),

        // ── Ошибки команд ──
        (R(@"(?:(?<cmd>[A-Za-z_]\w*): )?unknown type '(?<name>[^']*)'(?: \(known: (?<known>[^)]*)\))?"), UnknownType),
        (R(@"bad argument #(?<n>\d+) to '(?<fn>[^']+)' \((?<detail>.*?)\)*$"), BadArgument),
        (R(@"^(?<fn>[A-Za-z_]\w*): usage:? (?<usage>.+)$"), WrongUsage),
        (R(@"^(?<fn>[A-Za-z_]\w*): (?<detail>.+)$"), CommandMisuse),

        // ── Синтаксис ──
        (R(@"unfinished long string"), (m, i) => (
            "A `[[` text is not closed: it needs `]]` at the end.",
            "Add `]]` where the text should end.")),
        (R(@"unfinished long comment"), (m, i) => (
            "A `--[[` comment is not closed, so all the code after it is treated as a comment.",
            "Add `]]` where the comment should end, or use `--` for a one-line comment.")),
        (R(@"unfinished string"), (m, i) => (
            "A text in quotes is not closed: every \" or ' needs a matching closing quote on the same line.",
            "Add the missing quote, for example `move(\"up\", 1)`.")),
        (R(@"'end' expected(?: \(to close '(?<what>[^']+)' at line (?<open>\d+)\))?"), EndExpected),
        (R(@"'=' or 'in' expected"), (m, i) => (
            "A `for` loop needs `=` (counting loop) or `in` (going through a list) after the variable name.",
            "Write it like `for i = 1, 10 do ... end` or `for _, obj in ipairs(list) do ... end`.")),
        (R(@"^(?:'(?<tok>[^']+)'|(?<tok>\S)) expected"), TokenExpected),
        (R(@"malformed number"), (m, i) => (
            "A number is written wrongly, for example `3x` or `1.2.3`.",
            "Put an operator between a number and a name: `3 * x`.")),
        (R(@"function arguments expected"), (m, i) => (
            "After `obj:name` Lua expects brackets with arguments.",
            "Add brackets: `list:getNear()`.")),
        (R(@"unexpected symbol near (?<near>.+)$"), UnexpectedSymbol),
        (R(@"syntax error near (?<near>.+)$"), SyntaxError),

        // ── Ошибки выполнения ──
        (R(@"attempt to call a nil value(?: \((?<kind>\w+) '(?<name>[^']+)'\))?"), CallNil),
        (R(@"attempt to call a (?<type>\w+) value(?: \((?<kind>\w+) '(?<name>[^']+)'\))?"), CallNonFunction),
        (R(@"attempt to index a nil value(?: \((?<kind>\w+) '(?<name>[^']+)'\))?"), IndexNil),
        (R(@"attempt to index a (?<type>\w+) value(?: \((?<kind>\w+) '(?<name>[^']+)'\))?"), IndexNonTable),
        (R(@"attempt to (?:perform arithmetic on|add|sub|mul|div|mod|pow|unm|idiv) a (?<t1>\w+) value(?: \(\w+ '(?<n1>[^']+)'\))?(?: with a (?<t2>\w+) value(?: \(\w+ '(?<n2>[^']+)'\))?)?"), Arithmetic),
        (R(@"attempt to get length of a (?<type>\w+) value(?: \(\w+ '(?<name>[^']+)'\))?"), Length),
        (R(@"attempt to compare (?:two (?<both>\w+) values|a (?<t1>\w+) value with a (?<t2>\w+) value)"), Compare),
        (R(@"attempt to concatenate a (?<t1>\w+) value(?: \(\w+ '(?<n1>[^']+)'\))?(?: with a (?<t2>\w+) value(?: \(\w+ '(?<n2>[^']+)'\))?)?"), Concatenate),
        (R(@"'for' (?<what>initial value|limit|step) must be a number"), (m, i) => (
            $"The {m.Groups["what"].Value} of the `for` loop is not a number (maybe it is nil).",
            "In `for i = 1, 10, 1 do` the start, end and step must be numbers. Check the variables used there.")),
    };

    // ─── Public API ───────────────────────────────────────────

    /// <param name="rawError">ExecutionResult.ErrorMessage.</param>
    /// <param name="commands">Открытые команды (LuaSandbox.Registry) — имена и сигнатуры для подсказок.</param>
    /// <param name="windowNames">Заголовки доп. окон (их функции тоже можно вызвать).</param>
    /// <param name="mainChunkName">Имя чанка главного окна (LuaSandbox.MainChunkName).</param>
    /// <param name="isConstructUnlocked">Открыта ли конструкция (id из LuaConstructs); null — все открыты.</param>
    public static LuaErrorExplanation Explain(
        string rawError,
        IEnumerable<ILuaCommand> commands,
        IEnumerable<string> windowNames = null,
        string mainChunkName = "Main",
        Func<string, bool> isConstructUnlocked = null)
    {
        if (string.IsNullOrWhiteSpace(rawError)) return LuaErrorExplanation.RawOnly(rawError);

        Input input = BuildInput(rawError, commands, windowNames, mainChunkName);
        input.IsConstructUnlocked = isConstructUnlocked;

        foreach (var (pattern, explain) in Rules)
        {
            Match match = pattern.Match(input.Message);
            if (!match.Success) continue;

            // Шаблоны LuaConstructGuard сами несут окно и строку (по исходному коду окна — без сдвига)
            if (match.Groups["chunk"].Success && int.TryParse(match.Groups["line"].Value, out int guardLine))
            {
                input.Window = match.Groups["chunk"].Value;
                input.Line = guardLine;
                input.LineOffset = 0;
            }
            else if (match.Groups["win"].Success)
            {
                input.Window = match.Groups["win"].Value;
            }

            var (explanation, suggestion) = explain(match, input);
            // Правило не подошло по смыслу (например, «имя: текст» не от команды) — ищем дальше
            if (string.IsNullOrEmpty(explanation)) continue;
            return new LuaErrorExplanation(rawError, input.Window, input.Line, explanation, suggestion);
        }

        // Шаблон не найден — хотя бы место ошибки
        return new LuaErrorExplanation(rawError, input.Window, input.Line, null, null);
    }

    /// <summary>
    /// Ближайшее имя из кандидатов (без учёта регистра; перестановка соседних букв — одна правка).
    /// null — ничего достаточно близкого. Само имя (с тем же регистром) не предлагается.
    /// </summary>
    public static string FindClosest(string name, IEnumerable<string> candidates)
    {
        if (string.IsNullOrEmpty(name) || candidates == null) return null;

        string lower = name.ToLowerInvariant();
        int limit = name.Length <= 3 ? 1 : name.Length <= 6 ? 2 : 3;
        string best = null;
        int bestDistance = int.MaxValue;

        foreach (string candidate in candidates)
        {
            if (string.IsNullOrEmpty(candidate) || candidate == name) continue;

            int distance = Distance(lower, candidate.ToLowerInvariant());
            if (distance > limit || distance >= bestDistance) continue;

            best = candidate;
            bestDistance = distance;
        }
        return best;
    }

    /// <summary>
    /// Расстояние Левенштейна с перестановкой соседних символов (optimal string alignment):
    /// «mvoe» → «move» — одна правка.
    /// </summary>
    public static int Distance(string a, string b)
    {
        a ??= string.Empty;
        b ??= string.Empty;
        int[,] d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                int value = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    value = Math.Min(value, d[i - 2, j - 2] + 1);
                d[i, j] = value;
            }
        }
        return d[a.Length, b.Length];
    }

    // ─── Разбор ───────────────────────────────────────────────

    private static Regex R(string pattern) => new Regex(pattern, Options);

    private static Input BuildInput(
        string rawError, IEnumerable<ILuaCommand> commands, IEnumerable<string> windowNames, string mainChunkName)
    {
        var input = new Input
        {
            MainChunk = string.IsNullOrEmpty(mainChunkName) ? "Main" : mainChunkName,
            CommandNames = new List<string>(),
            Signatures = new Dictionary<string, string>(StringComparer.Ordinal),
            WindowNames = new List<string>()
        };

        if (commands != null)
        {
            foreach (ILuaCommand command in commands)
            {
                if (command == null || string.IsNullOrEmpty(command.Name)) continue;
                input.CommandNames.Add(command.Name);
                input.Signatures[command.Name] = command.Signature;
            }
        }
        if (windowNames != null)
            foreach (string window in windowNames)
                if (!string.IsNullOrEmpty(window)) input.WindowNames.Add(window);

        string message = PrefixRegex.Replace(rawError.Trim(), string.Empty, 1);
        Match location = LocationRegex.Match(message);
        if (location.Success)
        {
            input.Window = location.Groups["chunk"].Value;
            // Доп. окно выполняется как "function Title(...)\n<код>\nend" — первая строка кода окна вторая в чанке
            input.LineOffset = input.Window == input.MainChunk ? 0 : 1;
            int.TryParse(location.Groups["line"].Value, out int line);
            input.Line = Math.Max(1, line - input.LineOffset);
            message = message.Substring(location.Length);
        }

        input.Message = message;
        return input;
    }

    /// <summary>Строка чанка → строка окна (0 — строка обёртки доп. окна).</summary>
    private static int ToWindowLine(Input input, int chunkLine) => chunkLine - input.LineOffset;

    // ─── Объяснения: энергия, зависание, открытия ─────────────

    private static (string, string) OutOfEnergy(Match m, Input i) => (
        $"Your energy ran out at {m.Groups["src"].Value}: it needed {m.Groups["need"].Value}, " +
        $"but only {m.Groups["have"].Value} was left. Every command call, loop pass and condition check costs energy.",
        "Look at the cost next to each line and do fewer steps or loop passes. End Day restores energy for tomorrow.");

    private static (string, string) InfiniteLoop(Match m, Input i) => (
        "The code ran too long without a pause, so the game stopped it. " +
        "Usually this is a loop that never ends, like `while true do` with nothing that stops it.",
        "Make sure the loop condition becomes false at some point, or leave the loop with `break`.");

    private static (string, string) StackOverflow(Match m, Input i) => (
        "A function keeps calling itself (or two functions call each other) and never stops, so Lua ran out of space.",
        "Add a condition that ends the calls, for example `if n <= 0 then return end` at the start of the function.");

    private static (string, string) LockedCommand(Match m, Input i) => (
        $"The command `{m.Groups["name"].Value}` exists, but you haven't unlocked it yet.",
        "Open Achievements to see which goal unlocks it. Until then, use the commands listed in Help.");

    private static (string, string) LockedClasses(Match m, Input i) => (
        $"Extra code windows (classes) aren't unlocked yet, but the window `{m.Groups["win"].Value}` has code in it.",
        "Move that code into Main or clear the window. Achievements unlock class windows later.");

    private static (string, string) ForbiddenGoto(Match m, Input i) => (
        "`goto` and `::labels::` are not part of this game: they would let the code repeat for free.",
        "Use a loop instead: `for`, `while` or `repeat`.");

    private static (string, string) LockedConstruct(Match m, Input i) => (
        $"Your code uses {m.Groups["what"].Value}, which you haven't unlocked yet.",
        "Rewrite this part without it (Help shows what you can use), or complete achievements to unlock it.");

    // ─── Объяснения: команды ──────────────────────────────────

    private static (string, string) UnknownType(Match m, Input i)
    {
        string command = m.Groups["cmd"].Success ? m.Groups["cmd"].Value : "findObjects";
        string name = m.Groups["name"].Value;
        var known = new List<string>();
        if (m.Groups["known"].Success)
            foreach (string part in m.Groups["known"].Value.Split(','))
                if (part.Trim().Length > 0) known.Add(part.Trim());

        string closest = FindClosest(name, known);
        string suggestion = closest != null
            ? $"Did you mean \"{closest}\"? Write it like `{command}(\"{closest}\")`."
            : known.Count > 0
                ? $"Type names are lowercase words, for example `{command}(\"{known[0]}\")`."
                : "Check the type name in Help.";
        return ($"`{command}` doesn't know the type \"{name}\".", suggestion);
    }

    private static (string, string) BadArgument(Match m, Input i)
    {
        string fn = m.Groups["fn"].Value;
        string number = m.Groups["n"].Value;
        string detail = m.Groups["detail"].Value.Trim();

        string explanation = detail == "value expected" || detail.EndsWith("got no value", StringComparison.Ordinal)
            ? $"`{fn}` needs more arguments: argument {number} is missing."
            : $"Argument {number} of `{fn}` has the wrong kind of value ({detail}).";
        return (explanation, UsageHint(fn, i, "Check the order and the kinds of arguments in Help."));
    }

    private static (string, string) WrongUsage(Match m, Input i)
    {
        string fn = m.Groups["fn"].Value;
        return ($"`{fn}` was called with arguments it doesn't understand.",
            $"Correct forms: {m.Groups["usage"].Value.Trim()}");
    }

    /// <summary>
    /// «имя: текст» от команды ("check: distance must be …") или метода списка findObjects
    /// ("getMin: field name expected …"). Другие имена — не это правило.
    /// </summary>
    private static (string, string) CommandMisuse(Match m, Input i)
    {
        string fn = m.Groups["fn"].Value;
        string detail = m.Groups["detail"].Value.Trim().TrimEnd('.');

        string method = FindListMethodSignature(fn);
        if (method != null)
            return ($"The list method `{fn}` got a wrong argument: {detail}.", $"Write it like `list:{method}`.");

        if (i.Signatures.TryGetValue(fn, out string signature))
            return ($"`{fn}` couldn't run: {detail}.",
                string.IsNullOrEmpty(signature) ? "Check the arguments in Help." : $"Write it like `{signature}`.");

        return (null, null);
    }

    private static string UsageHint(string fn, Input i, string fallback)
    {
        if (i.Signatures.TryGetValue(fn, out string signature) && !string.IsNullOrEmpty(signature))
            return $"Write it like `{signature}`.";
        string method = FindListMethodSignature(fn);
        return method != null ? $"Write it like `list:{method}`." : fallback;
    }

    private static string FindListMethodSignature(string name)
    {
        foreach (var method in LuaObjectList.Methods)
            if (method.Name == name) return method.Signature;
        return null;
    }

    // ─── Объяснения: синтаксис ────────────────────────────────

    private static (string, string) EndExpected(Match m, Input i)
    {
        string explanation = "A block is not closed: every `if`, `for`, `while` and `function` needs its own `end`.";
        if (m.Groups["what"].Success && int.TryParse(m.Groups["open"].Value, out int open))
        {
            int line = ToWindowLine(i, open);
            // У доп. окна строка 1 чанка — обёртка function Title(...): её end «съел» блок внутри окна
            if (line >= 1)
                explanation += $" The `{m.Groups["what"].Value}` on line {line} has no `end`.";
        }
        return (explanation,
            "Add `end` where the block should finish. Indenting the code inside a block helps to see where it ends.");
    }

    private static (string, string) TokenExpected(Match m, Input i)
    {
        string token = DecodeToken(m.Groups["tok"].Value);
        switch (token)
        {
            case "then":
                return ("After the condition of `if` (or `elseif`) Lua needs the word `then`.",
                    "Write it like `if x > 3 then ... end`. To compare use `==` (equal) and `~=` (not equal), not `=` or `!=`.");
            case "do":
                return ("After `for ...` or `while ...` Lua needs the word `do`.",
                    "Write it like `for i = 1, 3 do ... end` or `while x < 3 do ... end`.");
            case "=":
                return ("Lua expected `=` here.",
                    "To store a value write `name = value`. To call a command add brackets: `harvest()`.");
            case ")":
                return ("A bracket `(` is not closed, or a comma between arguments is missing.",
                    "Separate arguments with commas, like `moveTo(3, 4)`, and close every `(` with `)`.");
            case "}":
                return ("A list `{` is not closed, or a comma between its items is missing.",
                    "Write it like `{1, 2, 3}`.");
            case "]":
                return ("A `[` is not closed with `]`.", "Write it like `list[1]`.");
            case ",":
                return ("Lua expected a comma here. A counting `for` loop needs a start and an end separated by a comma.",
                    "Write it like `for i = 1, 10 do ... end`.");
            case "until":
                return ("A `repeat` loop needs `until condition` at the end.",
                    "Write it like `repeat ... until x > 3`.");
            case "end":
                return ("A block is not closed: every `if`, `for`, `while` and `function` needs its own `end`.",
                    "Add `end` where the block should finish.");
            case "<eof>":
                return ("There is something extra after the code has ended: usually an extra `end`, " +
                        "or code after `return` / `break` in the same block.",
                    "Remove the extra `end` (one per `if`, `for`, `while` and `function`). " +
                    "`return` and `break` must be the last line of their block.");
            case "<name>":
                return ("Lua expected a name here: letters, digits and `_`, not starting with a digit and not a Lua word.",
                    "For example `local count = 0` or `function Helper()` — a function needs a name.");
            default:
                return ($"Lua expected `{token}` here.", "Compare this line with the examples in Help.");
        }
    }

    private static (string, string) UnexpectedSymbol(Match m, Input i)
    {
        string near = m.Groups["near"].Value.Trim();
        if (near.StartsWith("'!", StringComparison.Ordinal))
            return ("Lua doesn't use `!`.", "\"Not equal\" is written `~=`, and \"not\" is written `not`.");
        if (near == "<eof>")
            return ("The code ends too early: something is missing, for example after `=` or `+` (`x = 1 +`).",
                "Finish the expression or remove the extra operator.");

        return ($"Lua found {near} where it didn't expect it.",
            "Check for a missing comma between arguments (`move(\"up\", 1)`), missing quotes around text, " +
            "or an extra bracket.");
    }

    private static (string, string) SyntaxError(Match m, Input i) => (
        "Lua couldn't understand this line. Often a word stands on its own: a command name without brackets, " +
        "or a value that isn't stored or used.",
        "Commands need brackets: `harvest()`, not `harvest`. To store a value write `x = value`.");

    private static string DecodeToken(string token)
    {
        if (token.Length == 1 && token[0] >= 256 && token[0] - 256 < TokenNames.Length)
            return TokenNames[token[0] - 256];
        return token;
    }

    // ─── Объяснения: выполнение ───────────────────────────────

    private static (string, string) CallNil(Match m, Input i)
    {
        if (!m.Groups["name"].Success)
            return ("Your code called something that doesn't exist (it is nil).",
                "Check the spelling of command and function names in Help.");

        string name = m.Groups["name"].Value;
        string kind = m.Groups["kind"].Value;

        var candidates = new List<string>();
        if (kind == "method" || kind == "field")
        {
            foreach (var method in LuaObjectList.Methods) candidates.Add(method.Name);
            candidates.AddRange(LuaLibraryFunctions);
        }
        candidates.AddRange(i.CommandNames);
        candidates.AddRange(i.WindowNames);
        if (kind != "method") candidates.AddRange(LuaGlobals);

        string closest = FindClosest(name, candidates);
        string explanation = $"`{name}` is not a command or function that Lua knows, so it can't be called.";

        string suggestion;
        if (closest != null)
        {
            suggestion = string.Equals(closest, name, StringComparison.OrdinalIgnoreCase)
                ? $"Did you mean `{closest}`? Names are case-sensitive."
                : $"Did you mean `{closest}`?";
            if (i.Signatures.TryGetValue(closest, out string signature) && !string.IsNullOrEmpty(signature))
                suggestion += $" Write it like `{signature}`.";
        }
        else
        {
            suggestion = "Check the spelling in Help. Names are case-sensitive: `moveTo`, not `moveto`.";
        }
        return (explanation, suggestion);
    }

    private static (string, string) CallNonFunction(Match m, Input i)
    {
        string name = m.Groups["name"].Success ? $"`{m.Groups["name"].Value}`" : "This value";
        return ($"{name} is a {m.Groups["type"].Value}, not a function, so it can't be called with `( )`.",
            "Maybe a variable has the same name as a command or function — give the variable another name.");
    }

    private static (string, string) IndexNil(Match m, Input i)
    {
        string name = m.Groups["name"].Success ? m.Groups["name"].Value : null;
        string what = name != null ? $"`{name}` is nil (empty)" : "A value is nil (empty)";
        string explanation = $"{what}, so you can't use `.` or `:` on it. This often happens when a command found nothing: " +
                             "`getNear()`, `find()` or `check()` returned nil.";

        // Пока `if` закрыт, совет «проверь через if» бесполезен — подсказываем, что делать без условий
        if (!i.IsUnlocked(LuaConstructs.If))
            return (explanation,
                "Make sure something is in sight before you look for it (`findObjects(...).count` shows how many), " +
                "or explore first. Conditions (`if`) unlock after your first harvest.");

        return (explanation,
            name != null
                ? $"Check it before use: `if {name} then ... end`. Also check the spelling of the variable."
                : "Check the value before use: `if obj then ... end`.");
    }

    private static (string, string) IndexNonTable(Match m, Input i)
    {
        string name = m.Groups["name"].Success ? $"`{m.Groups["name"].Value}`" : "This value";
        return ($"{name} is a {m.Groups["type"].Value}, not a table, so it has no fields to read with `.`.",
            "Fields of found objects are plain values: write `obj.x`, not `obj.x.y`.");
    }

    private static (string, string) Arithmetic(Match m, Input i)
    {
        string t1 = m.Groups["t1"].Value;
        string t2 = m.Groups["t2"].Success ? m.Groups["t2"].Value : null;
        string bad = t1 != "number" ? t1 : t2 ?? t1;
        string name = t1 != "number" ? Name(m, "n1") : Name(m, "n2");

        switch (bad)
        {
            case "nil":
                return ($"You did math with {name ?? "a value"} that is nil (has no value). " +
                        "Maybe the name is misspelled, it was never set, or a command returned nothing.",
                    "Give it a value first (`local count = 0`) or check it: `if x then ... end`.");
            case "string":
                return ($"You did math with text{(name != null ? $" ({name})" : "")}. Numbers in quotes are text: \"5\" is not 5.",
                    "Remove the quotes or convert the text: `tonumber(\"5\")`.");
            default:
                return ($"You did math with a {bad} value{(name != null ? $" ({name})" : "")}. Only numbers can be added, subtracted or multiplied.",
                    "If it is a found object, use its number field: `obj.x`, `obj.distance`.");
        }
    }

    private static (string, string) Length(Match m, Input i) => (
        $"`#` gives the length of a list or text, but this value is a {m.Groups["type"].Value}.",
        "Check that the list exists. For findObjects results you can also use `list.count`.");

    private static (string, string) Compare(Match m, Input i)
    {
        bool hasNil = m.Groups["both"].Value == "nil" || m.Groups["t1"].Value == "nil" || m.Groups["t2"].Value == "nil";
        if (hasNil)
            return ("One side of the comparison (`<`, `>`, `<=`, `>=`) is nil — it has no value, so Lua can't compare it.",
                "Maybe a name is misspelled or a command returned nil. Check first: `if x and x > 3 then ... end`.");

        string a = m.Groups["both"].Success ? m.Groups["both"].Value : m.Groups["t1"].Value;
        string b = m.Groups["both"].Success ? m.Groups["both"].Value : m.Groups["t2"].Value;
        return ($"You compared a {a} with a {b}. `<` and `>` work only for two numbers (or two texts).",
            "Compare a field instead, for example `obj.distance < 5`.");
    }

    private static (string, string) Concatenate(Match m, Input i)
    {
        string t1 = m.Groups["t1"].Value;
        string t2 = m.Groups["t2"].Success ? m.Groups["t2"].Value : null;
        string bad = t1 != "string" && t1 != "number" ? t1 : t2 ?? t1;

        if (bad == "nil")
            return ("`..` joins texts, but one of the parts is nil (has no value).",
                "Wrap the value in `tostring(...)`, for example `\"x = \" .. tostring(x)`.");
        return ($"`..` joins texts and numbers, but one of the parts is a {bad}.",
            "Join a field instead, for example `\"found \" .. obj.type`.");
    }

    private static string Name(Match m, string group) =>
        m.Groups[group].Success ? $"`{m.Groups[group].Value}`" : null;
}
