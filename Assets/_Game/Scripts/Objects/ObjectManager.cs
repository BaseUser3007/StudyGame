using System;
using System.Collections.Generic;
using UnityEngine;

public class ObjectManager : MonoBehaviour
{
    [SerializeField] private ObjectGenerator objectGenerator;
    [SerializeField] private Transform poolContainer;

    /// <summary>Объект добавлен в индекс (генерация, выброс, добыча на землю).</summary>
    public event Action<ResourceObject> OnObjectAdded;

    private ObjectPool _pool;
    private IMapDataProvider _mapData;
    // Объекты лежат в свободных точках; индекс — по тайлу, в котором они находятся
    private readonly Dictionary<Vector2Int, List<ResourceObject>> _objectsByTile
        = new Dictionary<Vector2Int, List<ResourceObject>>();
    // Доступные объекты по id, который видит Lua (GetInstanceID) — объект мог уйти от своей точки (овца)
    private readonly Dictionary<int, ResourceObject> _objectsById = new Dictionary<int, ResourceObject>();

    // ─── Инициализация ────────────────────────────────────────

    /// <param name="starterConfig">Стартовый набор у спавна (только забег); null — без него.</param>
    /// <param name="keepClear">Прямоугольник карты без ресурсов (портал забега с полем); null — нет.</param>
    public void Initialize(IMapDataProvider mapData, Vector2Int spawnCenter, MapGenerationConfig starterConfig,
        Rect? keepClear = null)
    {
        ValidateReferences();

        _mapData = mapData;
        _pool = new ObjectPool(poolContainer);

        // Подписываемся до генерации, иначе объекты не попадут в индекс
        objectGenerator.OnObjectSpawned += HandleObjectSpawned;
        objectGenerator.Initialize(mapData, _pool, spawnCenter, starterConfig, keepClear);
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
        _objectsById[obj.GetInstanceID()] = obj;
        obj.OnConsumed += HandleObjectConsumed;
        OnObjectAdded?.Invoke(obj);
    }

    private void HandleObjectConsumed(ResourceObject obj)
    {
        obj.OnConsumed -= HandleObjectConsumed;

        if (_objectsByTile.TryGetValue(obj.Tile, out List<ResourceObject> list))
            list.Remove(obj);
        _objectsById.Remove(obj.GetInstanceID());
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

    /// <summary>
    /// Убирает с карты все объекты, чья точка лежит в прямоугольнике тайлов (место под головоломку).
    /// Возвращает, сколько убрано.
    /// </summary>
    public int RemoveObjectsInArea(RectInt tiles)
    {
        var buffer = new List<ResourceObject>();
        for (int x = tiles.xMin; x < tiles.xMax; x++)
            for (int y = tiles.yMin; y < tiles.yMax; y++)
                buffer.AddRange(GetObjectsAtTile(new Vector2Int(x, y)));

        foreach (ResourceObject obj in buffer)
            obj.Despawn();
        return buffer.Count;
    }

    /// <summary>
    /// Доступный объект по id из Lua (GetInstanceID), или null — собран или такого нет.
    /// </summary>
    public ResourceObject GetById(int id)
    {
        return _objectsById.TryGetValue(id, out ResourceObject obj) && obj != null && obj.IsAvailable ? obj : null;
    }

    /// <summary>
    /// Все доступные объекты типа T на карте (без учёта поля зрения).
    /// </summary>
    public List<T> GetAll<T>() where T : ResourceObject
    {
        var result = new List<T>();
        foreach (ResourceObject obj in _objectsById.Values)
            if (obj is T typed && obj.IsAvailable)
                result.Add(typed);
        return result;
    }

    /// <summary>
    /// Переносит объект в новую точку карты (подвижные объекты — овцы): transform, порядок отрисовки,
    /// индекс по тайлам и учёт позиций генератора. Проверки места — на вызывающем.
    /// </summary>
    public void Move(ResourceObject obj, Vector2 newPosition)
    {
        if (obj == null || !obj.IsAvailable || _mapData == null) return;

        Vector2 oldPosition = obj.Position;
        Vector2Int oldTile = obj.Tile;

        Vector3 world = _mapData.MapToWorld(newPosition);
        obj.transform.position = new Vector3(world.x, world.y, obj.transform.position.z);
        obj.SetPosition(newPosition);
        objectGenerator.MovePosition(oldPosition, newPosition);

        Vector2Int newTile = obj.Tile;
        if (newTile == oldTile) return;

        if (_objectsByTile.TryGetValue(oldTile, out List<ResourceObject> oldList))
            oldList.Remove(obj);
        if (!_objectsByTile.TryGetValue(newTile, out List<ResourceObject> newList))
            _objectsByTile[newTile] = newList = new List<ResourceObject>();
        newList.Add(obj);
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