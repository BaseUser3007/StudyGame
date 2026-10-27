using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public enum TileType { Grass, Water }

public class TilemapGenerator : MonoBehaviour, IMapDataProvider
{
    [SerializeField] private Tilemap waterTilemap;
    [SerializeField] private Tilemap groundTilemap;
    [SerializeField] private Tilemap fogTilemap;
    [SerializeField] private MapGenerationConfig config;

    private TileType[,] _tileMap;
    private BiomeMap _biomeMap;
    private int _seed;

    // Для систем, которым нужны параметры генерации помимо IMapDataProvider (стартовый набор у спавна)
    public MapGenerationConfig Config => config;
    public int MapWidth => config.mapWidth;
    public int MapHeight => config.mapHeight;
    public int Seed => _seed;
    public BiomeMap BiomeMap => _biomeMap;

    public void GenerateMap()
    {
        ValidateConfig();

        _seed = config.randomSeed
            ? Random.Range(0, int.MaxValue)
            : config.seed;

        // Биомам нужна маска суши, тайлам земли — биомы
        GenerateWater();
        bool[,] land = IslandLayout.Generate(config.mapWidth, config.mapHeight, _seed, config.GetIslandSettings());
        // Свой сдвиг seed: иначе поток случайных чисел биомов совпадёт с потоком острова
        GenerateBiomes(land, unchecked(_seed * 17 + 101));
        GenerateGround(land);
        GenerateFog();
    }

    // ─── Validation ───────────────────────────────────────────

    private void ValidateConfig()
    {
        Debug.Assert(config != null,
            "TilemapGenerator: MapGenerationConfig is not assigned");
        Debug.Assert(config.noiseScale > 0f,
            "TilemapGenerator: noiseScale must be greater than 0");
        Debug.Assert(config.landFraction > 0f && config.landFraction < 1f,
            "TilemapGenerator: landFraction must be between 0 and 1");
        Debug.Assert(config.waterTile != null,
            "TilemapGenerator: waterTile is not assigned in config");
        Debug.Assert(config.grassTile != null,
            "TilemapGenerator: grassTile is not assigned in config");
        Debug.Assert(config.fogTile != null,
            "TilemapGenerator: fogTile is not assigned in config");
        Debug.Assert(config.biomeConfig != null,
            "TilemapGenerator: biomeConfig is not assigned in config");
    }

    // ─── Private Generation ───────────────────────────────────

    private void GenerateWater()
    {
        waterTilemap.ClearAllTiles();

        FillTilemap(
            waterTilemap,
            -config.mapPadding,
            -config.mapPadding,
            config.mapWidth + config.mapPadding,
            config.mapHeight + config.mapPadding,
            config.waterTile);
    }

    private void GenerateGround(bool[,] land)
    {
        _tileMap = new TileType[config.mapWidth, config.mapHeight];
        groundTilemap.ClearAllTiles();

        // Тайлы ставятся одним пакетом: правила LandRuleTile пересчитываются уже по готовым соседям
        List<Vector3Int> positions = new List<Vector3Int>();
        List<TileBase> tiles = new List<TileBase>();

        for (int x = 0; x < config.mapWidth; x++)
        {
            for (int y = 0; y < config.mapHeight; y++)
            {
                TileType tileType = land[x, y] ? TileType.Grass : TileType.Water;
                _tileMap[x, y] = tileType;
                if (tileType == TileType.Water) continue;

                positions.Add(new Vector3Int(x, y, 0));
                tiles.Add(GetGroundTile(x, y));
            }
        }

        groundTilemap.SetTiles(positions.ToArray(), tiles.ToArray());
    }

    // Свой тайл у биома, иначе общий тайл травы
    private TileBase GetGroundTile(int x, int y)
    {
        Biome biome = _biomeMap?.Get(x, y);
        return biome != null && biome.groundTile != null ? biome.groundTile : config.grassTile;
    }

    private void GenerateBiomes(bool[,] land, int seed)
    {
        _biomeMap = BiomeMap.Generate(config.biomeConfig, land, seed);
    }

    private void GenerateFog()
    {
        fogTilemap.ClearAllTiles();
        fogTilemap.color = Color.black;

        FillTilemap(
            fogTilemap,
            -config.mapPadding,
            -config.mapPadding,
            config.mapWidth + config.mapPadding,
            config.mapHeight + config.mapPadding,
            config.fogTile);
    }

    private void FillTilemap(Tilemap tilemap, int startX, int startY, int endX, int endY, TileBase tile)
    {
        for (int x = startX; x < endX; x++)
            for (int y = startY; y < endY; y++)
                tilemap.SetTile(new Vector3Int(x, y, 0), tile);
    }

    // ─── IMapDataProvider ─────────────────────────────────────

    public TileType GetTileType(int x, int y)
    {
        if (!IsInBounds(x, y)) return TileType.Water;
        return _tileMap[x, y];
    }

    public Biome GetBiome(int x, int y)
    {
        return _biomeMap?.Get(x, y);
    }

    public bool IsWalkable(int x, int y)
    {
        if (!IsInBounds(x, y)) return false;
        return _tileMap[x, y] != TileType.Water;
    }

    public bool IsInBounds(int x, int y)
    {
        return x >= 0 && x < config.mapWidth
            && y >= 0 && y < config.mapHeight;
    }

    public Vector3 TileIndexToWorld(int x, int y)
    {
        return groundTilemap.CellToWorld(new Vector3Int(x, y, 0))
             + groundTilemap.cellSize * 0.5f;
    }

    public Vector2Int WorldToTileIndex(Vector3 worldPos)
    {
        Vector3Int cell = groundTilemap.WorldToCell(worldPos);
        return new Vector2Int(cell.x, cell.y);
    }

    public Vector3 MapToWorld(Vector2 mapPos)
    {
        return TileIndexToWorld(0, 0)
             + Vector3.Scale(new Vector3(mapPos.x, mapPos.y, 0f), groundTilemap.cellSize);
    }
}