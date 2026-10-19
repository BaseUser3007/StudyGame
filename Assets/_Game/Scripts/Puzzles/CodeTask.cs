using System;
using System.Collections.Generic;

/// <summary>Вид задачи обелиска (GameDesign.md §10.4).</summary>
public enum CodeTaskKind
{
    /// <summary>Сумма всех чисел.</summary>
    Sum,
    /// <summary>Наибольшее число.</summary>
    Max,
    /// <summary>Сколько чисел больше limit.</summary>
    Count
}

/// <summary>
/// Одна задача на код: условие (англ., для UI и Lua), входной массив, limit и правильный ответ.
/// Чистая логика без Unity — вход генерируется из переданного System.Random.
/// </summary>
public sealed class CodeTask
{
    /// <summary>Сколько чисел во входе sum/max.</summary>
    public const int InputLength = 12;
    /// <summary>Сколько чисел во входе count (перебор solve закрыт правилом «одна попытка за запуск», PuzzleManager).</summary>
    public const int CountInputLength = 12;
    /// <summary>Порог count по умолчанию для Create (тесты, отладка); Generate берёт случайный.</summary>
    public const int CountLimit = 10;
    /// <summary>Диапазон случайного порога count в Generate.</summary>
    public const int CountLimitMin = 10;
    public const int CountLimitMax = 89;
    /// <summary>Значения входа count: 1–99.</summary>
    public const int CountValueMax = 99;

    // Допуск сравнения ответа: Lua отдаёт double, ответы целые
    private const double AnswerTolerance = 1e-6;

    public CodeTaskKind Kind { get; }
    /// <summary>Id вида для Lua (kind): "sum" / "max" / "count".</summary>
    public string KindId => ToId(Kind);
    public string Text => GetText(Kind);
    public IReadOnlyList<int> Input { get; }
    /// <summary>Порог (только для count, иначе 0).</summary>
    public int Limit { get; }
    /// <summary>Правильный ответ.</summary>
    public int Answer { get; }

    private CodeTask(CodeTaskKind kind, int[] input, int limit)
    {
        Kind = kind;
        Input = input;
        Limit = limit;
        Answer = Solve(kind, input, limit);
    }

    // ─── Public API ───────────────────────────────────────────

    /// <summary>
    /// Новая задача: sum — 12 целых 1–50, max — 12 целых 1–99, count — 30 целых 1–99 со случайным limit 10–89.
    /// </summary>
    public static CodeTask Generate(CodeTaskKind kind, Random random)
    {
        if (random == null) throw new ArgumentNullException(nameof(random));

        if (kind == CodeTaskKind.Count)
            return GenerateCount(random);

        GetRange(kind, out int min, out int max);
        var input = new int[InputLength];
        for (int i = 0; i < input.Length; i++)
            input[i] = random.Next(min, max + 1);

        return new CodeTask(kind, input, 0);
    }

    /// <summary>Длина входа задачи этого вида.</summary>
    public static int GetInputLength(CodeTaskKind kind) =>
        kind == CodeTaskKind.Count ? CountInputLength : InputLength;

    /// <summary>Задача с заданным входом (для тестов и отладки).</summary>
    public static CodeTask Create(CodeTaskKind kind, int[] input, int limit = CountLimit)
    {
        if (input == null) throw new ArgumentNullException(nameof(input));
        return new CodeTask(kind, (int[])input.Clone(), kind == CodeTaskKind.Count ? limit : 0);
    }

    /// <summary>Ответ верный (число, равное правильному; NaN/бесконечность — нет).</summary>
    public bool IsCorrect(double answer)
    {
        if (double.IsNaN(answer) || double.IsInfinity(answer)) return false;
        return Math.Abs(answer - Answer) < AnswerTolerance;
    }

    public static string ToId(CodeTaskKind kind)
    {
        switch (kind)
        {
            case CodeTaskKind.Sum: return "sum";
            case CodeTaskKind.Max: return "max";
            case CodeTaskKind.Count: return "count";
            default: return kind.ToString().ToLowerInvariant();
        }
    }

    public static string GetText(CodeTaskKind kind)
    {
        switch (kind)
        {
            case CodeTaskKind.Sum: return "Return the sum of all numbers in input.";
            case CodeTaskKind.Max: return "Return the largest number in input.";
            case CodeTaskKind.Count: return "Return how many numbers in input are greater than limit.";
            default: return string.Empty;
        }
    }

    // ─── Helpers ──────────────────────────────────────────────

    /// <summary>
    /// count: ответ k выбирается равномерно из 0..30 (limit не подсказывает ответ), затем k чисел больше limit
    /// и остальные не больше него, перемешанные. Перебору solve нужно в среднем ~16 попыток при любой стратегии.
    /// </summary>
    private static CodeTask GenerateCount(Random random)
    {
        int limit = random.Next(CountLimitMin, CountLimitMax + 1);
        int above = random.Next(0, CountInputLength + 1);

        var input = new int[CountInputLength];
        for (int i = 0; i < input.Length; i++)
            input[i] = i < above
                ? random.Next(limit + 1, CountValueMax + 1)
                : random.Next(1, limit + 1);

        // Фишер–Йетс: числа больше limit не должны стоять подряд в начале
        for (int i = input.Length - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (input[i], input[j]) = (input[j], input[i]);
        }

        return new CodeTask(CodeTaskKind.Count, input, limit);
    }

    private static void GetRange(CodeTaskKind kind, out int min, out int max)
    {
        switch (kind)
        {
            case CodeTaskKind.Max: min = 1; max = 99; break;
            default: min = 1; max = 50; break;
        }
    }

    private static int Solve(CodeTaskKind kind, int[] input, int limit)
    {
        int result = 0;
        switch (kind)
        {
            case CodeTaskKind.Sum:
                foreach (int n in input) result += n;
                break;
            case CodeTaskKind.Max:
                result = input.Length > 0 ? int.MinValue : 0;
                foreach (int n in input) result = Math.Max(result, n);
                break;
            case CodeTaskKind.Count:
                foreach (int n in input) if (n > limit) result++;
                break;
        }
        return result;
    }
}

/// <summary>
/// Задача одного обелиска на протяжении забега: вид постоянный, вход — свой для каждого запуска кода.
/// Задача создаётся при первом запросе в запуске (runId — GameEventBus.ActiveRunId; 0 — вне запуска,
/// например для UI) и не меняется до следующего запуска — ответ надо вычислить кодом в том же запуске.
/// </summary>
public sealed class CodeTaskSource
{
    private readonly int _seed;
    private CodeTask _task;
    private int _taskRunId = -1;

    public CodeTaskKind Kind { get; }

    /// <param name="seed">База для входных данных (Level смешивает seed карты и соль сцены).</param>
    public CodeTaskSource(CodeTaskKind kind, int seed)
    {
        Kind = kind;
        _seed = seed;
    }

    /// <summary>Задача для запуска runId (тот же runId — та же задача).</summary>
    public CodeTask GetTask(int runId)
    {
        if (_task == null || runId != _taskRunId)
        {
            // Вход зависит от запуска: новый запуск — новые числа
            var random = new Random(unchecked(_seed * 486187739 + runId * 16777619 + 97));
            _task = CodeTask.Generate(Kind, random);
            _taskRunId = runId;
        }
        return _task;
    }

    /// <summary>Проверка ответа против задачи этого запуска (если её не запрашивали — создаётся сейчас).</summary>
    public bool Check(int runId, double answer) => GetTask(runId).IsCorrect(answer);
}
