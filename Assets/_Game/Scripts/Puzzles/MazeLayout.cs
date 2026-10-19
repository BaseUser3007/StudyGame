using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// План лабиринта 7×7 тайлов (идеальный лабиринт 3×3 клетки, GameDesign.md §10.4) в локальных координатах
/// 0…6. Клетки — тайлы с нечётными координатами, между ними — стены или проходы; внешнее кольцо — стена
/// с одним входом в середине стороны. Цель — центральный тайл (3, 3).
/// Генерация — recursive backtracker от System.Random (детерминирована от seed). Чистая логика без сцены.
/// </summary>
public sealed class MazeLayout
{
    public const int Size = 7;
    public const int Cells = 3;

    private static readonly Vector2Int[] CellSteps =
    {
        new Vector2Int(0, 1), new Vector2Int(1, 0), new Vector2Int(0, -1), new Vector2Int(-1, 0)
    };

    private readonly bool[,] _walls;

    /// <summary>Тайл входа на внешнем кольце (локально).</summary>
    public Vector2Int Entrance { get; }
    /// <summary>Тайл снаружи лабиринта перед входом (локально, за пределами 0…6).</summary>
    public Vector2Int Approach { get; }
    /// <summary>Цель — центр.</summary>
    public Vector2Int Center => new Vector2Int(Size / 2, Size / 2);
    /// <summary>Длина кратчайшего пути от входа до центра (в шагах по тайлам).</summary>
    public int PathLength { get; }

    private MazeLayout(bool[,] walls, Vector2Int entrance, Vector2Int approach)
    {
        _walls = walls;
        Entrance = entrance;
        Approach = approach;
        PathLength = ShortestPath(entrance, Center);
    }

    // ─── Public API ───────────────────────────────────────────

    public bool IsWall(int x, int y) => InBounds(x, y) && _walls[x, y];
    public bool IsWall(Vector2Int local) => IsWall(local.x, local.y);
    public bool IsOpen(int x, int y) => InBounds(x, y) && !_walls[x, y];

    public static bool InBounds(int x, int y) => x >= 0 && x < Size && y >= 0 && y < Size;

    /// <summary>
    /// Лабиринт с путём от входа до центра не короче minPathLength шагов (иначе вход может вести
    /// прямо в центр). Не нашлось за maxAttempts — самый длинный из сгенерированных.
    /// </summary>
    public static MazeLayout Generate(System.Random random, int minPathLength = 7, int maxAttempts = 64)
    {
        if (random == null) throw new ArgumentNullException(nameof(random));

        MazeLayout best = null;
        for (int attempt = 0; attempt < Mathf.Max(1, maxAttempts); attempt++)
        {
            MazeLayout layout = GenerateOnce(random);
            if (best == null || layout.PathLength > best.PathLength) best = layout;
            if (layout.PathLength >= minPathLength) return layout;
        }
        return best;
    }

    /// <summary>
    /// Кратчайший путь по открытым тайлам (4 направления) или −1, если пути нет.
    /// </summary>
    public int ShortestPath(Vector2Int from, Vector2Int to)
    {
        if (!IsOpen(from.x, from.y) || !IsOpen(to.x, to.y)) return -1;

        var distance = new int[Size, Size];
        for (int x = 0; x < Size; x++)
            for (int y = 0; y < Size; y++)
                distance[x, y] = -1;

        var queue = new Queue<Vector2Int>();
        distance[from.x, from.y] = 0;
        queue.Enqueue(from);

        while (queue.Count > 0)
        {
            Vector2Int tile = queue.Dequeue();
            if (tile == to) return distance[tile.x, tile.y];

            foreach (Vector2Int step in CellSteps)
            {
                Vector2Int next = tile + step;
                if (!IsOpen(next.x, next.y) || distance[next.x, next.y] >= 0) continue;

                distance[next.x, next.y] = distance[tile.x, tile.y] + 1;
                queue.Enqueue(next);
            }
        }
        return -1;
    }

    /// <summary>Сколько открытых тайлов достижимо от входа (для проверки связности).</summary>
    public int CountReachableFromEntrance()
    {
        var visited = new bool[Size, Size];
        var stack = new Stack<Vector2Int>();
        stack.Push(Entrance);
        visited[Entrance.x, Entrance.y] = true;
        int count = 0;

        while (stack.Count > 0)
        {
            Vector2Int tile = stack.Pop();
            count++;
            foreach (Vector2Int step in CellSteps)
            {
                Vector2Int next = tile + step;
                if (!IsOpen(next.x, next.y) || visited[next.x, next.y]) continue;
                visited[next.x, next.y] = true;
                stack.Push(next);
            }
        }
        return count;
    }

    public int CountOpen()
    {
        int count = 0;
        for (int x = 0; x < Size; x++)
            for (int y = 0; y < Size; y++)
                if (!_walls[x, y]) count++;
        return count;
    }

    // ─── Generation ───────────────────────────────────────────

    private static MazeLayout GenerateOnce(System.Random random)
    {
        var walls = new bool[Size, Size];
        for (int x = 0; x < Size; x++)
            for (int y = 0; y < Size; y++)
                walls[x, y] = true;

        // Recursive backtracker по клеткам 3×3 (стеком, без рекурсии)
        var visited = new bool[Cells, Cells];
        var stack = new Stack<Vector2Int>();
        var start = new Vector2Int(random.Next(Cells), random.Next(Cells));
        visited[start.x, start.y] = true;
        OpenCell(walls, start);
        stack.Push(start);

        var options = new List<Vector2Int>(4);
        while (stack.Count > 0)
        {
            Vector2Int cell = stack.Peek();
            options.Clear();
            foreach (Vector2Int step in CellSteps)
            {
                Vector2Int next = cell + step;
                if (next.x >= 0 && next.x < Cells && next.y >= 0 && next.y < Cells && !visited[next.x, next.y])
                    options.Add(next);
            }

            if (options.Count == 0)
            {
                stack.Pop();
                continue;
            }

            Vector2Int chosen = options[random.Next(options.Count)];
            visited[chosen.x, chosen.y] = true;
            OpenCell(walls, chosen);
            // Проход между клетками — тайл посередине
            Vector2Int a = CellToTile(cell), b = CellToTile(chosen);
            walls[(a.x + b.x) / 2, (a.y + b.y) / 2] = false;
            stack.Push(chosen);
        }

        // Вход — середина случайной стороны внешнего кольца
        int mid = Size / 2;
        Vector2Int entrance, approach;
        switch (random.Next(4))
        {
            case 0: entrance = new Vector2Int(mid, 0); approach = new Vector2Int(mid, -1); break;
            case 1: entrance = new Vector2Int(mid, Size - 1); approach = new Vector2Int(mid, Size); break;
            case 2: entrance = new Vector2Int(0, mid); approach = new Vector2Int(-1, mid); break;
            default: entrance = new Vector2Int(Size - 1, mid); approach = new Vector2Int(Size, mid); break;
        }
        walls[entrance.x, entrance.y] = false;

        return new MazeLayout(walls, entrance, approach);
    }

    private static Vector2Int CellToTile(Vector2Int cell) => new Vector2Int(cell.x * 2 + 1, cell.y * 2 + 1);

    private static void OpenCell(bool[,] walls, Vector2Int cell)
    {
        Vector2Int tile = CellToTile(cell);
        walls[tile.x, tile.y] = false;
    }
}
