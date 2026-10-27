using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Расстановка головоломок забега (GameDesign.md §10.4, T24). При старте резервирует места для всех типов
/// <see cref="PuzzleTypes.All"/> с количеством больше нуля (открытых и закрытых) детерминированно от
/// IMapDataProvider.Seed: на суше (с полем в 1 тайл), в тумане, не ближе minDistanceFromCamps к лагерям врагов,
/// со своими расстояниями от спавна и между собой. Сразу строятся только открытые (isUnlocked —
/// UnlockRegistry.IsMapEventUnlocked), остальные места ждут в <see cref="PuzzlePlan"/> — их строит
/// <see cref="PuzzleSpawner"/>, когда событие открывается посреди забега.
/// Порядок резерва прежний: сначала лабиринт (ему нужно больше места), затем площадки с плитами (5×5), затем
/// обелиски, — поэтому при всех открытых типах расстановка совпадает с прежней (до T24).
/// Постройка места — один путь (<see cref="Build"/>) и при старте, и позже. Объекты карты на месте
/// головоломки убираются в момент постройки.
/// </summary>
public static class PuzzleGenerator
{
    // Поле суши вокруг области головоломки (подход к входу / обелиску)
    internal const int LandMargin = 1;

    // ─── Public API ───────────────────────────────────────────

    /// <param name="fog">Туман (FogOfWarSystem; null — без проверки тумана, тесты).</param>
    /// <param name="camps">Центры лагерей врагов (EnemyManager.CampCenters).</param>
    /// <param name="salt">Соль входных данных задач (разная в каждой загрузке сцены).</param>
    /// <returns>План: все места и список ожидания; null — нечего генерировать (нет конфига/карты).</returns>
    public static PuzzlePlan Generate(
        PuzzleConfig config, IMapDataProvider map, IFogProvider fog, ObjectManager objects,
        IReadOnlyList<Vector2> camps, Vector2Int spawnTile, Func<string, bool> isUnlocked, PuzzleManager manager,
        int salt)
    {
        if (config == null || map == null || manager == null || isUnlocked == null) return null;

        // Свой сдвиг seed, чтобы не совпадать с биомами, объектами и лагерями
        var random = new System.Random(unchecked(map.Seed * 31 + 7919));
        var context = new Context(config, map, fog, objects, camps, new Vector2(spawnTile.x, spawnTile.y));
        Transform container = new GameObject("Puzzles").transform;
        var plan = new PuzzlePlan(context, container, manager, salt);

        ReserveAll(context, random, plan);

        // Строим открытые; порядок постройки на random не влияет (у каждого места свой seed раскладки)
        foreach (PuzzleSlot slot in plan.Slots)
            if (isUnlocked(slot.TypeId))
                plan.Build(slot);

        // Стены и обелиски должны сразу участвовать в проверках движения
        Physics2D.SyncTransforms();
        return plan;
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

    // ─── Reserve (старт забега) ───────────────────────────────

    /// <summary>
    /// Места для всех типов в прежнем порядке и с прежней последовательностью random.
    /// Id выдаются подряд всем местам (и ждущим), поэтому остаются уникальными.
    /// </summary>
    private static void ReserveAll(Context context, System.Random random, PuzzlePlan plan)
    {
        PuzzleConfig config = context.Config;
        int nextId = 1;

        if (config.mazeCount > 0)
        {
            if (config.mazeWallPrefab == null)
                Debug.LogWarning("PuzzleGenerator: PuzzleConfig.mazeWallPrefab is not assigned — no mazes");
            else
                for (int i = 0; i < config.mazeCount; i++)
                {
                    PuzzleSlot slot = ReserveMaze(context, random, nextId, null);
                    if (slot == null)
                    {
                        Debug.LogWarning($"PuzzleGenerator: no place for a maze (spawn ≥ {config.mazeMinDistanceFromSpawn}, " +
                                         $"camps ≥ {config.minDistanceFromCamps})");
                        break;
                    }
                    plan.AddSlot(slot);
                    nextId++;
                }
        }

        // Плиты (T16): площадка 5×5, префаб необязателен. При platesCount = 0 random не трогаем —
        // расстановка остальных головоломок не меняется
        if (config.platesCount > 0)
        {
            for (int i = 0; i < config.platesCount; i++)
            {
                PuzzleSlot slot = ReservePlates(context, random, nextId, null);
                if (slot == null)
                {
                    Debug.LogWarning("PuzzleGenerator: no place for pressure plates " +
                                     $"(spawn ≥ {config.platesMinDistanceFromSpawn}, camps ≥ {config.minDistanceFromCamps})");
                    break;
                }
                plan.AddSlot(slot);
                nextId++;
            }
        }

        if (config.codeTaskCount > 0)
        {
            if (config.obeliskPrefab == null)
                Debug.LogWarning("PuzzleGenerator: PuzzleConfig.obeliskPrefab is not assigned — no code tasks");
            else
            {
                List<CodeTaskKind> kinds = PickKinds(random, config.codeTaskCount);
                foreach (CodeTaskKind kind in kinds)
                {
                    PuzzleSlot slot = ReserveObelisk(context, random, nextId, kind, null);
                    if (slot == null)
                    {
                        Debug.LogWarning("PuzzleGenerator: no place for a code task obelisk " +
                                         $"(spawn ≥ {config.codeTaskMinDistanceFromSpawn}, between ≥ {config.codeTaskMinSpacing})");
                        break;
                    }
                    plan.AddSlot(slot);
                    nextId++;
                }
            }
        }
    }

    // ─── Relocate (посреди забега) ────────────────────────────

    /// <summary>
    /// Новое место для ждущего слота по тем же правилам, что при старте, но вся область с полем должна быть
    /// ни разу не открыта и areaBlocked (враги) — false. Id, вид задачи и seed раскладки сохраняются.
    /// Random — от seed карты и id места (детерминированно при одинаковом тумане). Null — места нет
    /// (старое резервирование тогда остаётся как было).
    /// </summary>
    internal static PuzzleSlot Relocate(Context context, PuzzleSlot slot, Func<RectInt, bool> areaBlocked)
    {
        var random = new System.Random(unchecked(context.Map.Seed * 31 + 7919 + slot.Id * 104729));

        context.Release(slot);
        context.RequireUnexplored = true;
        context.AreaBlocked = areaBlocked;
        try
        {
            PuzzleSlot moved;
            switch (slot.TypeId)
            {
                case PuzzleTypes.Maze: moved = ReserveMaze(context, random, slot.Id, slot.LayoutSeed); break;
                case PuzzleTypes.Plates: moved = ReservePlates(context, random, slot.Id, slot.LayoutSeed); break;
                case PuzzleTypes.CodeTask:
                    moved = ReserveObelisk(context, random, slot.Id, slot.Kind, slot.LayoutSeed);
                    break;
                default: moved = null; break;
            }

            if (moved == null) context.Occupy(slot);
            return moved;
        }
        finally
        {
            context.RequireUnexplored = false;
            context.AreaBlocked = null;
        }
    }

    /// <summary>
    /// Можно ли строить ждущее место там, где оно зарезервировано: вся область с полем ни разу не открыта,
    /// areaBlocked (враги) — false.
    /// </summary>
    internal static bool IsStillHidden(Context context, PuzzleSlot slot, Func<RectInt, bool> areaBlocked)
    {
        RectInt withMargin = Expand(slot.Tiles, LandMargin);
        if (context.Fog != null)
            for (int x = withMargin.xMin; x < withMargin.xMax; x++)
                for (int y = withMargin.yMin; y < withMargin.yMax; y++)
                    if (context.Fog.IsExplored(x, y)) return false;

        return areaBlocked == null || !areaBlocked(withMargin);
    }

    // ─── Reserve: по типам ────────────────────────────────────
    // layoutSeed = null — старт: seed раскладки берётся из random в том же месте последовательности, что и раньше

    private static PuzzleSlot ReserveMaze(Context context, System.Random random, int id, int? layoutSeed)
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

            // Стартовая платформа — вход лабиринта (внутри области): раскладка детерминирована от seed и
            // совпадёт с той, что построит Build; random при этом не тратится
            int seed = layoutSeed ?? random.Next();
            MazeLayout layout = MazeLayout.Generate(new System.Random(seed), config.mazeMinPathLength);
            var slot = new PuzzleSlot(id, PuzzleTypes.Maze, tiles, center, TileBounds(tiles), default,
                seed, origin + layout.Entrance);
            context.Occupy(slot);
            return slot;
        }
        return null;
    }

    private static PuzzleSlot ReservePlates(Context context, System.Random random, int id, int? layoutSeed)
    {
        PuzzleConfig config = context.Config;
        List<Vector2Int> origins = context.ShuffledLand(random);
        int size = PlatesLayout.Size;

        foreach (Vector2Int origin in origins)
        {
            var tiles = new RectInt(origin.x, origin.y, size, size);
            Vector2Int centerTile = origin + PlatesLayout.Center;
            Vector2 center = new Vector2(centerTile.x, centerTile.y);
            if (Vector2.Distance(center, context.Spawn) < config.platesMinDistanceFromSpawn) continue;
            if (!context.IsFree(tiles)) continue;
            if (!context.TryPickStartTile(TileBounds(tiles), allowTop: true, out Vector2Int startTile)) continue;

            var slot = new PuzzleSlot(id, PuzzleTypes.Plates, tiles, center, TileBounds(tiles), default,
                layoutSeed ?? random.Next(), startTile);
            context.Occupy(slot);
            return slot;
        }
        return null;
    }

    private static PuzzleSlot ReserveObelisk(
        Context context, System.Random random, int id, CodeTaskKind kind, int? layoutSeed)
    {
        PuzzleConfig config = context.Config;
        if (config.obeliskPrefab == null) return null;
        Rect footprint = ObjectFootprint.Get(config.obeliskPrefab.transform);
        List<Vector2Int> candidates = context.ShuffledLand(random);

        foreach (Vector2Int tile in candidates)
        {
            Vector2 position = new Vector2(tile.x, tile.y);
            if (Vector2.Distance(position, context.Spawn) < config.codeTaskMinDistanceFromSpawn) continue;
            if (context.IsTooCloseToObelisk(position)) continue;

            Rect area = new Rect(footprint.position + position, footprint.size);
            RectInt tiles = ToTiles(area);
            if (!context.IsFree(tiles)) continue;
            // Над обелиском платформу закрыл бы его спрайт — только снизу и по бокам
            if (!context.TryPickStartTile(area, allowTop: false, out Vector2Int startTile)) continue;

            var slot = new PuzzleSlot(id, PuzzleTypes.CodeTask, tiles, position, area, kind,
                layoutSeed ?? random.Next(), startTile);
            context.Occupy(slot);
            return slot;
        }
        return null;
    }

    // ─── Build (общий путь: старт и посреди забега) ───────────

    /// <summary>
    /// Строит головоломку на месте слота: убирает объекты карты под областью с полем, создаёт объект,
    /// лабиринту — «густая изгородь» тумана, и стартовую платформу (T25) на slot.StartTile.
    /// Регистрацию в PuzzleManager и SyncTransforms делает вызывающий. Null — не из чего строить (нет префаба).
    /// </summary>
    internal static PuzzleStructure Build(Context context, PuzzleSlot slot, Transform container, int salt)
    {
        PuzzleStructure puzzle = BuildStructure(context, slot, container, salt);
        if (puzzle == null) return null;

        // Платформа — отдельный объект в контейнере (уничтожается вместе с головоломкой)
        GameObject platform = PuzzleStartPlatform.Create(
            container, slot.StartTile, context.Map, context.Config.startPlatformPrefab);
        puzzle.SetStartPlatform(slot.StartTile, platform);
        return puzzle;
    }

    private static PuzzleStructure BuildStructure(Context context, PuzzleSlot slot, Transform container, int salt)
    {
        PuzzleConfig config = context.Config;
        switch (slot.TypeId)
        {
            case PuzzleTypes.Maze:
            {
                if (config.mazeWallPrefab == null) return null;
                MazeLayout layout = MazeLayout.Generate(new System.Random(slot.LayoutSeed), config.mazeMinPathLength);
                RemoveObjects(context, slot);

                var root = new GameObject($"Maze_{slot.Id}");
                root.transform.SetParent(container, false);
                root.transform.position = context.Map.MapToWorld(slot.Position);
                MazePuzzle maze = root.AddComponent<MazePuzzle>();
                maze.Setup(slot.Id, slot.Position);
                maze.Build(layout, slot.Tiles.position, context.Map, config);

                // Изгородь — только у настоящего тумана (в тестах туман — заглушка IFogProvider)
                if (context.Fog is FogOfWarSystem fogSystem && fogSystem != null)
                    maze.AddHedge(fogSystem, config.mazeVisionRadius);
                return maze;
            }

            case PuzzleTypes.Plates:
            {
                PlatesLayout layout = PlatesLayout.Generate(new System.Random(slot.LayoutSeed));
                // По площадке ходят — объекты с неё и с поля вокруг убираем
                RemoveObjects(context, slot);

                var root = new GameObject($"Plates_{slot.Id}");
                root.transform.SetParent(container, false);
                root.transform.position = context.Map.MapToWorld(slot.Position);
                PlatesPuzzle plates = root.AddComponent<PlatesPuzzle>();
                plates.Setup(slot.Id, slot.Position);
                plates.Build(layout, slot.Tiles.position, context.Map, config.platePrefab);
                return plates;
            }

            case PuzzleTypes.CodeTask:
            {
                if (config.obeliskPrefab == null) return null;
                Rect footprint = ObjectFootprint.Get(config.obeliskPrefab.transform);
                RemoveObjects(context, slot);

                GameObject instance = UnityEngine.Object.Instantiate(
                    config.obeliskPrefab, context.Map.MapToWorld(slot.Position), Quaternion.identity, container);
                instance.name = $"Obelisk_{slot.Id}_{CodeTask.ToId(slot.Kind)}";
                if (!instance.TryGetComponent(out CodeTaskPuzzle obelisk))
                    obelisk = instance.AddComponent<CodeTaskPuzzle>();
                obelisk.Setup(slot.Id, slot.Position);
                obelisk.Initialize(slot.Kind, unchecked(slot.LayoutSeed ^ salt), footprint);
                return obelisk;
            }

            default:
                return null;
        }
    }

    // ─── Helpers ──────────────────────────────────────────────

    private static void RemoveObjects(Context context, PuzzleSlot slot)
    {
        if (context.Objects != null)
            context.Objects.RemoveObjectsInArea(Expand(slot.Tiles, LandMargin));
    }

    /// <summary>Прямоугольник карты, который занимают тайлы (как Bounds у лабиринта и плит).</summary>
    private static Rect TileBounds(RectInt tiles) =>
        new Rect(tiles.xMin - 0.5f, tiles.yMin - 0.5f, tiles.width, tiles.height);

    /// <summary>Тайлы, на которые заходит прямоугольник карты (точка — один тайл).</summary>
    private static RectInt ToTiles(Rect area)
    {
        Vector2Int min = MapPosition.ToTile(area.min);
        Vector2Int max = MapPosition.ToTile(area.max);
        return new RectInt(min.x, min.y, max.x - min.x + 1, max.y - min.y + 1);
    }

    /// <summary>Прямоугольник тайлов с полем margin со всех сторон.</summary>
    public static RectInt Expand(RectInt tiles, int margin) =>
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

    /// <summary>Прямоугольник карты заходит на тайл (касание краем не считается — как PuzzleStructure.ContainsTile).</summary>
    private static bool OverlapsTile(Rect rect, Vector2Int tile) =>
        rect.xMin < tile.x + 0.5f && rect.xMax > tile.x - 0.5f
        && rect.yMin < tile.y + 0.5f && rect.yMax > tile.y - 0.5f;

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

    /// <summary>
    /// Общие данные расстановки: карта, ограничения и занятые места (построенные и ждущие).
    /// Живёт весь забег внутри <see cref="PuzzlePlan"/> — поздние головоломки учитывают все места.
    /// </summary>
    internal sealed class Context
    {
        public readonly PuzzleConfig Config;
        public readonly IMapDataProvider Map;
        public readonly IFogProvider Fog;
        public readonly ObjectManager Objects;
        public readonly IReadOnlyList<Vector2> Camps;
        public readonly Vector2 Spawn;

        /// <summary>Поиск посреди забега: тайлы должны быть ни разу не открыты (при старте — просто не видны).</summary>
        public bool RequireUnexplored;
        /// <summary>Доп. запрет области с полем (враги в ней); null — нет.</summary>
        public Func<RectInt, bool> AreaBlocked;

        private readonly List<PuzzleSlot> _occupied = new List<PuzzleSlot>();
        private List<Vector2Int> _land;

        public Context(PuzzleConfig config, IMapDataProvider map, IFogProvider fog, ObjectManager objects,
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
                    if (Fog != null && (RequireUnexplored ? Fog.IsExplored(x, y) : Fog.IsVisible(x, y)))
                        return false;
                }

            Rect area = TileBounds(tiles);
            if (Camps != null)
                foreach (Vector2 camp in Camps)
                    if (DistanceToRect(camp, area) < Config.minDistanceFromCamps)
                        return false;

            foreach (PuzzleSlot other in _occupied)
            {
                if (RectGap(area, other.OccupiedRect) < Config.minDistanceBetweenAreas)
                    return false;
                // Платформа другой головоломки не должна оказаться под новой (T25; при minDistanceBetweenAreas
                // ≥ 2 этого не бывает — расстановка прежняя)
                if (tiles.Contains(other.StartTile))
                    return false;
            }

            return AreaBlocked == null || !AreaBlocked(withMargin);
        }

        /// <summary>
        /// Стартовая платформа плит / обелиска (T25): середина стороны вплотную к области, ближайшая к спавну
        /// (при равенстве — снизу, слева, справа, сверху); суша, не в области и не на платформе другой
        /// головоломки. Тайл лежит в поле LandMargin, которое IsFree уже проверил. Random не тратится.
        /// Считается от прямоугольника на карте (Bounds), а не от тайлов: у обелиска след коллайдера по краю
        /// задевает лишний ряд тайлов, а платформа должна стоять вплотную к самому обелиску.
        /// </summary>
        public bool TryPickStartTile(Rect bounds, bool allowTop, out Vector2Int startTile)
        {
            Vector2Int center = MapPosition.ToTile(bounds.center);
            int below = MapPosition.ToTile(new Vector2(0f, bounds.yMin - 0.5f)).y;
            int above = MapPosition.ToTile(new Vector2(0f, bounds.yMax + 0.5f)).y;
            int left = MapPosition.ToTile(new Vector2(bounds.xMin - 0.5f, 0f)).x;
            int right = MapPosition.ToTile(new Vector2(bounds.xMax + 0.5f, 0f)).x;
            var candidates = new List<Vector2Int>(4)
            {
                new Vector2Int(center.x, below),
                new Vector2Int(left, center.y),
                new Vector2Int(right, center.y),
            };
            if (allowTop) candidates.Add(new Vector2Int(center.x, above));

            startTile = default;
            float best = float.MaxValue;
            bool found = false;
            foreach (Vector2Int tile in candidates)
            {
                // Не под своей же областью (коллайдер обелиска, заходящий в нижний тайл) — иначе платформа под ним
                if (OverlapsTile(bounds, tile) || !IsStartTileFree(tile)) continue;
                float distance = Vector2.Distance(new Vector2(tile.x, tile.y), Spawn);
                // Строго меньше — при равенстве остаётся раньше перечисленная сторона
                if (distance < best - 0.0001f)
                {
                    best = distance;
                    startTile = tile;
                    found = true;
                }
            }
            return found;
        }

        private bool IsStartTileFree(Vector2Int tile)
        {
            if (!Map.IsWalkable(tile.x, tile.y)) return false;
            foreach (PuzzleSlot other in _occupied)
            {
                if (other.StartTile == tile) return false;
                if (OverlapsTile(other.OccupiedRect, tile)) return false;
            }
            return true;
        }

        /// <summary>Обелиск ближе codeTaskMinSpacing к другому (построенному или ждущему).</summary>
        public bool IsTooCloseToObelisk(Vector2 position)
        {
            foreach (PuzzleSlot other in _occupied)
                if (other.TypeId == PuzzleTypes.CodeTask
                    && Vector2.Distance(position, other.Position) < Config.codeTaskMinSpacing)
                    return true;
            return false;
        }

        public void Occupy(PuzzleSlot slot) => _occupied.Add(slot);

        public void Release(PuzzleSlot slot) => _occupied.Remove(slot);
    }
}
