using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Стартовый набор у спавна (MapGenerationConfig.starterResources), чистая логика без сцены:
/// сколько объектов каждого типа не хватает в обзоре со спавна и случайная точка в кольце.
/// Размещением (суша, footprint, minObjectSpacing) занимается ObjectGenerator.
/// </summary>
public static class StarterResourcePlanner
{
    // ─── Подсчёт ──────────────────────────────────────────────

    /// <summary>
    /// Сколько объектов каждого ключа (тип объекта) нужно досыпать. Повторы в required складываются,
    /// пустые ключи и count ≤ 0 пропускаются. Уже имеющимися считаются объекты не дальше maxRadius
    /// от center. Порядок результата — порядок первого появления ключа в required (детерминированно).
    /// </summary>
    public static List<(T key, int missing)> GetMissing<T>(
        IEnumerable<(T key, int count)> required,
        IEnumerable<(T key, Vector2 position)> existing,
        Vector2 center,
        float maxRadius)
    {
        var order = new List<T>();
        var needed = new Dictionary<T, int>();

        foreach ((T key, int count) in required)
        {
            if (key == null || count <= 0) continue;

            if (needed.TryGetValue(key, out int current))
                needed[key] = current + count;
            else
            {
                needed[key] = count;
                order.Add(key);
            }
        }

        var present = new Dictionary<T, int>();
        foreach ((T key, Vector2 position) in existing)
        {
            if (key == null || !needed.ContainsKey(key)) continue;
            if (Vector2.Distance(center, position) > maxRadius) continue;

            present.TryGetValue(key, out int current);
            present[key] = current + 1;
        }

        var result = new List<(T key, int missing)>();
        foreach (T key in order)
        {
            present.TryGetValue(key, out int have);
            int missing = needed[key] - have;
            if (missing > 0) result.Add((key, missing));
        }

        return result;
    }

    // ─── Кольцо ───────────────────────────────────────────────

    /// <summary>
    /// Равномерно по площади случайная точка в кольце minRadius ≤ r ≤ maxRadius вокруг center.
    /// </summary>
    public static Vector2 RandomPointInRing(System.Random random, Vector2 center, float minRadius, float maxRadius)
    {
        if (random == null) throw new ArgumentNullException(nameof(random));

        float min = Mathf.Max(0f, Mathf.Min(minRadius, maxRadius));
        float max = Mathf.Max(minRadius, maxRadius);
        // r = √(lerp(min², max², u)) — иначе точки скучиваются у внутреннего края
        double u = random.NextDouble();
        float radius = (float)Math.Sqrt(min * min + u * (max * max - min * min));
        double angle = random.NextDouble() * 2.0 * Math.PI;

        return center + new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * radius;
    }

    public static bool IsInRing(Vector2 center, Vector2 position, float minRadius, float maxRadius)
    {
        float distance = Vector2.Distance(center, position);
        return distance >= minRadius && distance <= maxRadius;
    }
}
