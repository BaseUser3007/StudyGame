using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Место забега по умолчанию: портал — в случайной точке суши, игрок — вплотную под ним.
/// Детерминирован от seed (System.Random, без UnityEngine.Random).
///
/// Правила выбора:
/// 1. Под порталом и в поле landMargin тайлов вокруг — только суша (край карты считается водой).
/// 2. Точка появления — проходимый тайл, касающийся портала: сначала ряд под ним (от середины),
///    затем бока, затем ряд над ним. Игрок стоит ниже портала и рисуется перед ним.
/// 3. Связная (по 4 соседям) область суши, где стоит игрок, — не меньше minRegionTiles тайлов,
///    чтобы забег не начинался на крошечном острове. Если таких нет — самая большая область.
/// 4. Совсем нет места — запасной вариант: спавн как раньше (SpawnPoint), портал — рядом с ним
///    (сверху, если можно), а если и рядом нет суши — на самом тайле спавна (SpawnInsidePortal).
/// </summary>
public sealed class RandomPortalLocationGenerator : IRunLocationGenerator
{
    public const int DefaultMinRegionTiles = 150;

    private readonly Vector2Int _portalSize;
    private readonly int _landMargin;
    private readonly int _minRegionTiles;
    private readonly Vector2Int _fallbackSpawn;

    /// <param name="portalSize">Размер портала в тайлах (минимум 1×1).</param>
    /// <param name="landMargin">Поле суши вокруг портала, в тайлах.</param>
    /// <param name="minRegionTiles">Минимальная площадь связной суши, где начинается забег.</param>
    /// <param name="fallbackSpawn">Спавн, если места нет (прежняя логика SpawnPoint).</param>
    public RandomPortalLocationGenerator(
        Vector2Int portalSize, int landMargin, int minRegionTiles, Vector2Int fallbackSpawn)
    {
        _portalSize = new Vector2Int(Mathf.Max(1, portalSize.x), Mathf.Max(1, portalSize.y));
        _landMargin = Mathf.Max(0, landMargin);
        _minRegionTiles = Mathf.Max(1, minRegionTiles);
        _fallbackSpawn = fallbackSpawn;
    }

    /// <summary>Параметры из MapGenerationConfig (null — значения по умолчанию).</summary>
    public static RandomPortalLocationGenerator FromConfig(MapGenerationConfig config, Vector2Int fallbackSpawn)
    {
        if (config == null)
            return new RandomPortalLocationGenerator(Vector2Int.one, 1, DefaultMinRegionTiles, fallbackSpawn);

        return new RandomPortalLocationGenerator(
            config.portalSize, config.portalLandMargin, config.portalMinRegionTiles, fallbackSpawn);
    }

    // ─── Public API ───────────────────────────────────────────

    public RunLocation Generate(IMapDataProvider map, int seed)
    {
        int[,] regions = LabelRegions(map, out List<int> sizes);

        // Сначала — только большие области; нет таких — самая большая из имеющихся
        List<(Vector2Int portal, Vector2Int spawn)> candidates =
            CollectCandidates(map, regions, id => sizes[id] >= _minRegionTiles);

        if (candidates.Count == 0 && sizes.Count > 0)
        {
            int largest = 0;
            for (int i = 1; i < sizes.Count; i++)
                if (sizes[i] > sizes[largest]) largest = i;
            candidates = CollectCandidates(map, regions, id => id == largest);
        }

        if (candidates.Count == 0)
            return Fallback(map, seed);

        // Свой сдвиг seed — не совпадать с биомами, объектами, лагерями и головоломками
        var random = new System.Random(unchecked(seed * 37 + 4099));
        (Vector2Int portal, Vector2Int spawn) chosen = candidates[random.Next(candidates.Count)];
        return new RunLocation(seed, chosen.spawn, new RectInt(chosen.portal, _portalSize));
    }

    // ─── Candidates ───────────────────────────────────────────

    private List<(Vector2Int portal, Vector2Int spawn)> CollectCandidates(
        IMapDataProvider map, int[,] regions, System.Func<int, bool> regionAllowed)
    {
        var result = new List<(Vector2Int, Vector2Int)>();

        for (int x = 0; x < map.MapWidth; x++)
            for (int y = 0; y < map.MapHeight; y++)
            {
                var area = new RectInt(x, y, _portalSize.x, _portalSize.y);
                if (!IsLand(map, Expand(area, _landMargin))) continue;

                Vector2Int? spawn = FindSpawnNear(map, area);
                if (!spawn.HasValue) continue;

                int region = regions[spawn.Value.x, spawn.Value.y];
                if (region < 0 || !regionAllowed(region)) continue;

                result.Add((area.position, spawn.Value));
            }

        return result;
    }

    /// <summary>
    /// Проходимый тайл, касающийся портала: ряд под ним (от середины к краям), бока снизу вверх, ряд над ним.
    /// </summary>
    public static Vector2Int? FindSpawnNear(IMapDataProvider map, RectInt area)
    {
        int center = area.xMin + (area.width - 1) / 2;
        int below = area.yMin - 1;

        // Под порталом, от середины: center, center+1, center-1, …
        for (int d = 0; d <= area.width; d++)
        {
            int right = center + d;
            if (right <= area.xMax && map.IsWalkable(right, below)) return new Vector2Int(right, below);
            int left = center - d;
            if (d > 0 && left >= area.xMin - 1 && map.IsWalkable(left, below)) return new Vector2Int(left, below);
        }

        for (int y = area.yMin; y < area.yMax; y++)
        {
            if (map.IsWalkable(area.xMin - 1, y)) return new Vector2Int(area.xMin - 1, y);
            if (map.IsWalkable(area.xMax, y)) return new Vector2Int(area.xMax, y);
        }

        for (int x = area.xMin - 1; x <= area.xMax; x++)
            if (map.IsWalkable(x, area.yMax)) return new Vector2Int(x, area.yMax);

        return null;
    }

    // ─── Fallback ─────────────────────────────────────────────

    /// <summary>
    /// Спавн — прежний (SpawnPoint); портал — вплотную к нему на суше (лучше над ним, ближе к середине),
    /// без требования поля. Нет суши рядом — портал 1×1 на самом тайле спавна.
    /// </summary>
    private RunLocation Fallback(IMapDataProvider map, int seed)
    {
        Vector2Int spawn = _fallbackSpawn;
        RectInt? best = null;
        int bestScore = int.MaxValue;

        for (int px = spawn.x - _portalSize.x; px <= spawn.x + 1; px++)
            for (int py = spawn.y - _portalSize.y; py <= spawn.y + 1; py++)
            {
                var area = new RectInt(px, py, _portalSize.x, _portalSize.y);
                if (area.Contains(spawn) || !IsLand(map, area)) continue;
                // Спавн должен касаться портала (в том числе по диагонали)
                if (!Expand(area, 1).Contains(spawn)) continue;

                int centerX = area.xMin + (area.width - 1) / 2;
                int score = (py == spawn.y + 1 ? 0 : 100) + Mathf.Abs(centerX - spawn.x);
                if (score >= bestScore) continue;
                bestScore = score;
                best = area;
            }

        RectInt portal = best ?? new RectInt(spawn, Vector2Int.one);
        Debug.LogWarning($"RandomPortalLocationGenerator: no land for the portal (size {_portalSize}, " +
                         $"margin {_landMargin}) — fallback spawn {spawn}, portal at {portal.position}");
        return new RunLocation(seed, spawn, portal, usedFallback: true);
    }

    // ─── Helpers ──────────────────────────────────────────────

    /// <summary>Номера связных (по 4 соседям) областей суши; вода — -1. sizes[id] — площадь области.</summary>
    private static int[,] LabelRegions(IMapDataProvider map, out List<int> sizes)
    {
        int width = map.MapWidth;
        int height = map.MapHeight;
        var labels = new int[width, height];
        sizes = new List<int>();

        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                labels[x, y] = -1;

        var queue = new Queue<Vector2Int>();
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                if (labels[x, y] >= 0 || !map.IsWalkable(x, y)) continue;

                int id = sizes.Count;
                int size = 0;
                labels[x, y] = id;
                queue.Enqueue(new Vector2Int(x, y));

                while (queue.Count > 0)
                {
                    Vector2Int tile = queue.Dequeue();
                    size++;
                    TryVisit(map, labels, queue, id, tile.x + 1, tile.y);
                    TryVisit(map, labels, queue, id, tile.x - 1, tile.y);
                    TryVisit(map, labels, queue, id, tile.x, tile.y + 1);
                    TryVisit(map, labels, queue, id, tile.x, tile.y - 1);
                }

                sizes.Add(size);
            }

        return labels;
    }

    private static void TryVisit(IMapDataProvider map, int[,] labels, Queue<Vector2Int> queue, int id, int x, int y)
    {
        if (!map.IsInBounds(x, y) || labels[x, y] >= 0 || !map.IsWalkable(x, y)) return;
        labels[x, y] = id;
        queue.Enqueue(new Vector2Int(x, y));
    }

    /// <summary>Все тайлы прямоугольника — суша (за краем карты — вода).</summary>
    private static bool IsLand(IMapDataProvider map, RectInt area)
    {
        for (int x = area.xMin; x < area.xMax; x++)
            for (int y = area.yMin; y < area.yMax; y++)
                if (!map.IsWalkable(x, y))
                    return false;
        return true;
    }

    private static RectInt Expand(RectInt area, int margin)
    {
        return new RectInt(area.xMin - margin, area.yMin - margin, area.width + margin * 2, area.height + margin * 2);
    }
}
