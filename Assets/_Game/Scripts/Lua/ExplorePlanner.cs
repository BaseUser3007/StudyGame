using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Выбор цели для autoExplore: куда идти, чтобы открыть больше никогда не виденной суши.
/// 1) Из 8 направлений — то, где путь до <see cref="StepTiles"/> тайлов по прямой (как MoveAsync: вода
///    останавливает) откроет больше всего неоткрытых тайлов суши (квадрат обзора вокруг каждого тайла пути).
/// 2) Если ни одно направление ничего не открывает — BFS по суше (8 соседей) в радиусе
///    <see cref="FrontierSearchRadius"/> до ближайшего неоткрытого тайла; цель — самая дальняя точка
///    этого пути (не дальше StepTiles шагов), до которой можно дойти по прямой (как MoveToAsync).
/// Равные варианты решает хеш от seed карты, позиции и направления — без UnityEngine.Random.
/// Чистая логика без сцены — тестируется в EditMode.
/// </summary>
public static class ExplorePlanner
{
    /// <summary>Сколько тайлов проходит один шаг разведки.</summary>
    public const int StepTiles = 6;
    /// <summary>Как далеко (по Чебышёву) ищется граница тумана, если вокруг всё открыто.</summary>
    public const int FrontierSearchRadius = 20;

    private static readonly Direction[] Directions =
    {
        Direction.North, Direction.NorthEast, Direction.East, Direction.SouthEast,
        Direction.South, Direction.SouthWest, Direction.West, Direction.NorthWest
    };

    public readonly struct Plan
    {
        /// <summary>Тайл, к которому идти (MoveToAsync).</summary>
        public readonly Vector2Int Target;
        /// <summary>Сколько неоткрытых тайлов суши откроет путь (по расчёту, без учёта объектов).</summary>
        public readonly int ExpectedNewTiles;
        /// <summary>Цель выбрана по пути к границе тумана (вокруг всё открыто).</summary>
        public readonly bool ToFrontier;

        public Plan(Vector2Int target, int expectedNewTiles, bool toFrontier)
        {
            Target = target;
            ExpectedNewTiles = expectedNewTiles;
            ToFrontier = toFrontier;
        }
    }

    // ─── Public API ───────────────────────────────────────────

    /// <summary>
    /// Цель шага разведки. false — идти некуда: всё в радиусе поиска открыто или недостижимо.
    /// </summary>
    public static bool TryPlan(
        IMapDataProvider map, IFogProvider fog, Vector2Int start, int visionRadius, out Plan plan)
    {
        plan = default;
        if (map == null || fog == null) return false;
        visionRadius = Mathf.Max(0, visionRadius);

        int bestScore = 0;
        uint bestTie = 0;
        Vector2Int bestTarget = start;

        foreach (Direction direction in Directions)
        {
            List<Vector2Int> path = WalkDirection(map, start, direction, StepTiles);
            if (path.Count == 0) continue;

            int score = CountRevealed(map, fog, path, visionRadius);
            uint tie = TieBreak(map.Seed, start, (int)direction);
            if (score > bestScore || (score == bestScore && score > 0 && tie < bestTie))
            {
                bestScore = score;
                bestTie = tie;
                bestTarget = path[path.Count - 1];
            }
        }

        if (bestScore > 0)
        {
            plan = new Plan(bestTarget, bestScore, false);
            return true;
        }

        return TryPlanToFrontier(map, fog, start, visionRadius, out plan);
    }

    /// <summary>
    /// Тайлы, которые пройдёт MoveAsync(direction, steps): по одному, пока следующий — суша.
    /// Стартовый тайл не входит.
    /// </summary>
    public static List<Vector2Int> WalkDirection(IMapDataProvider map, Vector2Int start, Direction direction, int steps)
    {
        var path = new List<Vector2Int>(steps);
        Vector2Int step = PlayerCommandExecutor.DirectionToVector(direction);
        Vector2Int current = start;

        for (int i = 0; i < steps; i++)
        {
            Vector2Int next = current + step;
            if (!map.IsWalkable(next.x, next.y)) break;
            path.Add(next);
            current = next;
        }

        return path;
    }

    /// <summary>
    /// Тайлы, которые пройдёт MoveToAsync(target): шаг по знаку разности (сначала диагональ), пока суша.
    /// Стартовый тайл не входит; путь может оборваться раньше цели.
    /// </summary>
    public static List<Vector2Int> WalkStraight(IMapDataProvider map, Vector2Int start, Vector2Int target)
    {
        var path = new List<Vector2Int>();
        Vector2Int current = start;

        while (current != target)
        {
            var next = new Vector2Int(
                current.x + System.Math.Sign(target.x - current.x),
                current.y + System.Math.Sign(target.y - current.y));
            if (!map.IsWalkable(next.x, next.y)) break;
            path.Add(next);
            current = next;
        }

        return path;
    }

    /// <summary>
    /// Сколько неоткрытых тайлов суши попадёт в обзор (квадрат visionRadius), если пройти по тайлам path.
    /// </summary>
    public static int CountRevealed(IMapDataProvider map, IFogProvider fog, List<Vector2Int> path, int visionRadius)
    {
        var revealed = new HashSet<Vector2Int>();
        foreach (Vector2Int center in path)
            for (int dx = -visionRadius; dx <= visionRadius; dx++)
                for (int dy = -visionRadius; dy <= visionRadius; dy++)
                {
                    int x = center.x + dx;
                    int y = center.y + dy;
                    if (IsUnexploredLand(map, fog, x, y))
                        revealed.Add(new Vector2Int(x, y));
                }
        return revealed.Count;
    }

    /// <summary>
    /// Неоткрытые тайлы суши в квадрате radius вокруг center — до и после шага разведки,
    /// чтобы вернуть игроку, сколько открыто на самом деле.
    /// </summary>
    public static int CountUnexploredLand(IMapDataProvider map, IFogProvider fog, Vector2Int center, int radius)
    {
        int count = 0;
        for (int x = center.x - radius; x <= center.x + radius; x++)
            for (int y = center.y - radius; y <= center.y + radius; y++)
                if (IsUnexploredLand(map, fog, x, y))
                    count++;
        return count;
    }

    // ─── Граница тумана ───────────────────────────────────────

    private static bool TryPlanToFrontier(
        IMapDataProvider map, IFogProvider fog, Vector2Int start, int visionRadius, out Plan plan)
    {
        plan = default;

        // BFS по суше: ближайший (по числу шагов) неоткрытый тайл; порядок соседей фиксирован — результат детерминирован
        var parents = new Dictionary<Vector2Int, Vector2Int>();
        var queue = new Queue<Vector2Int>();
        parents[start] = start;
        queue.Enqueue(start);
        Vector2Int? frontier = null;

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            if (current != start && !fog.IsExplored(current.x, current.y))
            {
                frontier = current;
                break;
            }

            foreach (Direction direction in Directions)
            {
                Vector2Int next = current + PlayerCommandExecutor.DirectionToVector(direction);
                if (parents.ContainsKey(next)) continue;
                if (ChebyshevDistance(start, next) > FrontierSearchRadius) continue;
                if (!map.IsWalkable(next.x, next.y)) continue;

                parents[next] = current;
                queue.Enqueue(next);
            }
        }

        if (frontier == null) return false;

        // Путь от старта к границе (без стартового тайла)
        var path = new List<Vector2Int>();
        for (Vector2Int node = frontier.Value; node != start; node = parents[node])
            path.Add(node);
        path.Reverse();

        // Самая дальняя точка пути в пределах шага, до которой MoveToAsync дойдёт по прямой.
        // Первый тайл пути — сосед старта, до него дойти можно всегда
        int limit = Mathf.Min(StepTiles, path.Count);
        for (int i = limit - 1; i >= 0; i--)
        {
            List<Vector2Int> straight = WalkStraight(map, start, path[i]);
            if (straight.Count == 0 || straight[straight.Count - 1] != path[i]) continue;

            plan = new Plan(path[i], CountRevealed(map, fog, straight, visionRadius), true);
            return true;
        }

        return false;
    }

    // ─── Helpers ──────────────────────────────────────────────

    private static bool IsUnexploredLand(IMapDataProvider map, IFogProvider fog, int x, int y)
    {
        return map.IsInBounds(x, y) && map.IsWalkable(x, y) && !fog.IsExplored(x, y);
    }

    private static int ChebyshevDistance(Vector2Int a, Vector2Int b)
    {
        return Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));
    }

    /// <summary>
    /// Детерминированный «жребий» при равном счёте: хеш seed карты, тайла и направления.
    /// </summary>
    private static uint TieBreak(int seed, Vector2Int tile, int direction)
    {
        unchecked
        {
            uint h = (uint)seed * 0x9E3779B1u;
            h ^= (uint)tile.x * 0x85EBCA6Bu;
            h = (h << 13) | (h >> 19);
            h ^= (uint)tile.y * 0xC2B2AE35u;
            h = (h << 11) | (h >> 21);
            h ^= (uint)(direction + 1) * 0x27D4EB2Fu;
            h ^= h >> 16;
            h *= 0x7FEB352Du;
            h ^= h >> 15;
            h *= 0x846CA68Bu;
            h ^= h >> 16;
            return h;
        }
    }
}
