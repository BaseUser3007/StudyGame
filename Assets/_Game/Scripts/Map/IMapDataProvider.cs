public interface IMapDataProvider
{
    int MapWidth { get; }
    int MapHeight { get; }

    bool IsWalkable(int x, int y);
    bool IsInBounds(int x, int y);
    TileType GetTileType(int x, int y);

    UnityEngine.Vector3 TileIndexToWorld(int x, int y);
    UnityEngine.Vector2Int WorldToTileIndex(UnityEngine.Vector3 worldPos);
}