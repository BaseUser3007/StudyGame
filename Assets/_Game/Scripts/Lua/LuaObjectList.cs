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
/// Объекты, которые уже собраны, и погибшие враги методы пропускают. Расстояние считается на момент вызова
/// метода (поле distance у объектов в новом списке обновляется).
///
/// Список может быть смешанным: ресурсы (kind = "object") и враги (kind = "enemy", поля health,
/// maxHealth, state, durability = 0). У врага id = <see cref="EnemyIdOffset"/> + Enemy.Id — не пересекается
/// с id объектов (GetInstanceID объектов, созданных во время игры, отрицательные). Позиция, здоровье
/// и состояние врага читаются методами на момент вызова (враг двигается), а distance до врага —
/// от точной позиции персонажа, как её проверяет attack().
/// Головоломки (kind = "puzzle", type "obelisk"/"maze", поле solved): id = <see cref="PuzzleIdOffset"/> + PuzzleStructure.Id;
/// distance до них — от точной позиции персонажа до края области (так досягаемость проверяют puzzle()/solve()).
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
    public const string Fields = "distance, durability, x, y, id, type, kind, tool; у врагов ещё health, maxHealth, state; у головоломок — solved";

    /// <summary>Значение поля kind у ресурсов и врагов.</summary>
    public const string KindObject = "object";
    public const string KindEnemy = "enemy";
    public const string KindPuzzle = "puzzle";

    /// <summary>
    /// id врага в Lua = EnemyIdOffset + Enemy.Id. Объекты карты создаются во время игры, их GetInstanceID
    /// отрицательные, так что диапазоны не пересекаются: a.id == b.id и find("id", …) работают в смешанном списке.
    /// </summary>
    public const int EnemyIdOffset = 1000000000;

    /// <summary>
    /// id головоломки в Lua = PuzzleIdOffset + PuzzleStructure.Id (Id — с 1, их единицы): не пересекается
    /// ни с врагами, ни с объектами карты.
    /// </summary>
    public const int PuzzleIdOffset = 2000000000;

    private static readonly HashSet<string> MethodNames = new HashSet<string>();

    static LuaObjectList()
    {
        foreach (var method in Methods)
            MethodNames.Add(method.Name);
    }

    public static bool IsMethod(string name) => MethodNames.Contains(name);

    private readonly PlayerCommandExecutor _executor;

    /// <summary>
    /// Элемент списка: ресурс, враг или головоломка (ровно одно из трёх) и его таблица для Lua.
    /// </summary>
    private readonly struct Entry
    {
        public readonly ResourceObject Obj;
        public readonly Enemy Enemy;
        public readonly PuzzleStructure Puzzle;
        // Враги берутся из пула: после гибели тот же компонент может стать другим врагом с новым Id
        public readonly int EnemyId;
        public readonly LuaTable Table;

        public Entry(ResourceObject obj, LuaTable table)
        {
            Obj = obj;
            Enemy = null;
            Puzzle = null;
            EnemyId = 0;
            Table = table;
        }

        public Entry(Enemy enemy, LuaTable table)
        {
            Obj = null;
            Enemy = enemy;
            Puzzle = null;
            EnemyId = enemy.Id;
            Table = table;
        }

        public Entry(PuzzleStructure puzzle, LuaTable table)
        {
            Obj = null;
            Enemy = null;
            Puzzle = puzzle;
            EnemyId = 0;
            Table = table;
        }

        public bool IsEnemy => !ReferenceEquals(Enemy, null);
        public bool IsPuzzle => !ReferenceEquals(Puzzle, null);

        // Головоломка остаётся на карте до конца забега (решённая — с solved = true)
        public bool IsAlive => IsEnemy
            ? Enemy != null && Enemy.IsAlive && Enemy.Id == EnemyId
            : IsPuzzle ? Puzzle != null : Obj != null && Obj.IsAvailable;

        public Vector2 Position => IsEnemy ? Enemy.Position : IsPuzzle ? Puzzle.Position : Obj.Position;
    }

    public LuaObjectList(PlayerCommandExecutor executor)
    {
        _executor = executor;
    }

    // ─── Public API ───────────────────────────────────────────

    public LuaTable Build(List<ResourceObject> objects, EnergyMeter meter)
    {
        return Build(objects, null, meter);
    }

    /// <summary>
    /// Смешанный список: сначала ресурсы, затем живые враги (видимость проверяет вызывающий).
    /// </summary>
    public LuaTable Build(List<ResourceObject> objects, List<Enemy> enemies, EnergyMeter meter)
    {
        return Build(objects, enemies, null, meter);
    }

    /// <summary>
    /// Смешанный список: ресурсы, живые враги, затем головоломки (видимость проверяет вызывающий).
    /// </summary>
    public LuaTable Build(
        List<ResourceObject> objects, List<Enemy> enemies, List<PuzzleStructure> puzzles, EnergyMeter meter)
    {
        var entries = new List<Entry>((objects?.Count ?? 0) + (enemies?.Count ?? 0) + (puzzles?.Count ?? 0));
        if (objects != null)
            foreach (ResourceObject obj in objects)
                entries.Add(new Entry(obj, BuildObjectTable(obj)));
        if (enemies != null)
            foreach (Enemy enemy in enemies)
                if (enemy != null && enemy.IsAlive)
                    entries.Add(new Entry(enemy, BuildEnemyTable(enemy)));
        if (puzzles != null)
            foreach (PuzzleStructure puzzle in puzzles)
                if (puzzle != null)
                    entries.Add(new Entry(puzzle, BuildPuzzleTable(puzzle)));

        return BuildList(entries, meter);
    }

    /// <summary>
    /// Таблица головоломки (kind = "puzzle") — элемент findObjects, поле puzzle у check.
    /// </summary>
    public LuaTable BuildPuzzleTable(PuzzleStructure puzzle)
    {
        LuaTable table = new LuaTable();

        table["id"] = (double)(PuzzleIdOffset + puzzle.Id);
        table["kind"] = KindPuzzle;
        table["type"] = LuaPuzzles.GetTypeName(puzzle);
        // Обелиск — основание, лабиринт — центральный тайл (цель)
        table["x"] = (double)puzzle.Position.x;
        table["y"] = (double)puzzle.Position.y;
        // Не добывается — энергии на «добычу» 0, как у врагов
        table["durability"] = 0d;
        if (puzzle is MazePuzzle maze && maze.Layout != null)
        {
            // Тайл снаружи перед входом: moveTo(maze.entranceX, maze.entranceY)
            table["entranceX"] = (double)maze.ApproachTile.x;
            table["entranceY"] = (double)maze.ApproachTile.y;
        }
        RefreshPuzzle(table, puzzle);
        return table;
    }

    /// <summary>
    /// Таблица — головоломка из findObjects/check (kind = "puzzle"): возвращает PuzzleStructure.Id.
    /// </summary>
    public static bool TryGetPuzzleId(LuaTable table, out int puzzleId)
    {
        puzzleId = 0;
        if (table == null || !table["kind"].TryRead(out string kind) || kind != KindPuzzle) return false;
        if (!table["id"].TryRead(out double id)) return false;

        puzzleId = (int)(id - PuzzleIdOffset);
        return true;
    }

    /// <summary>
    /// Таблица — враг из findObjects/check (kind = "enemy"): возвращает Enemy.Id.
    /// </summary>
    public static bool TryGetEnemyId(LuaTable table, out int enemyId)
    {
        enemyId = 0;
        if (table == null || !table["kind"].TryRead(out string kind) || kind != KindEnemy) return false;
        if (!table["id"].TryRead(out double id)) return false;

        enemyId = (int)(id - EnemyIdOffset);
        return true;
    }

    // ─── List ─────────────────────────────────────────────────

    private LuaTable BuildList(List<Entry> entries, EnergyMeter meter)
    {
        LuaTable list = new LuaTable();

        for (int i = 0; i < entries.Count; i++)
            list[i + 1] = entries[i].Table;

        // Сколько элементов (то же, что #list) — удобно для проверок: if list.count > 0
        list["count"] = (double)entries.Count;

        AddMethod(list, meter, "getNear", args => Pick(entries, e => -Distance(e)));
        AddMethod(list, meter, "getFar", args => Pick(entries, e => Distance(e)));
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
                float d = Vector2.Distance(point, e.Position);
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

    /// <summary>
    /// Имя типа объекта для Lua: TreeObject → "tree", GoldOreObject → "gold_ore".
    /// </summary>
    public static string GetTypeName(ResourceObject obj)
    {
        return GetTypeName(obj.GetType());
    }

    public static string GetTypeName(Type type)
    {
        const string suffix = "Object";
        string name = type.Name;
        if (name.EndsWith(suffix, StringComparison.Ordinal) && name.Length > suffix.Length)
            name = name.Substring(0, name.Length - suffix.Length);

        // CamelCase → snake_case
        var sb = new System.Text.StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (char.IsUpper(c) && i > 0) sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    private LuaTable BuildObjectTable(ResourceObject obj)
    {
        LuaTable table = new LuaTable();

        // Уникален для каждого объекта на карте — для сравнения: a.id == b.id
        table["id"] = (double)obj.GetInstanceID();
        table["kind"] = KindObject;
        table["x"] = (double)obj.Position.x;
        table["y"] = (double)obj.Position.y;
        table["type"] = GetTypeName(obj);
        // Энергия на добычу сверх платы за команду (mine — по прочности добычи, подбор с земли — 0)
        table["durability"] = (double)ResourceHarvester.GetEnergyCost(obj);
        // Расстояние от персонажа (в тайлах) — на момент поиска; методы-списки его обновляют
        table["distance"] = (double)Distance(obj);
        // Нужный инструмент ("axe", "pickaxe") или nil
        if (obj.RequiredTool != null)
            table["tool"] = obj.RequiredTool.Value.ToString().ToLower();

        return table;
    }

    private LuaTable BuildEnemyTable(Enemy enemy)
    {
        LuaTable table = new LuaTable();

        table["id"] = (double)(EnemyIdOffset + enemy.Id);
        table["kind"] = KindEnemy;
        table["type"] = enemy.TypeId;
        // Врага не добывают (harvest/mine → «use attack()»); удар стоит только цену вызова attack
        table["durability"] = 0d;
        // tool = nil: оружие не обязательно, кулак тоже бьёт
        RefreshEnemy(table, enemy);
        return table;
    }

    /// <summary>
    /// Изменчивые поля врага: позиция, здоровье, состояние, расстояние.
    /// </summary>
    private void RefreshEnemy(LuaTable table, Enemy enemy)
    {
        table["x"] = (double)enemy.Position.x;
        table["y"] = (double)enemy.Position.y;
        table["health"] = (double)enemy.Health;
        table["maxHealth"] = (double)enemy.MaxHealth;
        table["state"] = enemy.StateName;
        table["distance"] = (double)Distance(enemy);
    }

    /// <summary>Изменчивые поля головоломки: решена ли, расстояние.</summary>
    private void RefreshPuzzle(LuaTable table, PuzzleStructure puzzle)
    {
        table["solved"] = puzzle.IsSolved;
        table["distance"] = (double)Distance(puzzle);
    }

    private float Distance(ResourceObject obj)
    {
        return Vector2.Distance(_executor.CurrentTile, obj.Position);
    }

    // До врага — от точной позиции персонажа: так дистанцию проверяет удар (CombatService)
    private float Distance(Enemy enemy)
    {
        return Vector2.Distance(_executor.Position, enemy.Position);
    }

    // До головоломки — от точной позиции персонажа до края области: так досягаемость проверяет PuzzleManager
    private float Distance(PuzzleStructure puzzle)
    {
        return _executor != null ? puzzle.DistanceTo(_executor.Position) : 0f;
    }

    private float Distance(Entry e)
    {
        return e.IsEnemy ? Distance(e.Enemy) : e.IsPuzzle ? Distance(e.Puzzle) : Distance(e.Obj);
    }

    private LuaValue GetField(Entry e, string field)
    {
        if (field == "distance") return (double)Distance(e);

        // Враг двигается и теряет здоровье — читаем на момент вызова метода
        if (e.IsEnemy)
        {
            switch (field)
            {
                case "x": return (double)e.Enemy.Position.x;
                case "y": return (double)e.Enemy.Position.y;
                case "health": return (double)e.Enemy.Health;
                case "state": return e.Enemy.StateName;
            }
        }

        // Головоломку могли решить после поиска
        if (e.IsPuzzle && field == "solved") return e.Puzzle.IsSolved;

        return e.Table[field];
    }

    // ─── Helpers ──────────────────────────────────────────────

    private static List<Entry> Alive(List<Entry> entries)
    {
        var alive = new List<Entry>(entries.Count);
        foreach (Entry e in entries)
            if (e.IsAlive)
                alive.Add(e);
        return alive;
    }

    /// <summary>
    /// Обновляет distance (у врагов — ещё позицию, здоровье, состояние) у элементов нового списка.
    /// </summary>
    private List<Entry> Refreshed(List<Entry> entries)
    {
        foreach (Entry e in entries)
        {
            if (e.IsEnemy) RefreshEnemy(e.Table, e.Enemy);
            else if (e.IsPuzzle) RefreshPuzzle(e.Table, e.Puzzle);
            else e.Table["distance"] = (double)Distance(e.Obj);
        }
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
        throw new ArgumentException($"{method}: field '{field}' is not a number (numeric fields: distance, durability, x, y, id; enemies: health, maxHealth)");
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
