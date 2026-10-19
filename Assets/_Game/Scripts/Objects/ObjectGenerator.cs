using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Раскладывает ресурсы по карте: в каждом биоме — свои ресурсы со своей плотностью
/// (настраивается в BiomeConfig, который назначен в MapGenerationConfig). В забеге затем досыпает
/// стартовый набор (MapGenerationConfig.starterResources) в кольце вокруг спавна, видимом сразу.
/// </summary>
public class ObjectGenerator : MonoBehaviour
{
    public event Action<ResourceObject> OnObjectSpawned;

    [Tooltip("Минимальное расстояние между объектами (в тайлах)")]
    [SerializeField] private float minObjectSpacing = 0.75f;

    [Tooltip("Вокруг точки появления персонажа объекты не создаются (в тайлах). " +
             "Считается до края коллайдера объекта")]
    [SerializeField] private float spawnClearRadius = 3f;

    private const int SpawnAttempts = 30;
    // Кольцо стартового набора маленькое — попыток на объект больше
    private const int StarterSpawnAttempts = 100;
    // Объект лежит внутри своего тайла, чуть отступив от границы
    private const float TileMargin = 0.1f;

    private IMapDataProvider _mapData;
    private ObjectPool _objectPool;
    private Vector2 _spawnCenter;
    // null — стартовый набор не нужен (лагерь)
    private MapGenerationConfig _starterConfig;
    // От seed карты: при фиксированном seed объекты раскладываются так же, как карта и биомы
    private System.Random _random;
    // Позиции созданных объектов по тайлам — для быстрой проверки расстояния
    private readonly Dictionary<Vector2Int, List<Vector2>> _positionsByTile
        = new Dictionary<Vector2Int, List<Vector2>>();

    // ─── Инициализация ────────────────────────────────────────

    /// <param name="starterConfig">Откуда брать стартовый набор у спавна; null — не досыпать (лагерь).</param>
    public void Initialize(
        IMapDataProvider mapData,
        ObjectPool objectPool,
        Vector2Int spawnCenter,
        MapGenerationConfig starterConfig)
    {
        ValidateReferences(mapData, objectPool);

        _mapData = mapData;
        _objectPool = objectPool;
        _spawnCenter = new Vector2(spawnCenter.x, spawnCenter.y);
        _starterConfig = starterConfig;
        // Сдвиг, чтобы последовательность не совпадала с той, что раздала биомы
        _random = new System.Random(unchecked(mapData.Seed * 31 + 7919));

        List<ResourceObject> generated = GenerateAll();
        // После обычной генерации и тем же _random — детерминированно от seed
        GenerateStarterResources(generated);
    }

    private void ValidateReferences(IMapDataProvider mapData, ObjectPool objectPool)
    {
        Debug.Assert(mapData != null, "ObjectGenerator: mapData is not assigned");
        Debug.Assert(objectPool != null, "ObjectGenerator: objectPool is not assigned");
    }

    // ─── Generation ───────────────────────────────────────────

    /// <summary>
    /// Обычная раскладка по биомам. Возвращает созданные объекты (их учитывает стартовый набор).
    /// </summary>
    private List<ResourceObject> GenerateAll()
    {
        var generated = new List<ResourceObject>();
        Dictionary<Biome, List<Vector2Int>> tilesByBiome = CollectLandTiles();

        if (tilesByBiome.Count == 0)
            Debug.LogWarning("ObjectGenerator: на карте нет биомов — ресурсы не созданы. " +
                             "Назначьте BiomeConfig в MapGenerationConfig");

        foreach (var pair in tilesByBiome)
        {
            Biome biome = pair.Key;
            List<Vector2Int> tiles = pair.Value;
            // Тайлы, куда помещается объект с данной площадью коллайдера, — считаем один раз на префаб
            var candidatesByPrefab = new Dictionary<ResourceObject, List<Vector2Int>>();

            foreach (BiomeResource resource in biome.resources)
            {
                if (resource == null || resource.prefab == null || resource.density <= 0f) continue;

                // Плотность — от всей площади биома, а место ищем только там, где объект помещается
                int count = RollCount(resource.density * tiles.Count / 100f);
                Rect footprint = ObjectFootprint.Get(resource.prefab);

                if (!candidatesByPrefab.TryGetValue(resource.prefab, out List<Vector2Int> candidates))
                    candidatesByPrefab[resource.prefab] = candidates = FilterTiles(tiles, footprint);

                int spawned = 0;
                for (int i = 0; i < count; i++)
                {
                    ResourceObject obj = TrySpawnObject(resource.prefab, footprint, candidates);
                    if (obj == null) continue;
                    generated.Add(obj);
                    spawned++;
                }

                if (spawned < count)
                    Debug.LogWarning(
                        $"ObjectGenerator: {biome.name}/{resource.prefab.name} — создано {spawned} из {count}, " +
                        "не хватает свободного места");
            }
        }

        return generated;
    }

    // ─── Starter Resources ────────────────────────────────────

    /// <summary>
    /// Гарантированный набор в обзоре со спавна: для каждого типа из starterResources досыпает объекты
    /// до count в кольце [max(starterMinRadius, spawnClearRadius), starterMaxRadius]; уже лежащие не дальше
    /// starterMaxRadius объекты того же типа засчитываются. Правила места — как у обычной генерации.
    /// </summary>
    private void GenerateStarterResources(List<ResourceObject> generated)
    {
        if (_starterConfig == null || _starterConfig.starterResources == null) return;

        // Тип объекта — как ключ пула; создаём первый префаб этого типа из списка
        var prefabByType = new Dictionary<Type, ResourceObject>();
        var required = new List<(Type key, int count)>();
        foreach (StarterResource resource in _starterConfig.starterResources)
        {
            if (resource == null || resource.prefab == null || resource.count <= 0) continue;

            Type type = resource.prefab.GetType();
            if (!prefabByType.ContainsKey(type)) prefabByType[type] = resource.prefab;
            required.Add((type, resource.count));
        }

        if (required.Count == 0) return;

        float minRadius = Mathf.Max(_starterConfig.starterMinRadius, spawnClearRadius);
        float maxRadius = _starterConfig.starterMaxRadius;
        if (maxRadius <= minRadius)
        {
            Debug.LogWarning($"ObjectGenerator: starterMaxRadius ({maxRadius}) должен быть больше " +
                             $"starterMinRadius/spawnClearRadius ({minRadius}) — стартовый набор не создан");
            return;
        }

        var existing = new List<(Type key, Vector2 position)>(generated.Count);
        foreach (ResourceObject obj in generated)
            existing.Add((obj.GetType(), obj.Position));

        foreach ((Type type, int missing) in
                 StarterResourcePlanner.GetMissing(required, existing, _spawnCenter, maxRadius))
        {
            ResourceObject prefab = prefabByType[type];
            Rect footprint = ObjectFootprint.Get(prefab);

            int spawned = 0;
            for (int i = 0; i < missing; i++)
                if (TrySpawnInRing(prefab, footprint, minRadius, maxRadius) != null)
                    spawned++;

            if (spawned < missing)
                Debug.LogWarning(
                    $"ObjectGenerator: стартовый набор {prefab.name} — создано {spawned} из {missing}, " +
                    "у спавна не хватает свободной суши");
        }
    }

    private ResourceObject TrySpawnInRing(ResourceObject prefab, Rect footprint, float minRadius, float maxRadius)
    {
        for (int attempt = 0; attempt < StarterSpawnAttempts; attempt++)
        {
            Vector2 position = StarterResourcePlanner.RandomPointInRing(_random, _spawnCenter, minRadius, maxRadius);

            // Точка может выйти за край кольца из-за округления float
            if (!StarterResourcePlanner.IsInRing(_spawnCenter, position, minRadius, maxRadius)) continue;
            // Суша под коллайдером, зона спавна (до края коллайдера), minObjectSpacing
            if (!IsValidSpawnPosition(position, footprint)) continue;

            return SpawnAt(prefab, position);
        }

        return null;
    }

    /// <summary>
    /// Тайлы суши, сгруппированные по биомам.
    /// </summary>
    private Dictionary<Biome, List<Vector2Int>> CollectLandTiles()
    {
        var result = new Dictionary<Biome, List<Vector2Int>>();

        for (int x = 0; x < _mapData.MapWidth; x++)
        {
            for (int y = 0; y < _mapData.MapHeight; y++)
            {
                if (!_mapData.IsWalkable(x, y)) continue;

                Biome biome = _mapData.GetBiome(x, y);
                if (biome == null) continue;

                if (!result.TryGetValue(biome, out List<Vector2Int> tiles))
                    result[biome] = tiles = new List<Vector2Int>();
                tiles.Add(new Vector2Int(x, y));
            }
        }

        return result;
    }

    /// <summary>
    /// Дробная часть ожидаемого количества превращается в шанс ещё одного объекта.
    /// </summary>
    private int RollCount(float expected)
    {
        int count = Mathf.FloorToInt(expected);
        if (_random.NextDouble() < expected - count) count++;
        return count;
    }

    private ResourceObject TrySpawnObject(ResourceObject prefab, Rect footprint, List<Vector2Int> tiles)
    {
        if (tiles.Count == 0) return null;

        for (int attempt = 0; attempt < SpawnAttempts; attempt++)
        {
            Vector2Int tile = tiles[_random.Next(tiles.Count)];
            Vector2 position = RandomPointInTile(tile);

            if (!IsValidSpawnPosition(position, footprint)) continue;

            return SpawnAt(prefab, position);
        }

        return null;
    }

    /// <summary>
    /// Создаёт объект в точке карты без проверок места (например, выброшенный предмет).
    /// Объект попадает в ObjectManager и возвращается в пул, когда его соберут.
    /// </summary>
    public ResourceObject SpawnAt(ResourceObject prefab, Vector2 position)
    {
        ResourceObject obj = _objectPool.Get(prefab, _mapData.MapToWorld(position));
        obj.SetPosition(position);
        AddPosition(position);

        obj.OnConsumed += HandleObjectConsumed;
        OnObjectSpawned?.Invoke(obj);
        return obj;
    }

    private Vector2 RandomPointInTile(Vector2Int tile)
    {
        float half = 0.5f - TileMargin;
        return new Vector2(
            tile.x + RandomRange(-half, half),
            tile.y + RandomRange(-half, half));
    }

    private float RandomRange(float min, float max)
    {
        return min + (float)_random.NextDouble() * (max - min);
    }

    private void HandleObjectConsumed(ResourceObject obj)
    {
        obj.OnConsumed -= HandleObjectConsumed;
        RemovePosition(obj.Position);
        _objectPool.Return(obj);
    }

    // ─── Placement ────────────────────────────────────────────

    /// <summary>
    /// footprint — площадь коллайдера объекта относительно его позиции (см. ObjectFootprint).
    /// Под коллайдером должна быть только суша, и он не должен задевать зону спавна.
    /// Остальная картинка (крона дерева) может свисать куда угодно.
    /// </summary>
    private bool IsValidSpawnPosition(Vector2 position, Rect footprint)
    {
        Rect area = PlaceFootprint(footprint, position);

        if (DistanceToRect(_spawnCenter, area) < spawnClearRadius)
            return false;

        if (!IsLandUnder(area))
            return false;

        Vector2Int tile = MapPosition.ToTile(position);
        int range = Mathf.CeilToInt(minObjectSpacing);
        for (int x = -range; x <= range; x++)
            for (int y = -range; y <= range; y++)
            {
                if (!_positionsByTile.TryGetValue(new Vector2Int(tile.x + x, tile.y + y), out List<Vector2> list))
                    continue;

                foreach (Vector2 other in list)
                    if (Vector2.Distance(position, other) < minObjectSpacing)
                        return false;
            }

        return true;
    }

    /// <summary>
    /// Грубый предварительный отбор: объект, поставленный в центр тайла, стоит на суше
    /// и тайл не лежит целиком внутри зоны спавна. Точная проверка — в IsValidSpawnPosition.
    /// </summary>
    private List<Vector2Int> FilterTiles(List<Vector2Int> tiles, Rect footprint)
    {
        var result = new List<Vector2Int>(tiles.Count);
        // Дальше всего от центра тайла точка объекта может отойти на (0.5 - TileMargin) · √2
        float maxShift = (0.5f - TileMargin) * 1.4143f;

        foreach (Vector2Int tile in tiles)
        {
            Rect area = PlaceFootprint(footprint, tile);
            if (DistanceToRect(_spawnCenter, area) + maxShift < spawnClearRadius) continue;
            if (!IsLandUnder(area)) continue;
            result.Add(tile);
        }

        return result;
    }

    private static Rect PlaceFootprint(Rect footprint, Vector2 position)
    {
        return new Rect(footprint.position + position, footprint.size);
    }

    /// <summary>
    /// Все тайлы, которые задевает прямоугольник, — суша (за краем карты — вода).
    /// </summary>
    private bool IsLandUnder(Rect area)
    {
        Vector2Int min = MapPosition.ToTile(area.min);
        Vector2Int max = MapPosition.ToTile(area.max);

        for (int x = min.x; x <= max.x; x++)
            for (int y = min.y; y <= max.y; y++)
                if (!_mapData.IsWalkable(x, y))
                    return false;
        return true;
    }

    private static float DistanceToRect(Vector2 point, Rect rect)
    {
        Vector2 closest = new Vector2(
            Mathf.Clamp(point.x, rect.xMin, rect.xMax),
            Mathf.Clamp(point.y, rect.yMin, rect.yMax));
        return Vector2.Distance(point, closest);
    }

    private void AddPosition(Vector2 position)
    {
        Vector2Int tile = MapPosition.ToTile(position);
        if (!_positionsByTile.TryGetValue(tile, out List<Vector2> list))
            _positionsByTile[tile] = list = new List<Vector2>();
        list.Add(position);
    }

    private void RemovePosition(Vector2 position)
    {
        if (_positionsByTile.TryGetValue(MapPosition.ToTile(position), out List<Vector2> list))
            list.Remove(position);
    }

    // ─── Gizmos ───────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        // Точка спавна известна только после генерации карты
        if (_mapData == null) return;

        Gizmos.color = Color.yellow;
        DrawCircle(_mapData.MapToWorld(_spawnCenter), spawnClearRadius);
    }

    private static void DrawCircle(Vector3 center, float radius)
    {
        const int segments = 64;
        Vector3 prev = center + new Vector3(radius, 0f, 0f);

        for (int i = 1; i <= segments; i++)
        {
            float angle = i * 2f * Mathf.PI / segments;
            Vector3 next = center + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }
}
