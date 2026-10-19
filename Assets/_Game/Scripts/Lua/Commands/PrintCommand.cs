using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lua;

/// <summary>
/// print(...) — вывод значений в лог под главным окном (через LuaSandbox.OnLogMessage).
/// Заменяет стандартный print Lua, который писал в консоль Unity, невидимую игроку.
/// Аргументы — через табуляцию; таблицы раскрываются на один уровень ({a=1, b="x"} / {1, 2, 3}),
/// у списка findObjects — count и первые <see cref="ListPreviewItems"/> элемента; длина строки
/// ограничена <see cref="MaxLength"/> символами. Открывается достижением first_bug.
/// </summary>
public class PrintCommand : LuaCommandBase
{
    /// <summary>Максимальная длина одной выведенной строки (дальше — «…»).</summary>
    public const int MaxLength = 200;
    /// <summary>Сколько элементов списка findObjects показывать.</summary>
    public const int ListPreviewItems = 3;

    private const string Ellipsis = "…";

    public override string Name => "print";
    public override float EnergyCost => 0.5f;
    public override string Signature => "print(...)";
    public override string Description => "Выводит значения в лог под главным окном: числа, текст, nil, " +
                                          "таблицы с полями (на один уровень), у списка findObjects — count и первые " +
                                          $"{ListPreviewItems} элемента. Строка не длиннее {MaxLength} символов. " +
                                          "Внутри цикла платится на каждом проходе";

    private readonly Action<string> _output;

    /// <param name="output">Куда отдать готовую строку (LuaSandbox → OnLogMessage).</param>
    public PrintCommand(Action<string> output)
    {
        _output = output;
    }

    protected override ValueTask<int> ExecuteAsync(LuaFunctionExecutionContext context, CancellationToken ct)
    {
        var values = new LuaValue[context.ArgumentCount];
        for (int i = 0; i < values.Length; i++)
            values[i] = context.GetArgument(i);

        _output?.Invoke(Format(values));
        return new ValueTask<int>(0);
    }

    // ─── Public API ───────────────────────────────────────────

    /// <summary>
    /// Строка, которую выведет print с этими аргументами (без вызова — для тестов и UI).
    /// </summary>
    public static string Format(IReadOnlyList<LuaValue> values)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < values.Count && sb.Length <= MaxLength; i++)
        {
            if (i > 0) sb.Append('\t');
            // Строки верхнего уровня — без кавычек, как в стандартном print
            AppendValue(sb, values[i], depth: 0, quoteStrings: false);
        }

        return Truncate(sb.ToString());
    }

    // ─── Форматирование ───────────────────────────────────────

    private static string Truncate(string text)
    {
        return text.Length <= MaxLength ? text : text.Substring(0, MaxLength - Ellipsis.Length) + Ellipsis;
    }

    /// <summary>depth — уровень вложенности: таблицы раскрываются только на нулевом.</summary>
    private static void AppendValue(StringBuilder sb, LuaValue value, int depth, bool quoteStrings)
    {
        switch (value.Type)
        {
            case LuaValueType.Nil:
                sb.Append("nil");
                break;
            case LuaValueType.Boolean:
                sb.Append(value.ToBoolean() ? "true" : "false");
                break;
            case LuaValueType.Number:
                sb.Append(FormatNumber(value.Read<double>()));
                break;
            case LuaValueType.String:
                string text = value.Read<string>();
                if (quoteStrings) sb.Append('"').Append(text).Append('"');
                else sb.Append(text);
                break;
            case LuaValueType.Table:
                LuaTable table = value.Read<LuaTable>();
                if (depth > 0) sb.Append("{...}");
                else if (IsObjectList(table)) AppendObjectList(sb, table);
                else AppendTable(sb, table, depth);
                break;
            case LuaValueType.Function:
                sb.Append("function");
                break;
            default:
                sb.Append(value.Type.ToString().ToLowerInvariant());
                break;
        }
    }

    /// <summary>Целые — без дробной части, остальные — до 6 значащих цифр, всегда с точкой.</summary>
    private static string FormatNumber(double number)
    {
        if (double.IsNaN(number)) return "nan";
        if (double.IsPositiveInfinity(number)) return "inf";
        if (double.IsNegativeInfinity(number)) return "-inf";
        if (Math.Abs(number) < 1e15 && Math.Floor(number) == number)
            return ((long)number).ToString(CultureInfo.InvariantCulture);
        // Экспонента — как в Lua: 1e+20, а не 1E+20
        return number.ToString("G6", CultureInfo.InvariantCulture).Replace('E', 'e');
    }

    /// <summary>
    /// Таблица на один уровень: сначала элементы массива, затем поля (вложенные таблицы — «{...}»).
    /// </summary>
    private static void AppendTable(StringBuilder sb, LuaTable table, int depth)
    {
        sb.Append('{');
        bool first = true;
        int arrayLength = table.ArrayLength;

        for (int i = 1; i <= arrayLength && sb.Length <= MaxLength; i++)
        {
            if (!first) sb.Append(", ");
            first = false;
            AppendValue(sb, table[i], depth + 1, quoteStrings: true);
        }

        foreach (KeyValuePair<LuaValue, LuaValue> pair in table)
        {
            if (sb.Length > MaxLength) break;
            if (IsArrayKey(pair.Key, arrayLength)) continue;

            if (!first) sb.Append(", ");
            first = false;
            AppendKey(sb, pair.Key);
            AppendValue(sb, pair.Value, depth + 1, quoteStrings: true);
        }

        sb.Append('}');
    }

    /// <summary>
    /// Список findObjects: count и первые элементы (их поля — на один уровень), методы не выводятся.
    /// </summary>
    private static void AppendObjectList(StringBuilder sb, LuaTable list)
    {
        int count = list["count"].TryRead(out double raw) ? (int)raw : list.ArrayLength;
        sb.Append("list(count=").Append(count).Append(") {");

        int shown = Math.Min(count, ListPreviewItems);
        for (int i = 1; i <= shown && sb.Length <= MaxLength; i++)
        {
            if (i > 1) sb.Append(", ");
            LuaValue item = list[i];
            if (item.TryRead(out LuaTable element))
                AppendTable(sb, element, 1);
            else
                AppendValue(sb, item, 1, quoteStrings: true);
        }

        if (count > shown) sb.Append(shown > 0 ? ", ..." : "...");
        sb.Append('}');
    }

    private static void AppendKey(StringBuilder sb, LuaValue key)
    {
        if (key.TryRead(out string name) && IsIdentifier(name))
        {
            sb.Append(name).Append('=');
            return;
        }

        sb.Append('[');
        AppendValue(sb, key, 1, quoteStrings: true);
        sb.Append("]=");
    }

    /// <summary>Список findObjects: есть count и метод getNear (методы лежат прямо в таблице).</summary>
    private static bool IsObjectList(LuaTable table)
    {
        return table["count"].Type == LuaValueType.Number
            && table["getNear"].Type == LuaValueType.Function;
    }

    private static bool IsArrayKey(LuaValue key, int arrayLength)
    {
        if (!key.TryRead(out double index)) return false;
        return index >= 1 && index <= arrayLength && Math.Floor(index) == index;
    }

    private static bool IsIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (!(char.IsLetter(name[0]) || name[0] == '_')) return false;
        foreach (char c in name)
            if (!(char.IsLetterOrDigit(c) || c == '_')) return false;
        return true;
    }
}
