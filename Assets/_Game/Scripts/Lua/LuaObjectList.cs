using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lua;
using UnityEngine;

/// <summary>
/// Массив объектов, который возвращает findObjects: элементы result[1..count] и методы
/// для поиска, сравнения, фильтрации и сортировки. Методы, возвращающие список
/// (where, sortBy, take), создают новый такой же массив — их можно выстраивать в цепочку:
///   findObjects("stone"):where("distance", "<", 5):sortBy("durability"):take(3)
///
/// Объекты, которые уже собраны, методы пропускают. Расстояние считается на момент вызова
/// метода (поле distance у объектов в новом списке обновляется).
/// Каждый вызов метода стоит <see cref="MethodCost"/> энергии.
/// </summary>
public class LuaObjectList
{
    public const float MethodCost = 1f;

    /// <summary>
    /// Методы массива — для справки, подсветки, автодополнения и подсчёта стоимости в редакторе.
    /// </summary>
    public static readonly IReadOnlyList<(string Name, string Signature, string Description)> Methods = new[]
    {
        ("getNear",   "getNear()",                    "ближайший объект"),
        ("getFar",    "getFar()",                     "самый дальний объект"),
        ("getMin",    "getMin(field)",                "объект с наименьшим значением поля, например getMin(\"durability\")"),
        ("getMax",    "getMax(field)",                "объект с наибольшим значением поля"),
        ("getRandom", "getRandom()",                  "случайный объект"),
        ("getAt",     "getAt(x, y, [radius])",        "объект ближе всего к точке (по умолчанию не дальше 0.5)"),
        ("find",      "find(field, value)",           "первый объект, у которого поле равно значению, например find(\"id\", other.id)"),
        ("where",     "where(field, op, value)",      "новый список из подходящих объектов; op: ==, ~=, <, <=, >, >="),
        ("sortBy",    "sortBy(field, [descending])",  "новый список, отсортированный по полю (descending = true — по убыванию)"),
        ("take",      "take(n)",                      "новый список из первых n объектов"),
        ("contains",  "contains(obj)",                "есть ли объект в списке (сравнение по id)"),
        ("indexOf",   "indexOf(obj)",                 "номер объекта в списке или nil"),
        ("sum",       "sum(field)",                   "сумма числового поля, например sum(\"durability\") — энергия на добычу всех")
    };

    /// <summary>Поля объекта, с которыми работают методы.</summary>
    public const string Fields = "distance, durability, x, y, id, type, tool";

    private static readonly HashSet<string> MethodNames = new HashSet<string>();

    static LuaObjectList()
    {
        foreach (var method in Methods)
            MethodNames.Add(method.Name);
    }

    public static bool IsMethod(string name) => MethodNames.Contains(name);

    private readonly PlayerCommandExecutor _executor;

    private readonly struct Entry
    {
        public readonly ResourceObject Obj;
        public readonly LuaTable Table;

        public Entry(ResourceObject obj, LuaTable table)
        {
            Obj = obj;
            Table = table;
        }
    }

    public LuaObjectList(PlayerCommandExecutor executor)
    {
        _executor = executor;
    }

    // ─── Public API ───────────────────────────────────────────

    public LuaTable Build(List<ResourceObject> objects, EnergyMeter meter)
    {
        var entries = new List<Entry>(objects.Count);
        foreach (ResourceObject obj in objects)
            entries.Add(new Entry(obj, BuildObjectTable(obj)));

        return BuildList(entries, meter);
    }

    // ─── List ─────────────────────────────────────────────────

    private LuaTable BuildList(List<Entry> entries, EnergyMeter meter)
    {
        LuaTable list = new LuaTable();

        for (int i = 0; i < entries.Count; i++)
            list[i + 1] = entries[i].Table;

        // Сколько элементов (то же, что #list) — удобно для проверок: if list.count > 0
        list["count"] = (double)entries.Count;

        AddMethod(list, meter, "getNear", args => Pick(entries, e => -Distance(e.Obj)));
        AddMethod(list, meter, "getFar", args => Pick(entries, e => Distance(e.Obj)));
        AddMethod(list, meter, "getMin", args =>
        {
            string field = RequireField(args, 0, "getMin");
            return Pick(entries, e => -RequireNumber(e, field, "getMin"));
        });
        AddMethod(list, meter, "getMax", args =>
        {
            string field = RequireField(args, 0, "getMax");
            return Pick(entries, e => RequireNumber(e, field, "getMax"));
        });
        AddMethod(list, meter, "getRandom", args =>
        {
            List<Entry> alive = Alive(entries);
            return alive.Count > 0 ? alive[UnityEngine.Random.Range(0, alive.Count)].Table : LuaValue.Nil;
        });
        AddMethod(list, meter, "getAt", args =>
        {
            Vector2 point = new Vector2((float)RequireNumberArg(args, 0, "getAt"), (float)RequireNumberArg(args, 1, "getAt"));
            double radius = args.Count > 2 && args[2].TryRead(out double r) ? r : 0.5;
            Entry? best = null;
            float bestDistance = float.MaxValue;
            foreach (Entry e in Alive(entries))
            {
                float d = Vector2.Distance(point, e.Obj.Position);
                if (d <= radius && d < bestDistance)
                {
                    bestDistance = d;
                    best = e;
                }
            }
            return best.HasValue ? best.Value.Table : LuaValue.Nil;
        });
        AddMethod(list, meter, "find", args =>
        {
            string field = RequireField(args, 0, "find");
            LuaValue value = args.Count > 1 ? args[1] : LuaValue.Nil;
            foreach (Entry e in Alive(entries))
                if (Compare(GetField(e, field), "==", value))
                    return e.Table;
            return LuaValue.Nil;
        });
        AddMethod(list, meter, "where", args =>
        {
            string field = RequireField(args, 0, "where");
            if (args.Count < 2 || !args[1].TryRead(out string op) || !IsOperator(op))
                throw new ArgumentException("where: operator must be one of ==, ~=, <, <=, >, >=");
            LuaValue value = args.Count > 2 ? args[2] : LuaValue.Nil;

            var filtered = new List<Entry>();
            foreach (Entry e in Alive(entries))
                if (Compare(GetField(e, field), op, value))
                    filtered.Add(e);
            return BuildList(Refreshed(filtered), meter);
        });
        AddMethod(list, meter, "sortBy", args =>
        {
            string field = RequireField(args, 0, "sortBy");
            bool descending = args.Count > 1 && args[1].TryRead(out bool d) && d;

            List<Entry> sorted = Alive(entries);
            // Стабильная сортировка: при равенстве сохраняется исходный порядок
            var indexed = new List<(Entry entry, int index)>();
            for (int i = 0; i < sorted.Count; i++) indexed.Add((sorted[i], i));
            indexed.Sort((a, b) =>
            {
                int c = CompareForSort(GetField(a.entry, field), GetField(b.entry, field));
                if (descending) c = -c;
                return c != 0 ? c : a.index.CompareTo(b.index);
            });

            sorted.Clear();
            foreach (var item in indexed) sorted.Add(item.entry);
            return BuildList(Refreshed(sorted), meter);
        });
        AddMethod(list, meter, "take", args =>
        {
            int n = (int)Math.Max(0, Math.Floor(RequireNumberArg(args, 0, "take")));
            List<Entry> alive = Alive(entries);
            return BuildList(Refreshed(alive.GetRange(0, Math.Min(n, alive.Count))), meter);
        });
        AddMethod(list, meter, "contains", args => IndexOf(entries, args) > 0);
        AddMethod(list, meter, "indexOf", args =>
        {
            int index = IndexOf(entries, args);
            return index > 0 ? (LuaValue)(double)index : LuaValue.Nil;
        });
        AddMethod(list, meter, "sum", args =>
        {
            string field = RequireField(args, 0, "sum");
            double total = 0;
            foreach (Entry e in Alive(entries))
                total += RequireNumber(e, field, "sum");
            return total;
        });

        return list;
    }

    /// <summary>
    /// Метод вызывается через двоеточие (list:getNear()), поэтому первым аргументом приходит сам список —
    /// он отбрасывается; вызов через точку (list.getNear()) тоже работает.
    /// </summary>
    private static void AddMethod(LuaTable list, EnergyMeter meter, string name, Func<List<LuaValue>, LuaValue> body)
    {
        list[name] = new LuaFunction((context, ct) =>
        {
            meter?.Charge(MethodCost, $"{name}()");

            var args = new List<LuaValue>();
            for (int i = 0; i < context.ArgumentCount; i++)
                args.Add(context.GetArgument(i));
            if (args.Count > 0 && args[0].TryRead(out LuaTable self) && self == list)
                args.RemoveAt(0);

            context.Return(body(args));
            return new ValueTask<int>(1);
        });
    }

    // ─── Objects ──────────────────────────────────────────────

    private LuaTable BuildObjectTable(ResourceObject obj)
    {
        LuaTable table = new LuaTable();

        // Уникален для каждого объекта на карте — для сравнения: a.id == b.id
        table["id"] = (double)obj.GetInstanceID();
        table["x"] = (double)obj.Position.x;
        table["y"] = (double)obj.Position.y;
        table["type"] = obj.GetType().Name.ToLower().Replace("object", "");
        // Энергия на добычу сверх платы за команду (mine — по прочности добычи, подбор с земли — 0)
        table["durability"] = (double)ResourceHarvester.GetEnergyCost(obj);
        // Расстояние от персонажа (в тайлах) — на момент поиска; методы-списки его обновляют
        table["distance"] = (double)Distance(obj);
        // Нужный инструмент ("axe", "pickaxe") или nil
        if (obj.RequiredTool != null)
            table["tool"] = obj.RequiredTool.Value.ToString().ToLower();

        return table;
    }

    private float Distance(ResourceObject obj)
    {
        return Vector2.Distance(_executor.CurrentTile, obj.Position);
    }

    private LuaValue GetField(Entry e, string field)
    {
        switch (field)
        {
            case "distance": return (double)Distance(e.Obj);
            default: return e.Table[field];
        }
    }

    // ─── Helpers ──────────────────────────────────────────────

    private static List<Entry> Alive(List<Entry> entries)
    {
        var alive = new List<Entry>(entries.Count);
        foreach (Entry e in entries)
            if (e.Obj != null && e.Obj.IsAvailable)
                alive.Add(e);
        return alive;
    }

    /// <summary>
    /// Обновляет distance у объектов, попадающих в новый список.
    /// </summary>
    private List<Entry> Refreshed(List<Entry> entries)
    {
        foreach (Entry e in entries)
            e.Table["distance"] = (double)Distance(e.Obj);
        return entries;
    }

    /// <summary>
    /// Объект с наибольшей оценкой среди несобранных, или nil.
    /// </summary>
    private static LuaValue Pick(List<Entry> entries, Func<Entry, double> score)
    {
        Entry? best = null;
        double bestScore = double.MinValue;

        foreach (Entry e in Alive(entries))
        {
            double s = score(e);
            if (best == null || s > bestScore)
            {
                bestScore = s;
                best = e;
            }
        }

        return best.HasValue ? best.Value.Table : LuaValue.Nil;
    }

    private static int IndexOf(List<Entry> entries, List<LuaValue> args)
    {
        if (args.Count == 0 || !args[0].TryRead(out LuaTable other) || !other["id"].TryRead(out double id))
            return 0;

        for (int i = 0; i < entries.Count; i++)
            if (entries[i].Table["id"].TryRead(out double entryId) && entryId == id)
                return i + 1;

        return 0;
    }

    private static string RequireField(List<LuaValue> args, int index, string method)
    {
        if (args.Count > index && args[index].TryRead(out string field) && !string.IsNullOrEmpty(field))
            return field;
        throw new ArgumentException($"{method}: field name expected ({Fields})");
    }

    private static double RequireNumberArg(List<LuaValue> args, int index, string method)
    {
        if (args.Count > index && args[index].TryRead(out double value))
            return value;
        throw new ArgumentException($"{method}: number expected as argument {index + 1}");
    }

    private double RequireNumber(Entry e, string field, string method)
    {
        if (GetField(e, field).TryRead(out double value))
            return value;
        throw new ArgumentException($"{method}: field '{field}' is not a number (numeric fields: distance, durability, x, y, id)");
    }

    private static bool IsOperator(string op)
    {
        return op == "==" || op == "~=" || op == "!=" || op == "<" || op == "<=" || op == ">" || op == ">=";
    }

    private static bool Compare(LuaValue left, string op, LuaValue right)
    {
        bool leftNil = left.Type == LuaValueType.Nil;
        bool rightNil = right.Type == LuaValueType.Nil;

        if (op == "==" || op == "~=" || op == "!=")
        {
            bool equal;
            if (leftNil || rightNil) equal = leftNil && rightNil;
            else if (left.TryRead(out double a) && right.TryRead(out double b)) equal = a == b;
            else if (left.TryRead(out string sa) && right.TryRead(out string sb))
                equal = string.Equals(sa, sb, StringComparison.OrdinalIgnoreCase);
            else equal = false;

            return op == "==" ? equal : !equal;
        }

        // Сравнение порядка — только для чисел или строк одного вида; nil никогда не подходит
        if (leftNil || rightNil) return false;
        int c;
        if (left.TryRead(out double x) && right.TryRead(out double y)) c = x.CompareTo(y);
        else if (left.TryRead(out string sx) && right.TryRead(out string sy))
            c = string.Compare(sx, sy, StringComparison.OrdinalIgnoreCase);
        else return false;

        switch (op)
        {
            case "<": return c < 0;
            case "<=": return c <= 0;
            case ">": return c > 0;
            case ">=": return c >= 0;
            default: return false;
        }
    }

    /// <summary>
    /// Порядок для sortBy: числа по значению, строки по алфавиту, nil — в конце.
    /// </summary>
    private static int CompareForSort(LuaValue a, LuaValue b)
    {
        bool aNil = a.Type == LuaValueType.Nil;
        bool bNil = b.Type == LuaValueType.Nil;
        if (aNil || bNil) return aNil == bNil ? 0 : (aNil ? 1 : -1);

        if (a.TryRead(out double x) && b.TryRead(out double y)) return x.CompareTo(y);
        if (a.TryRead(out string sx) && b.TryRead(out string sy))
            return string.Compare(sx, sy, StringComparison.OrdinalIgnoreCase);
        return 0;
    }
}
