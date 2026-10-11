public interface IMapDataProvider
{
    int MapWidth { get; }
    int MapHeight { get; }

    /// <summary>
    /// Seed последней генерации — от него же зависят биомы и раскладка объектов.
    /// </summary>
    int Seed { get; }

    bool IsWalkable(int x, int y);
    bool IsInBounds(int x, int y);
    TileType GetTileType(int x, int y);

    /// <summary>
    /// Биом тайла или null, если биомы не настроены / тайл вне карты.
    /// </summary>
    Biome GetBiome(int x, int y);

    UnityEngine.Vector3 TileIndexToWorld(int x, int y);
    UnityEngine.Vector2Int WorldToTileIndex(UnityEngine.Vector3 worldPos);

    /// <summary>
    /// Свободная позиция на карте (в тайлах, целые значения — центры тайлов) → мировые координаты.
    /// </summary>
    UnityEngine.Vector3 MapToWorld(UnityEngine.Vector2 mapPos);
}