using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

public class ObjectGenerator : MonoBehaviour
{
    [System.Serializable]
    public class SpawnEntry
    {
        public ResourceObject prefab;
        public int count;
    }

    public event Action<ResourceObject, Vector2Int> OnObjectSpawned;

    [SerializeField] private List<SpawnEntry> spawnEntries;

    private IMapDataProvider _mapData;
    private ObjectPool _objectPool;
    private Vector2 _spawnCenter;

    // ─── Инициализация ────────────────────────────────────────

    public void Initialize(
        IMapDataProvider mapData,
        ObjectPool objectPool,
        Vector2Int spawnCenter)
    {
        ValidateReferences(mapData, objectPool);

        _mapData = mapData;
        _objectPool = objectPool;
        _spawnCenter = new Vector2(spawnCenter.x, spawnCenter.y);

        GenerateAll();
    }

    private void ValidateReferences(IMapDataProvider mapData, ObjectPool objectPool)
    {
        Debug.Assert(mapData != null, "ObjectGenerator: mapData is not assigned");
        Debug.Assert(objectPool != null, "ObjectGenerator: objectPool is not assigned");
        Debug.Assert(spawnEntries != null && spawnEntries.Count > 0,
            "ObjectGenerator: spawnEntries is empty");
    }

    // ─── Generation ───────────────────────────────────────────

    private void GenerateAll()
    {
        foreach (SpawnEntry entry in spawnEntries)
        {
            if (entry.prefab == null) continue;

            for (int i = 0; i < entry.count; i++)
                TrySpawnObject(entry.prefab);
        }
    }

    private void TrySpawnObject(ResourceObject prefab)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            int tileX = Random.Range(0, _mapData.MapWidth);
            int tileY = Random.Range(0, _mapData.MapHeight);

            if (!IsValidSpawnPosition(tileX, tileY, prefab)) continue;

            Vector3 worldPos = _mapData.TileIndexToWorld(tileX, tileY);
            ResourceObject obj = _objectPool.Get(prefab, worldPos);
            Vector2Int tile = new Vector2Int(tileX, tileY);
            obj.SetTile(tile);

            obj.OnConsumed += HandleObjectConsumed;
            OnObjectSpawned?.Invoke(obj, tile);
            return;
        }
    }

    private void HandleObjectConsumed(ResourceObject obj)
    {
        obj.OnConsumed -= HandleObjectConsumed;
        _objectPool.Return(obj);
    }

    private bool IsValidSpawnPosition(int tileX, int tileY, ResourceObject prefab)
    {
        // Проверяем тайл и соседей в радиусе 1
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
                if (!_mapData.IsWalkable(tileX + dx, tileY + dy))
                    return false;

        float distance = Vector2.Distance(
            new Vector2(tileX, tileY),
            _spawnCenter);

        ResourceVariant variant = prefab.Variant;
        if (variant == null) return true;

        return distance >= variant.minSpawnDistance &&
               distance <= variant.maxSpawnDistance;
    }
}