using System;
using System.Collections.Generic;
using UnityEngine;

public class ObjectManager : MonoBehaviour
{
    [SerializeField] private ObjectGenerator objectGenerator;
    [SerializeField] private Transform poolContainer;

    private ObjectPool _pool;
    // Объекты лежат в свободных точках; индекс — по тайлу, в котором они находятся
    private readonly Dictionary<Vector2Int, List<ResourceObject>> _objectsByTile
        = new Dictionary<Vector2Int, List<ResourceObject>>();

    // ─── Инициализация ────────────────────────────────────────

    public void Initialize(IMapDataProvider mapData, Vector2Int spawnCenter)
    {
        ValidateReferences();

        _pool = new ObjectPool(poolContainer);

        // Подписываемся до генерации, иначе объекты не попадут в индекс
        objectGenerator.OnObjectSpawned += HandleObjectSpawned;
        objectGenerator.Initialize(mapData, _pool, spawnCenter);
    }

    private void OnDestroy()
    {
        if (objectGenerator != null)
            objectGenerator.OnObjectSpawned -= HandleObjectSpawned;
    }

    private void ValidateReferences()
    {
        Debug.Assert(objectGenerator != null, "ObjectManager: objectGenerator is not assigned");
        Debug.Assert(poolContainer != null, "ObjectManager: poolContainer is not assigned");
    }

    // ─── Object Tracking ──────────────────────────────────────

    private void HandleObjectSpawned(ResourceObject obj)
    {
        Vector2Int tile = obj.Tile;

        if (!_objectsByTile.TryGetValue(tile, out List<ResourceObject> list))
        {
            list = new List<ResourceObject>();
            _objectsByTile[tile] = list;
        }

        list.Add(obj);
        obj.OnConsumed += HandleObjectConsumed;
    }

    private void HandleObjectConsumed(ResourceObject obj)
    {
        obj.OnConsumed -= HandleObjectConsumed;

        if (_objectsByTile.TryGetValue(obj.Tile, out List<ResourceObject> list))
            list.Remove(obj);
    }

    /// <summary>
    /// Доступные объекты в тайлах квадрата radius вокруг center (как поле зрения).
    /// </summary>
    private IEnumerable<ResourceObject> EnumerateObjectsAround(Vector2Int center, int radius)
    {
        for (int x = -radius; x <= radius; x++)
            for (int y = -radius; y <= radius; y++)
            {
                Vector2Int tile = new Vector2Int(center.x + x, center.y + y);
                if (!_objectsByTile.TryGetValue(tile, out List<ResourceObject> list))
                    continue;

                foreach (ResourceObject obj in list)
                    if (obj.IsAvailable)
                        yield return obj;
            }
    }

    // ─── Public API ───────────────────────────────────────────

    /// <summary>
    /// Ближайший к точке доступный объект не дальше maxDistance, или null.
    /// </summary>
    public ResourceObject GetNearestObjectAt(Vector2 point, float maxDistance)
    {
        int radius = Mathf.CeilToInt(maxDistance);
        ResourceObject nearest = null;
        float minDistance = maxDistance;

        foreach (ResourceObject obj in EnumerateObjectsAround(MapPosition.ToTile(point), radius))
        {
            float distance = Vector2.Distance(point, obj.Position);

            if (distance <= minDistance)
            {
                minDistance = distance;
                nearest = obj;
            }
        }

        return nearest;
    }

    /// <summary>
    /// Возвращает все объекты указанного типа в радиусе от центра.
    /// </summary>
    public List<T> GetObjectsInRadius<T>(Vector2Int center, int radius)
        where T : ResourceObject
    {
        List<T> result = new List<T>();

        foreach (ResourceObject obj in EnumerateObjectsAround(center, radius))
            if (obj is T typed)
                result.Add(typed);

        return result;
    }

    /// <summary>
    /// Возвращает ближайший объект указанного типа к центру.
    /// </summary>
    public T GetNearestObject<T>(Vector2Int center, int radius)
        where T : ResourceObject
    {
        T nearest = null;
        float minDistance = float.MaxValue;

        foreach (T obj in GetObjectsInRadius<T>(center, radius))
        {
            float distance = Vector2.Distance(center, obj.Position);

            if (distance < minDistance)
            {
                minDistance = distance;
                nearest = obj;
            }
        }

        return nearest;
    }

    /// <summary>
    /// Создаёт объект на карте во время игры (выброшенный предмет и т.п.).
    /// </summary>
    public ResourceObject Spawn(ResourceObject prefab, Vector2 position)
    {
        return objectGenerator.SpawnAt(prefab, position);
    }

    /// <summary>
    /// Доступные объекты, которые находятся в тайле (по точке объекта, как в индексе).
    /// </summary>
    public List<ResourceObject> GetObjectsAtTile(Vector2Int tile)
    {
        return new List<ResourceObject>(EnumerateObjectsAround(tile, 0));
    }

    public List<ResourceObject> GetObjectsByType(Vector2Int center, int radius, Type type)
    {
        List<ResourceObject> result = new List<ResourceObject>();

        foreach (ResourceObject obj in EnumerateObjectsAround(center, radius))
            if (obj.GetType() == type)
                result.Add(obj);

        return result;
    }
}