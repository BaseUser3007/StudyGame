using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// ExplorePlanner — выбор цели autoExplore на синтетической карте: направление к неоткрытой суше,
/// вода на пути, граница тумана, «всё открыто», детерминизм.
/// </summary>
public class AutoExploreTests
{
    private const int Vision = 2;

    [Test]
    public void ChoosesDirectionWithMostUnexploredLand()
    {
        var map = new GridMap(40, 40);
        var start = new Vector2Int(20, 20);
        // Не открыта только полоса на востоке: x ≥ 25, |y − 20| ≤ 2
        var fog = new GridFog(map, (x, y) => !(x >= start.x + 5 && Math.Abs(y - start.y) <= 2));

        Assert.IsTrue(ExplorePlanner.TryPlan(map, fog, start, Vision, out ExplorePlanner.Plan plan));
        Assert.AreEqual(start + new Vector2Int(6, 0), plan.Target);
        Assert.IsFalse(plan.ToFrontier);
        Assert.AreEqual(20, plan.ExpectedNewTiles);
    }

    [Test]
    public void WaterOnTheRayIsTakenIntoAccount()
    {
        var map = new GridMap(40, 40);
        var start = new Vector2Int(20, 20);
        // Вода в двух тайлах к востоку перекрывает путь: MoveAsync остановится на x = 21
        for (int y = 0; y < 40; y++) map.SetWater(start.x + 2, y);
        // Неоткрытые полосы и на востоке (за водой), и на западе
        var fog = new GridFog(map, (x, y) => Math.Abs(y - start.y) > 2 || Math.Abs(x - start.x) < 5);

        Assert.IsTrue(ExplorePlanner.TryPlan(map, fog, start, Vision, out ExplorePlanner.Plan plan));
        Assert.AreEqual(start + new Vector2Int(-6, 0), plan.Target);
    }

    [Test]
    public void AllExploredAround_GoesTowardFogFrontier()
    {
        var map = new GridMap(60, 60);
        var start = new Vector2Int(30, 30);
        // Открыт квадрат радиуса 10 — шаг в 6 тайлов с обзором 2 ничего нового не откроет
        var fog = new GridFog(map, (x, y) => Chebyshev(start, new Vector2Int(x, y)) <= 10);

        Assert.IsTrue(ExplorePlanner.TryPlan(map, fog, start, Vision, out ExplorePlanner.Plan plan));
        Assert.IsTrue(plan.ToFrontier);
        Assert.AreEqual(ExplorePlanner.StepTiles, Chebyshev(start, plan.Target));
    }

    [Test]
    public void Frontier_TargetIsReachableInStraightLine()
    {
        var map = new GridMap(60, 60);
        var start = new Vector2Int(30, 30);
        // Стена воды к востоку с проходом сверху; не открыто только то, что за ней
        for (int y = 22; y <= 38; y++) map.SetWater(start.x + 3, y);
        var fog = new GridFog(map, (x, y) => x <= start.x + 10);

        Assert.IsTrue(ExplorePlanner.TryPlan(map, fog, start, Vision, out ExplorePlanner.Plan plan));
        Assert.IsTrue(plan.ToFrontier);
        Assert.AreNotEqual(start, plan.Target);

        List<Vector2Int> walk = ExplorePlanner.WalkStraight(map, start, plan.Target);
        Assert.IsNotEmpty(walk);
        Assert.AreEqual(plan.Target, walk[walk.Count - 1], "MoveToAsync должен дойти до цели по прямой");
    }

    [Test]
    public void EverythingExplored_NoPlan()
    {
        var map = new GridMap(30, 30);
        var fog = new GridFog(map, (x, y) => true);

        Assert.IsFalse(ExplorePlanner.TryPlan(map, fog, new Vector2Int(15, 15), Vision, out _));
    }

    [Test]
    public void SurroundedByWater_NoPlan()
    {
        var map = new GridMap(30, 30);
        var start = new Vector2Int(15, 15);
        for (int x = 0; x < 30; x++)
            for (int y = 0; y < 30; y++)
                if (new Vector2Int(x, y) != start) map.SetWater(x, y);
        var fog = new GridFog(map, (x, y) => false);

        Assert.IsFalse(ExplorePlanner.TryPlan(map, fog, start, Vision, out _));
    }

    [Test]
    public void TieIsResolvedDeterministically()
    {
        var start = new Vector2Int(20, 20);
        // Ничего не открыто: все четыре диагонали открывают поровну — решает хеш, а не Random
        var mapA = new GridMap(40, 40, seed: 7);
        var fogA = new GridFog(mapA, (x, y) => false);

        Assert.IsTrue(ExplorePlanner.TryPlan(mapA, fogA, start, Vision, out ExplorePlanner.Plan first));
        for (int i = 0; i < 5; i++)
        {
            UnityEngine.Random.InitState(i);
            Assert.IsTrue(ExplorePlanner.TryPlan(mapA, fogA, start, Vision, out ExplorePlanner.Plan again));
            Assert.AreEqual(first.Target, again.Target);
        }

        Vector2Int offset = first.Target - start;
        Assert.AreEqual(6, Math.Abs(offset.x));
        Assert.AreEqual(6, Math.Abs(offset.y), "при открытой со всех сторон суше диагональ открывает больше всего");

        // Разные seed/позиции дают разные (но тоже детерминированные) выборы
        var seen = new HashSet<Vector2Int>();
        for (int seed = 0; seed < 32; seed++)
        {
            var map = new GridMap(40, 40, seed);
            Assert.IsTrue(ExplorePlanner.TryPlan(map, new GridFog(map, (x, y) => false), start, Vision, out ExplorePlanner.Plan plan));
            seen.Add(plan.Target - start);
        }
        Assert.Greater(seen.Count, 1, "жребий должен зависеть от seed");
    }

    [Test]
    public void CountRevealed_CountsOnlyUnexploredLand()
    {
        var map = new GridMap(10, 10);
        map.SetWater(5, 5);
        var fog = new GridFog(map, (x, y) => x < 4);
        var path = new List<Vector2Int> { new Vector2Int(5, 4) };

        // Квадрат 3×3 вокруг (5, 4): x 4..6, y 3..5, минус вода (5, 5)
        Assert.AreEqual(8, ExplorePlanner.CountRevealed(map, fog, path, 1));
    }

    // ─── Helpers ──────────────────────────────────────────────

    private static int Chebyshev(Vector2Int a, Vector2Int b)
        => Math.Max(Math.Abs(a.x - b.x), Math.Abs(a.y - b.y));

    /// <summary>Сетка суши с отдельными тайлами воды; тайл (x, y) — мировая точка (x, y).</summary>
    private sealed class GridMap : IMapDataProvider
    {
        private readonly bool[,] _water;

        public GridMap(int width, int height, int seed = 1)
        {
            MapWidth = width;
            MapHeight = height;
            Seed = seed;
            _water = new bool[width, height];
        }

        public void SetWater(int x, int y)
        {
            if (IsInBounds(x, y)) _water[x, y] = true;
        }

        public int MapWidth { get; }
        public int MapHeight { get; }
        public int Seed { get; }
        public bool IsWalkable(int x, int y) => IsInBounds(x, y) && !_water[x, y];
        public bool IsInBounds(int x, int y) => x >= 0 && y >= 0 && x < MapWidth && y < MapHeight;
        public TileType GetTileType(int x, int y) => IsWalkable(x, y) ? TileType.Grass : TileType.Water;
        public Biome GetBiome(int x, int y) => null;
        public Vector3 TileIndexToWorld(int x, int y) => new Vector3(x, y, 0f);
        public Vector2Int WorldToTileIndex(Vector3 worldPos) => MapPosition.ToTile(worldPos);
        public Vector3 MapToWorld(Vector2 mapPos) => new Vector3(mapPos.x, mapPos.y, 0f);
    }

    /// <summary>Туман по правилу explored(x, y); видимым считается то же, что открыто.</summary>
    private sealed class GridFog : IFogProvider
    {
        private readonly IMapDataProvider _map;
        private readonly Func<int, int, bool> _explored;

        public GridFog(IMapDataProvider map, Func<int, int, bool> explored)
        {
            _map = map;
            _explored = explored;
        }

        public bool IsVisible(int x, int y) => IsExplored(x, y);
        public bool IsExplored(int x, int y) => _map.IsInBounds(x, y) && _explored(x, y);
    }
}
