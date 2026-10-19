using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Расстановка головоломок при старте забега (GameDesign.md §10.4). Только открытые типы (isUnlocked —
/// UnlockRegistry.IsMapEventUnlocked), детерминированно от IMapDataProvider.Seed: на суше (с полем в 1 тайл),
/// в тумане, не ближе minDistanceFromCamps к лагерям врагов, со своими расстояниями от спавна и между собой.
/// Вызывается из Level после врагов. Объекты карты на месте головоломки убираются.
/// Сначала лабиринт (ему нужно больше места), затем обелиски.
/// </summary>
public static class PuzzleGenerator
{
    // Поле суши вокруг области головоломки (подход к входу / обелиску)
    private const int LandMargin = 1;

    // ─── Public API ───────────────────────────────────────────

    /// <param name="camps">Центры лагерей врагов (EnemyManager.CampCenters).</param>
    /// <param name="salt">Соль входных данных задач (разная в каждой загрузке сцены).</param>
    public static void Generate(
        PuzzleConfig config, IMapDataProvider map, FogOfWarSystem fog, ObjectManager objects,
        IReadOnlyList<Vector2> camps, Vector2Int spawnTile, Func<string, bool> isUnlocked, PuzzleManager manager,
        int salt)
    {
        if (config == null || map == null || manager == null || isUnlocked == null) return;

        // Свой сдвиг seed, чтобы не совпадать с биомами, объектами и лагерями
        var random = new System.Random(unchecked(map.Seed * 31 + 7919));
        var context = new Context(config, map, fog, objects, camps, new Vector2(spawnTile.x, spawnTile.y));
        Transform container = new GameObject("Puzzles").transform;
        int nextId = 1;

        if (config.mazeCount > 0 && isUnlocked(PuzzleTypes.Maze))
        {
            if (config.mazeWallPrefab == null)
                Debug.LogWarning("PuzzleGenerator: PuzzleConfig.mazeWallPrefab is not assigned — no mazes");
            else
                for (int i = 0; i < config.mazeCount; i++)
                {
                    MazePuzzle maze = PlaceMaze(context, random, container, nextId);
                    if (maze == null) break;
                    manager.Register(maze);
                    nextId++;
                }
        }

        if (config.codeTaskCount > 0 && isUnlocked(PuzzleTypes.CodeTask))
        {
            if (config.obeliskPrefab == null)
                Debug.LogWarning("PuzzleGenerator: PuzzleConfig.obeliskPrefab is not assigned — no code tasks");
            else
            {
                List<CodeTaskKind> kinds = PickKinds(random, config.codeTaskCount);
                foreach (CodeTaskKind kind in kinds)
                {
                    CodeTaskPuzzle obelisk = PlaceObelisk(context, random, container, nextId, kind, salt);
                    if (obelisk == null) break;
                    manager.Register(obelisk);
                    nextId++;
                }
            }
        }

        // Стены и обелиски должны сразу участвовать в проверках движения
        Physics2D.SyncTransforms();
    }

    /// <summary>
    /// Разные виды задач: перемешанные sum/max/count, при count больше трёх — по кругу.
    /// </summary>
    public static List<CodeTaskKind> PickKinds(System.Random random, int count)
    {
        var all = new List<CodeTaskKind> { CodeTaskKind.Sum, CodeTaskKind.Max, CodeTaskKind.Count };
        Shuffle(all, random);

        var result = new List<CodeTaskKind>(Mathf.Max(0, count));
        for (int i = 0; i < count; i++)
            result.Add(all[i % all.Count]);
        return result;
    }

    // ─── Maze ─────────────────────────────────────────────────

    private static MazePuzzle PlaceMaze(Context context, System.Random random, Transform container, int id)
    {
        PuzzleConfig config = context.Config;
        List<Vector2Int> origins = context.ShuffledLand(random);
        int size = MazeLayout.Size;

        foreach (Vector2Int origin in origins)
        {
            var tiles = new RectInt(origin.x, origin.y, size, size);
            Vector2 center = new Vector2(origin.x + size / 2, origin.y + size / 2);
            if (Vector2.Distance(center, context.Spawn) < config.mazeMinDistanceFromSpawn) continue;
            if (!context.IsFree(tiles)) continue;

            MazeLayout layout = MazeLayout.Generate(new System.Random(random.Next()), config.mazeMinPathLength);

            if (context.Objects != null)
                context.Objects.RemoveObjectsInArea(Expand(tiles, LandMargin));

            var root = new GameObject($"Maze_{id}");
            root.transform.SetParent(container, false);
            root.transform.position = context.Map.MapToWorld(center);
            MazePuzzle maze = root.AddComponent<MazePuzzle>();
            maze.Setup(id, center);
            maze.Build(layout, origin, context.Map, config);

            if (context.Fog != null)
                context.Fog.AddHedgeArea(maze.InnerArea, config.mazeVisionRadius);

            context.Occupy(maze.Bounds, center);
            return maze;
        }

        Debug.LogWarning($"PuzzleGenerator: no place for a maze (spawn ≥ {config.mazeMinDistanceFromSpawn}, " +
                         $"camps ≥ {config.minDistanceFromCamps})");
        return null;
    }

    // ─── Code task ────────────────────────────────────────────

    private static CodeTaskPuzzle PlaceObelisk(
        Context context, System.Random random, Transform container, int id, CodeTaskKind kind, int salt)
    {
        PuzzleConfig config = context.Config;
        Rect footprint = ObjectFootprint.Get(config.obeliskPrefab.transform);
        List<Vector2Int> candidates = context.ShuffledLand(random);

        foreach (Vector2Int tile in candidates)
        {
            Vector2 position = new Vector2(tile.x, tile.y);
            if (Vector2.Distance(position, context.Spawn) < config.codeTaskMinDistanceFromSpawn) continue;

            bool tooClose = false;
            foreach (Vector2 other in context.Obelisks)
                if (Vector2.Distance(position, other) < config.codeTaskMinSpacing)
                {
                    tooClose = true;
                    break;
                }
            if (tooClose) continue;

            Rect area = new Rect(footprint.position + position, footprint.size);
            RectInt tiles = ToTiles(area);
            if (!context.IsFree(tiles)) continue;

            if (context.Objects != null)
                context.Objects.RemoveObjectsInArea(Expand(tiles, LandMargin));

            GameObject instance = UnityEngine.Object.Instantiate(
                config.obeliskPrefab, context.Map.MapToWorld(position), Quaternion.identity, container);
            instance.name = $"Obelisk_{id}_{CodeTask.ToId(kind)}";
            if (!instance.TryGetComponent(out CodeTaskPuzzle obelisk))
                obelisk = instance.AddComponent<CodeTaskPuzzle>();
            obelisk.Setup(id, position);
            obelisk.Initialize(kind, unchecked(random.Next() ^ salt), footprint);

            context.Occupy(obelisk.Bounds, position);
            context.Obelisks.Add(position);
            return obelisk;
        }

        Debug.LogWarning("PuzzleGenerator: no place for a code task obelisk " +
                         $"(spawn ≥ {config.codeTaskMinDistanceFromSpawn}, between ≥ {config.codeTaskMinSpacing})");
        return null;
    }

    // ─── Helpers ──────────────────────────────────────────────

    /// <summary>Тайлы, на которые заходит прямоугольник карты (точка — один тайл).</summary>
    private static RectInt ToTiles(Rect area)
    {
        Vector2Int min = MapPosition.ToTile(area.min);
        Vector2Int max = MapPosition.ToTile(area.max);
        return new RectInt(min.x, min.y, max.x - min.x + 1, max.y - min.y + 1);
    }

    private static RectInt Expand(RectInt tiles, int margin) =>
        new RectInt(tiles.xMin - margin, tiles.yMin - margin, tiles.width + margin * 2, tiles.height + margin * 2);

    private static float DistanceToRect(Vector2 point, Rect rect)
    {
        Vector2 closest = new Vector2(
            Mathf.Clamp(point.x, rect.xMin, rect.xMax),
            Mathf.Clamp(point.y, rect.yMin, rect.yMax));
        return Vector2.Distance(point, closest);
    }

    /// <summary>Расстояние между прямоугольниками (0 — пересекаются).</summary>
    private static float RectGap(Rect a, Rect b)
    {
        float dx = Mathf.Max(0f, Mathf.Max(a.xMin - b.xMax, b.xMin - a.xMax));
        float dy = Mathf.Max(0f, Mathf.Max(a.yMin - b.yMax, b.yMin - a.yMax));
        return Mathf.Sqrt(dx * dx + dy * dy);
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

    /// <summary>Общие данные одной генерации: карта, ограничения и уже занятые области.</summary>
    private sealed class Context
    {
        public readonly PuzzleConfig Config;
        public readonly IMapDataProvider Map;
        public readonly FogOfWarSystem Fog;
        public readonly ObjectManager Objects;
        public readonly IReadOnlyList<Vector2> Camps;
        public readonly Vector2 Spawn;
        public readonly List<Vector2> Obelisks = new List<Vector2>();

        private readonly List<Rect> _occupied = new List<Rect>();
        private List<Vector2Int> _land;

        public Context(PuzzleConfig config, IMapDataProvider map, FogOfWarSystem fog, ObjectManager objects,
            IReadOnlyList<Vector2> camps, Vector2 spawn)
        {
            Config = config;
            Map = map;
            Fog = fog;
            Objects = objects;
            Camps = camps;
            Spawn = spawn;
        }

        /// <summary>Тайлы суши (обход x, y), перемешанные random — детерминированно от seed.</summary>
        public List<Vector2Int> ShuffledLand(System.Random random)
        {
            if (_land == null)
            {
                _land = new List<Vector2Int>();
                for (int x = 0; x < Map.MapWidth; x++)
                    for (int y = 0; y < Map.MapHeight; y++)
                        if (Map.IsWalkable(x, y))
                            _land.Add(new Vector2Int(x, y));
            }

            var copy = new List<Vector2Int>(_land);
            Shuffle(copy, random);
            return copy;
        }

        /// <summary>
        /// Область (с полем LandMargin) — суша под туманом, далеко от лагерей и других головоломок.
        /// </summary>
        public bool IsFree(RectInt tiles)
        {
            RectInt withMargin = Expand(tiles, LandMargin);
            for (int x = withMargin.xMin; x < withMargin.xMax; x++)
                for (int y = withMargin.yMin; y < withMargin.yMax; y++)
                {
                    if (!Map.IsWalkable(x, y)) return false;
                    if (Fog != null && Fog.IsVisible(x, y)) return false;
                }

            Rect area = new Rect(tiles.xMin - 0.5f, tiles.yMin - 0.5f, tiles.width, tiles.height);
            if (Camps != null)
                foreach (Vector2 camp in Camps)
                    if (DistanceToRect(camp, area) < Config.minDistanceFromCamps)
                        return false;

            foreach (Rect other in _occupied)
                if (RectGap(area, other) < Config.minDistanceBetweenAreas)
                    return false;

            return true;
        }

        public void Occupy(Rect bounds, Vector2 position)
        {
            // Без площади (обелиск без коллайдера) — тайл позиции
            if (bounds.width <= 0f || bounds.height <= 0f)
                bounds = new Rect(position.x - 0.5f, position.y - 0.5f, 1f, 1f);
            _occupied.Add(bounds);
        }
    }
}
