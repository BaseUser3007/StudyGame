using System;
using System.Collections.Generic;
using UnityEngine;

public class ObjectManager : MonoBehaviour
{
    [SerializeField] private ObjectGenerator objectGenerator;
    [SerializeField] private Transform poolContainer;

    private ObjectPool _pool;
    private Dictionary<Vector2Int, ResourceObject> _objects
        = new Dictionary<Vector2Int, ResourceObject>();

    // ─── Инициализация ────────────────────────────────────────

    public void Initialize(IMapDataProvider mapData, Vector2Int spawnCenter)
    {
        ValidateReferences();

        _pool = new ObjectPool(poolContainer);

        objectGenerator.Initialize(mapData, _pool, spawnCenter);
        objectGenerator.OnObjectSpawned += HandleObjectSpawned;
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

    private void HandleObjectSpawned(ResourceObject obj, Vector2Int tile)
    {
        _objects[tile] = obj;
        obj.OnConsumed += _ => _objects.Remove(tile);
    }

    // ─── Public API ───────────────────────────────────────────

    /// <summary>
    /// Возвращает все объекты указанного типа в радиусе от центра.
    /// </summary>
    public List<T> GetObjectsInRadius<T>(Vector2Int center, int radius)
        where T : ResourceObject
    {
        List<T> result = new List<T>();

        for (int x = -radius; x <= radius; x++)
            for (int y = -radius; y <= radius; y++)
            {
                Vector2Int tile = new Vector2Int(center.x + x, center.y + y);

                if (_objects.TryGetValue(tile, out ResourceObject obj)
                    && obj.IsAvailable
                    && obj is T typed)
                    result.Add(typed);
            }

        return result;
    }

    /// <summary>
    /// Возвращает ближайший объект указанного типа к центру.
    /// </summary>
    public T GetNearestObject<T>(Vector2Int center, int radius)
        where T : ResourceObject
    {
        List<T> objects = GetObjectsInRadius<T>(center, radius);

        if (objects.Count == 0) return null;

        T nearest = null;
        float minDistance = float.MaxValue;

        foreach (T obj in objects)
        {
            Vector2Int objTile = new Vector2Int(
                Mathf.RoundToInt(obj.transform.position.x),
                Mathf.RoundToInt(obj.transform.position.y));

            float distance = Vector2Int.Distance(center, objTile);

            if (distance < minDistance)
            {
                minDistance = distance;
                nearest = obj;
            }
        }

        return nearest;
    }

    public List<ResourceObject> GetObjectsByType(Vector2Int center, int radius, Type type)
    {
        List<ResourceObject> result = new List<ResourceObject>();

        for (int x = -radius; x <= radius; x++)
            for (int y = -radius; y <= radius; y++)
            {
                Vector2Int tile = new Vector2Int(center.x + x, center.y + y);

                if (_objects.TryGetValue(tile, out ResourceObject obj)
                    && obj.IsAvailable
                    && obj.GetType() == type)
                    result.Add(obj);
            }

        return result;
    }
}