using UnityEngine;

public class SpawnPoint : MonoBehaviour
{
    [SerializeField] private Vector2Int spawnCenter = new Vector2Int(50, 50);

    private IMapDataProvider _mapData;

    public void Initialize(IMapDataProvider mapData)
    {
        Debug.Assert(mapData != null,
            "SpawnPoint: mapData is not assigned");

        _mapData = mapData;
    }

    public Vector2Int GetSpawnTile()
    {
        if (_mapData.IsWalkable(spawnCenter.x, spawnCenter.y))
            return spawnCenter;

        return SearchBySpiral();
    }

    private Vector2Int SearchBySpiral()
    {
        int maxRadius = Mathf.Max(_mapData.MapWidth, _mapData.MapHeight);

        for (int radius = 1; radius < maxRadius; radius++)
        {
            Vector2Int? result = SearchAtRadius(radius);
            if (result.HasValue) return result.Value;
        }

        Debug.LogWarning("SpawnPoint: no walkable tile found, using center as fallback");
        return spawnCenter;
    }

    private Vector2Int? SearchAtRadius(int radius)
    {
        for (int x = -radius; x <= radius; x++)
        {
            for (int y = -radius; y <= radius; y++)
            {
                if (Mathf.Abs(x) != radius && Mathf.Abs(y) != radius)
                    continue;

                int tileX = spawnCenter.x + x;
                int tileY = spawnCenter.y + y;

                if (_mapData.IsWalkable(tileX, tileY))
                    return new Vector2Int(tileX, tileY);
            }
        }

        return null;
    }
}