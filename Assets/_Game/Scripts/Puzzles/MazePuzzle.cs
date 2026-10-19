using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Лабиринт 7×7 (maze): стены — экземпляры mazeWallPrefab с Collider2D (движение игрока их обходит
/// через Collider2D.Cast), в центре — сундук. Решено, когда игрок встал на центральный тайл
/// (PuzzleManager следит за PlayerEventBus.OnPlayerMoved). Корневой объект без DepthSortedSprite —
/// у каждой стены своя сортировка.
/// </summary>
public class MazePuzzle : PuzzleStructure
{
    private MazeLayout _layout;
    private Vector2Int _origin;
    private SpriteRenderer _chestRenderer;
    private Sprite _openedChestSprite;
    private readonly List<GameObject> _walls = new List<GameObject>();

    public override string TypeId => PuzzleTypes.Maze;
    public MazeLayout Layout => _layout;
    /// <summary>Нижний левый тайл лабиринта на карте.</summary>
    public Vector2Int Origin => _origin;
    /// <summary>Все 7×7 тайлов на карте.</summary>
    public RectInt TileArea => new RectInt(_origin.x, _origin.y, MazeLayout.Size, MazeLayout.Size);
    /// <summary>Внутренняя часть без внешнего кольца — там «густая изгородь» тумана.</summary>
    public RectInt InnerArea => new RectInt(_origin.x + 1, _origin.y + 1, MazeLayout.Size - 2, MazeLayout.Size - 2);
    public Vector2Int CenterTile => _origin + _layout.Center;
    public Vector2Int EntranceTile => _origin + _layout.Entrance;
    /// <summary>Тайл снаружи перед входом.</summary>
    public Vector2Int ApproachTile => _origin + _layout.Approach;
    public override Rect Bounds =>
        new Rect(_origin.x - 0.5f, _origin.y - 0.5f, MazeLayout.Size, MazeLayout.Size);

    /// <summary>
    /// Строит стены и сундук. mapData — перевод тайлов в мир; prefabs — из PuzzleConfig (стена обязательна).
    /// </summary>
    public void Build(MazeLayout layout, Vector2Int origin, IMapDataProvider mapData, PuzzleConfig config)
    {
        _layout = layout;
        _origin = origin;
        _openedChestSprite = config.mazeChestOpenedSprite;

        for (int x = 0; x < MazeLayout.Size; x++)
            for (int y = 0; y < MazeLayout.Size; y++)
            {
                if (!layout.IsWall(x, y)) continue;

                Vector2Int tile = origin + new Vector2Int(x, y);
                GameObject wall = Instantiate(config.mazeWallPrefab, mapData.TileIndexToWorld(tile.x, tile.y),
                    Quaternion.identity, transform);
                wall.name = $"MazeWall_{x}_{y}";
                EnsureCollider(wall, mapData);
                if (!wall.TryGetComponent(out DepthSortedSprite sorting))
                    sorting = wall.AddComponent<DepthSortedSprite>();
                sorting.Refresh();
                _walls.Add(wall);
            }

        if (config.mazeChestPrefab != null)
        {
            Vector2Int center = CenterTile;
            GameObject chest = Instantiate(config.mazeChestPrefab, mapData.TileIndexToWorld(center.x, center.y),
                Quaternion.identity, transform);
            chest.name = "MazeChest";
            MakeWalkable(chest);
            if (!chest.TryGetComponent(out DepthSortedSprite sorting))
                sorting = chest.AddComponent<DepthSortedSprite>();
            sorting.Refresh();
            _chestRenderer = chest.GetComponentInChildren<SpriteRenderer>();
        }
    }

    /// <summary>Стена ли на тайле карты.</summary>
    public bool IsWall(Vector2Int tile)
    {
        Vector2Int local = tile - _origin;
        return _layout != null && _layout.IsWall(local);
    }

    protected override void OnSolved()
    {
        if (_chestRenderer != null && _openedChestSprite != null)
            _chestRenderer.sprite = _openedChestSprite;
    }

    // ─── Helpers ──────────────────────────────────────────────

    // Стена без коллайдера не остановит игрока — добавляем квадрат в один тайл
    private static void EnsureCollider(GameObject wall, IMapDataProvider mapData)
    {
        foreach (Collider2D collider in wall.GetComponentsInChildren<Collider2D>())
            if (!collider.isTrigger) return;

        Debug.LogWarning($"MazePuzzle: на префабе стены {wall.name} нет Collider2D — добавлен BoxCollider2D 1×1", wall);
        Vector3 unit = mapData.MapToWorld(Vector2.one) - mapData.MapToWorld(Vector2.zero);
        var box = wall.AddComponent<BoxCollider2D>();
        Vector3 scale = wall.transform.lossyScale;
        box.size = new Vector2(
            Mathf.Abs(unit.x / (Mathf.Approximately(scale.x, 0f) ? 1f : scale.x)) * 0.95f,
            Mathf.Abs(unit.y / (Mathf.Approximately(scale.y, 0f) ? 1f : scale.y)) * 0.95f);
    }

    // Игрок должен встать на центр — сплошные коллайдеры сундука мешали бы, делаем их триггерами
    private static void MakeWalkable(GameObject chest)
    {
        foreach (Collider2D collider in chest.GetComponentsInChildren<Collider2D>())
        {
            if (collider.isTrigger) continue;
            collider.isTrigger = true;
            Debug.LogWarning("MazePuzzle: коллайдер сундука лабиринта переключён в триггер — игрок встаёт на центр", chest);
        }
    }
}
