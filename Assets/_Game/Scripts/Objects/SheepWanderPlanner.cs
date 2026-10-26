using System;
using UnityEngine;

/// <summary>
/// Чистая логика блуждания овцы (без Unity-сцены, покрыта EditMode-тестами SheepWanderTests):
/// seed овцы, выбор следующей точки около «дома», длительность паузы.
/// Проверка места (суша, головоломки, лагеря) приходит снаружи предикатом isAllowed.
/// </summary>
public static class SheepWanderPlanner
{
    /// <summary>Сколько случайных точек пробуем, прежде чем сдаться (овца постоит и попробует позже).</summary>
    public const int Attempts = 12;
    /// <summary>Шаг проверки отрезка до цели (в тайлах): весь путь должен быть разрешён.</summary>
    public const float PathSampleStep = 0.25f;

    // ─── Seed ─────────────────────────────────────────────────

    /// <summary>
    /// Seed овцы: от seed карты и точки появления (она детерминирована от seed), с точностью до 0.01 тайла.
    /// GetInstanceID не годится — он разный от запуска к запуску.
    /// </summary>
    public static int MakeSeed(int mapSeed, Vector2 home)
    {
        unchecked
        {
            int hash = mapSeed * 486187739 + 0x5EEB;
            hash = hash * 16777619 ^ Mathf.RoundToInt(home.x * 100f);
            hash = hash * 16777619 ^ Mathf.RoundToInt(home.y * 100f);
            return hash;
        }
    }

    // ─── Target ───────────────────────────────────────────────

    /// <summary>
    /// Случайная точка не дальше radius от home и не ближе minStep к current, до которой весь отрезок
    /// от current разрешён (isAllowed по точкам с шагом PathSampleStep). false — подходящей не нашлось.
    /// </summary>
    public static bool TryChooseTarget(
        Vector2 home, Vector2 current, float radius, float minStep, System.Random random,
        Func<Vector2, bool> isAllowed, out Vector2 target)
    {
        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            Vector2 point = home + RandomInCircle(random, radius);
            if (Vector2.Distance(point, current) < minStep) continue;
            if (!IsPathAllowed(current, point, isAllowed)) continue;

            target = point;
            return true;
        }

        target = current;
        return false;
    }

    /// <summary>Все точки отрезка (с шагом PathSampleStep, конец включён) разрешены. Начало не проверяется.</summary>
    public static bool IsPathAllowed(Vector2 from, Vector2 to, Func<Vector2, bool> isAllowed)
    {
        float length = Vector2.Distance(from, to);
        int steps = Mathf.Max(1, Mathf.CeilToInt(length / PathSampleStep));

        for (int i = 1; i <= steps; i++)
            if (!isAllowed(Vector2.Lerp(from, to, (float)i / steps)))
                return false;
        return true;
    }

    // ─── Wait ─────────────────────────────────────────────────

    /// <summary>Пауза в секундах, равномерно в [min, max).</summary>
    public static float NextWait(System.Random random, float min, float max)
    {
        return min + (float)random.NextDouble() * Mathf.Max(0f, max - min);
    }

    // ─── Helpers ──────────────────────────────────────────────

    private static Vector2 RandomInCircle(System.Random random, float radius)
    {
        double angle = random.NextDouble() * Math.PI * 2.0;
        // sqrt — равномерно по площади круга, а не гуще у центра
        double distance = Math.Sqrt(random.NextDouble()) * radius;
        return new Vector2((float)(Math.Cos(angle) * distance), (float)(Math.Sin(angle) * distance));
    }
}
