using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Место забега по умолчанию (RandomPortalLocationGenerator): детерминизм, суша под порталом,
/// спавн вплотную, большая область суши, запасной вариант.
/// </summary>
public class RunLocationTests
{
    private const int MinRegion = 50;

    private static RandomPortalLocationGenerator Generator(Vector2Int fallback, int margin = 1, Vector2Int? size = null)
        => new RandomPortalLocationGenerator(size ?? Vector2Int.one, margin, MinRegion, fallback);

    // ─── Детерминизм ──────────────────────────────────────────

    [Test]
    public void Generate_SameSeed_SameLocation()
    {
        GridMap map = GridMap.Land(40, 40);
        RunLocation a = Generator(new Vector2Int(20, 20)).Generate(map, 123);
        RunLocation b = Generator(new Vector2Int(20, 20)).Generate(map, 123);

        Assert.AreEqual(a.PortalArea, b.PortalArea);
        Assert.AreEqual(a.SpawnTile, b.SpawnTile);
        Assert.AreEqual(123, a.Seed);
    }

    [Test]
    public void Generate_DifferentSeeds_PortalMoves()
    {
        GridMap map = GridMap.Land(40, 40);
        var portals = new HashSet<Vector2Int>();
        for (int seed = 0; seed < 8; seed++)
            portals.Add(Generator(new Vector2Int(20, 20)).Generate(map, seed).PortalTile);

        Assert.Greater(portals.Count, 1, "портал не должен стоять в одном месте при любом seed");
    }

    // ─── Суша и спавн ─────────────────────────────────────────

    [Test]
    public void Generate_PortalWithMarginOnLand_SpawnWalkableAndAdjacent([Values(1, 7, 42, 999)] int seed)
    {
        GridMap map = GridMap.Land(30, 30);
        // Озеро и полосы воды — чтобы правило поля было не тривиальным
        map.FillWater(new RectInt(10, 10, 8, 8));
        map.FillWater(new RectInt(0, 25, 30, 1));
        var size = new Vector2Int(2, 2);

        RunLocation location = Generator(new Vector2Int(5, 5), margin: 1, size: size).Generate(map, seed);

        Assert.IsFalse(location.UsedFallback);
        Assert.AreEqual(size, location.PortalArea.size);
        RectInt withMargin = new RectInt(location.PortalArea.xMin - 1, location.PortalArea.yMin - 1,
            location.PortalArea.width + 2, location.PortalArea.height + 2);
        for (int x = withMargin.xMin; x < withMargin.xMax; x++)
            for (int y = withMargin.yMin; y < withMargin.yMax; y++)
                Assert.IsTrue(map.IsWalkable(x, y), $"под порталом с полем вода/край: ({x}, {y})");

        AssertSpawnAdjacent(map, location);
        Assert.AreEqual(location.PortalArea.yMin - 1, location.SpawnTile.y, "спавн — в ряду под порталом");
        Assert.IsFalse(location.SpawnInsidePortal);
    }

    [Test]
    public void Generate_PortalNotOnMapEdge()
    {
        GridMap map = GridMap.Land(6, 6);
        for (int seed = 0; seed < 20; seed++)
        {
            RunLocation location = Generator(new Vector2Int(3, 3)).Generate(map, seed);
            RectInt area = location.PortalArea;
            Assert.GreaterOrEqual(area.xMin, 1);
            Assert.GreaterOrEqual(area.yMin, 1);
            Assert.LessOrEqual(area.xMax, 5);
            Assert.LessOrEqual(area.yMax, 5);
        }
    }

    // ─── Области суши ─────────────────────────────────────────

    [Test]
    public void Generate_SkipsSmallIslands()
    {
        // Большой остров 20×20 и маленький 5×5 (25 < MinRegion), между ними вода
        GridMap map = GridMap.Water(40, 30);
        var big = new RectInt(2, 2, 20, 20);
        var small = new RectInt(30, 10, 5, 5);
        map.FillLand(big);
        map.FillLand(small);

        for (int seed = 0; seed < 30; seed++)
        {
            RunLocation location = Generator(new Vector2Int(32, 12)).Generate(map, seed);
            Assert.IsTrue(big.Contains(location.SpawnTile), $"seed {seed}: спавн на маленьком острове");
            Assert.IsTrue(big.Contains(location.PortalTile), $"seed {seed}: портал на маленьком острове");
        }
    }

    [Test]
    public void Generate_OnlySmallIslands_UsesLargest()
    {
        GridMap map = GridMap.Water(30, 30);
        var larger = new RectInt(2, 2, 6, 6);   // 36
        var smaller = new RectInt(15, 15, 5, 5); // 25
        map.FillLand(larger);
        map.FillLand(smaller);

        RunLocation location = Generator(new Vector2Int(17, 17)).Generate(map, 5);

        Assert.IsFalse(location.UsedFallback);
        Assert.IsTrue(larger.Contains(location.SpawnTile));
        Assert.IsTrue(larger.Contains(location.PortalTile));
    }

    // ─── Запасной вариант ─────────────────────────────────────

    [Test]
    public void Generate_NoRoomForMargin_FallbackSpawnWithPortalAbove()
    {
        // Полоска суши в 1 тайл шириной: поле вокруг портала не помещается нигде
        GridMap map = GridMap.Water(20, 20);
        map.FillLand(new RectInt(10, 3, 1, 10));
        var fallback = new Vector2Int(10, 6);

        RunLocation location = Generator(fallback).Generate(map, 3);

        Assert.IsTrue(location.UsedFallback);
        Assert.AreEqual(fallback, location.SpawnTile);
        Assert.AreEqual(new Vector2Int(10, 7), location.PortalTile, "портал — над спавном");
        Assert.IsFalse(location.SpawnInsidePortal);
    }

    [Test]
    public void Generate_NoLandAtAll_PortalOnSpawnTile()
    {
        GridMap map = GridMap.Water(10, 10);
        var fallback = new Vector2Int(5, 5);

        RunLocation location = Generator(fallback).Generate(map, 1);

        Assert.IsTrue(location.UsedFallback);
        Assert.AreEqual(fallback, location.SpawnTile);
        Assert.IsTrue(location.SpawnInsidePortal);
    }

    // ─── Геометрия RunLocation ────────────────────────────────

    [Test]
    public void RunLocation_PositionAndBounds()
    {
        var location = new RunLocation(0, new Vector2Int(4, 2), new RectInt(3, 3, 2, 2));

        Assert.AreEqual(new Vector2(3.5f, 3.5f), location.PortalPosition);
        Assert.AreEqual(new Rect(2.5f, 2.5f, 2f, 2f), location.PortalBounds);
        Assert.IsFalse(location.SpawnInsidePortal);
    }

    // ─── Helpers ──────────────────────────────────────────────

    private static void AssertSpawnAdjacent(IMapDataProvider map, RunLocation location)
    {
        Vector2Int spawn = location.SpawnTile;
        RectInt area = location.PortalArea;
        Assert.IsTrue(map.IsWalkable(spawn.x, spawn.y), "спавн непроходим");
        Assert.IsFalse(area.Contains(spawn), "спавн внутри портала");
        var ring = new RectInt(area.xMin - 1, area.yMin - 1, area.width + 2, area.height + 2);
        Assert.IsTrue(ring.Contains(spawn), "спавн не касается портала");
    }

    private sealed class GridMap : IMapDataProvider
    {
        private readonly bool[,] _water;

        private GridMap(int width, int height, bool water)
        {
            MapWidth = width;
            MapHeight = height;
            _water = new bool[width, height];
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    _water[x, y] = water;
        }

        public static GridMap Land(int width, int height) => new GridMap(width, height, false);
        public static GridMap Water(int width, int height) => new GridMap(width, height, true);

        public void FillWater(RectInt area) => Fill(area, true);
        public void FillLand(RectInt area) => Fill(area, false);

        private void Fill(RectInt area, bool water)
        {
            for (int x = area.xMin; x < area.xMax; x++)
                for (int y = area.yMin; y < area.yMax; y++)
                    if (IsInBounds(x, y)) _water[x, y] = water;
        }

        public int MapWidth { get; }
        public int MapHeight { get; }
        public int Seed => 1;
        public bool IsWalkable(int x, int y) => IsInBounds(x, y) && !_water[x, y];
        public bool IsInBounds(int x, int y) => x >= 0 && y >= 0 && x < MapWidth && y < MapHeight;
        public TileType GetTileType(int x, int y) => IsWalkable(x, y) ? TileType.Grass : TileType.Water;
        public Biome GetBiome(int x, int y) => null;
        public Vector3 TileIndexToWorld(int x, int y) => new Vector3(x, y, 0f);
        public Vector2Int WorldToTileIndex(Vector3 worldPos) => MapPosition.ToTile(worldPos);
        public Vector3 MapToWorld(Vector2 mapPos) => new Vector3(mapPos.x, mapPos.y, 0f);
    }
}
