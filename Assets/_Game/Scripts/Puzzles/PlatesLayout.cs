using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Расположение плит головоломки plates (T16): площадка <see cref="Size"/>×<see cref="Size"/>,
/// 4–5 плит с уникальными номерами 1..<see cref="MaxNumber"/>. Чистый класс, детерминирован от System.Random.
/// Плиты не касаются друг друга даже углами (сойдя с плиты, не наступишь на другую), в каждой строке и каждом
/// столбце площадки — не больше одной плиты (к плите можно пройти по прямой от края площадки по её строке,
/// не задев другие), и к каждой можно подойти снаружи по клеткам без плит.
/// Координаты — локальные (0,0 — нижний левый тайл площадки).
/// </summary>
public sealed class PlatesLayout
{
    public const int Size = 5;
    public const int MinPlates = 4;
    public const int MaxPlates = 5;
    /// <summary>Номера плит — из 1..MaxNumber (не подряд: порядок надо выяснить сортировкой).</summary>
    public const int MaxNumber = 30;

    private const int MaxAttempts = 2000;

    private readonly List<Vector2Int> _tiles;
    private readonly List<int> _numbers;

    /// <summary>Локальные тайлы плит (индекс плиты — индекс в списке).</summary>
    public IReadOnlyList<Vector2Int> Tiles => _tiles;
    /// <summary>Номер каждой плиты (по индексу), все разные.</summary>
    public IReadOnlyList<int> Numbers => _numbers;
    public int Count => _tiles.Count;
    /// <summary>Центр площадки (локально).</summary>
    public static Vector2Int Center => new Vector2Int(Size / 2, Size / 2);

    private PlatesLayout(List<Vector2Int> tiles, List<int> numbers)
    {
        _tiles = tiles;
        _numbers = numbers;
    }

    // ─── Public API ───────────────────────────────────────────

    /// <summary>
    /// Новое расположение. count — число плит (MinPlates..MaxPlates); 0 — случайно в этих пределах.
    /// </summary>
    public static PlatesLayout Generate(System.Random random, int count = 0)
    {
        if (random == null) throw new ArgumentNullException(nameof(random));
        count = count <= 0
            ? random.Next(MinPlates, MaxPlates + 1)
            : Mathf.Clamp(count, MinPlates, MaxPlates);

        var all = new List<Vector2Int>(Size * Size);
        for (int x = 0; x < Size; x++)
            for (int y = 0; y < Size; y++)
                all.Add(new Vector2Int(x, y));

        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            Shuffle(all, random);
            var tiles = new List<Vector2Int>(count);
            foreach (Vector2Int tile in all)
            {
                if (TouchesAny(tile, tiles)) continue;
                tiles.Add(tile);
                if (tiles.Count == count) break;
            }

            if (tiles.Count == count && AllReachable(tiles))
                return new PlatesLayout(tiles, PickNumbers(random, count));
        }

        // Не случится при 4–5 плитах на 5×5, но на всякий случай — фиксированная расстановка
        // (строки и столбцы разные, углами не касаются)
        var fallback = new List<Vector2Int>
        {
            new Vector2Int(0, 0), new Vector2Int(2, 1), new Vector2Int(4, 2), new Vector2Int(1, 3), new Vector2Int(3, 4)
        };
        fallback.RemoveRange(count, fallback.Count - count);
        return new PlatesLayout(fallback, PickNumbers(random, count));
    }

    /// <summary>Индекс плиты на локальном тайле или −1.</summary>
    public int IndexOf(Vector2Int local) => _tiles.IndexOf(local);

    public static bool InBounds(Vector2Int local) =>
        local.x >= 0 && local.y >= 0 && local.x < Size && local.y < Size;

    /// <summary>
    /// К каждой плите можно подойти снаружи площадки по клеткам без плит (шаги по сторонам):
    /// плита на краю — сразу снаружи, иначе у неё есть соседняя свободная клетка, связанная с краем.
    /// </summary>
    public static bool AllReachable(IReadOnlyList<Vector2Int> plates)
    {
        var isPlate = new bool[Size, Size];
        foreach (Vector2Int p in plates)
            if (InBounds(p)) isPlate[p.x, p.y] = true;

        // Свободные клетки, достижимые снаружи: старт — свободные клетки края
        var reached = new bool[Size, Size];
        var queue = new Queue<Vector2Int>();
        for (int x = 0; x < Size; x++)
            for (int y = 0; y < Size; y++)
                if (!isPlate[x, y] && IsEdge(x, y))
                {
                    reached[x, y] = true;
                    queue.Enqueue(new Vector2Int(x, y));
                }

        while (queue.Count > 0)
        {
            Vector2Int cell = queue.Dequeue();
            foreach (Vector2Int dir in Directions)
            {
                Vector2Int next = cell + dir;
                if (!InBounds(next) || isPlate[next.x, next.y] || reached[next.x, next.y]) continue;
                reached[next.x, next.y] = true;
                queue.Enqueue(next);
            }
        }

        foreach (Vector2Int p in plates)
        {
            if (IsEdge(p.x, p.y)) continue;
            bool approachable = false;
            foreach (Vector2Int dir in Directions)
            {
                Vector2Int n = p + dir;
                if (InBounds(n) && reached[n.x, n.y]) approachable = true;
            }
            if (!approachable) return false;
        }
        return true;
    }

    // ─── Helpers ──────────────────────────────────────────────

    private static readonly Vector2Int[] Directions =
        { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

    private static bool IsEdge(int x, int y) => x == 0 || y == 0 || x == Size - 1 || y == Size - 1;

    // Мешает ли тайл уже выбранным плитам: касается стороной/углом или стоит в той же строке/столбце
    private static bool TouchesAny(Vector2Int tile, List<Vector2Int> chosen)
    {
        foreach (Vector2Int other in chosen)
        {
            if (other.x == tile.x || other.y == tile.y) return true;
            if (Mathf.Abs(other.x - tile.x) <= 1 && Mathf.Abs(other.y - tile.y) <= 1) return true;
        }
        return false;
    }

    private static List<int> PickNumbers(System.Random random, int count)
    {
        var pool = new List<int>(MaxNumber);
        for (int n = 1; n <= MaxNumber; n++) pool.Add(n);
        Shuffle(pool, random);
        return pool.GetRange(0, count);
    }

    private static void Shuffle<T>(List<T> list, System.Random random)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            T tmp = list[i];
            list[i] = list[j];
            list[j] = tmp;
        }
    }
}
