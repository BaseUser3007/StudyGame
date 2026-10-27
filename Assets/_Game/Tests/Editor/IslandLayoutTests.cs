using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Остров (T24): детерминизм от seed, доля суши = landFraction, кольцо воды у края, одна связная
/// (4-связность) область суши; биомы только на суше, каждый встречается хотя бы раз.
/// </summary>
public class IslandLayoutTests
{
    private const int Size = 100;
    private static readonly int[] Seeds = { 0, 1, 42, 777, 123456, int.MaxValue - 1 };

    private readonly List<Object> _created = new List<Object>();

    [TearDown]
    public void TearDown()
    {
        foreach (Object obj in _created)
            if (obj != null) Object.DestroyImmediate(obj);
        _created.Clear();
    }

    private static bool[,] Island(int seed) => IslandLayout.Generate(Size, Size, seed, IslandSettings.Default);

    // ─── Остров ───────────────────────────────────────────────

    [Test]
    public void Generate_SameSeed_SameIsland()
    {
        foreach (int seed in Seeds)
            CollectionAssert.AreEqual(Island(seed), Island(seed), $"seed {seed}");
    }

    [Test]
    public void Generate_DifferentSeeds_DifferentIslands()
    {
        CollectionAssert.AreNotEqual(Island(1), Island(2));
    }

    [Test]
    public void Generate_LandFraction_MatchesSettings([Values(0.3f, 0.5f, 0.7f)] float fraction)
    {
        IslandSettings settings = IslandSettings.Default;
        settings.LandFraction = fraction;

        foreach (int seed in Seeds)
        {
            bool[,] land = IslandLayout.Generate(Size, Size, seed, settings);
            float actual = IslandLayout.CountLand(land) / (float)(Size * Size);
            Assert.AreEqual(fraction, actual, 0.01f, $"seed {seed}");
        }
    }

    [Test]
    public void Generate_BorderRing_IsWater()
    {
        int border = IslandSettings.Default.BorderWater;
        Assert.Greater(border, 0);

        foreach (int seed in Seeds)
        {
            bool[,] land = Island(seed);
            for (int x = 0; x < Size; x++)
                for (int y = 0; y < Size; y++)
                {
                    bool inRing = x < border || y < border || x >= Size - border || y >= Size - border;
                    if (inRing)
                        Assert.IsFalse(land[x, y], $"суша в кольце у края: ({x}, {y}), seed {seed}");
                }
        }
    }

    [Test]
    public void Generate_Land_IsSingleConnectedRegion()
    {
        foreach (int seed in Seeds)
        {
            bool[,] land = Island(seed);
            int total = IslandLayout.CountLand(land);
            Assert.Greater(total, 0);
            Assert.AreEqual(total, CountConnected(land), $"есть недостижимые островки, seed {seed}");
        }
    }

    [Test]
    public void Generate_TooSmallMap_AllWater()
    {
        IslandSettings settings = IslandSettings.Default;
        settings.BorderWater = 5;
        Assert.AreEqual(0, IslandLayout.CountLand(IslandLayout.Generate(8, 8, 1, settings)));
    }

    // ─── Биомы ────────────────────────────────────────────────

    [Test]
    public void BiomeMap_WaterHasNoBiome_EveryBiomeOnLand()
    {
        BiomeConfig config = CreateBiomes(4);

        foreach (int seed in Seeds)
        {
            bool[,] land = Island(seed);
            BiomeMap map = BiomeMap.Generate(config, land, seed);
            var seen = new HashSet<Biome>();

            for (int x = 0; x < Size; x++)
                for (int y = 0; y < Size; y++)
                {
                    Biome biome = map.Get(x, y);
                    if (!land[x, y])
                        Assert.IsNull(biome, $"биом на воде: ({x}, {y}), seed {seed}");
                    else
                    {
                        Assert.IsNotNull(biome, $"суша без биома: ({x}, {y}), seed {seed}");
                        seen.Add(biome);
                    }
                }

            Assert.AreEqual(config.biomes.Count, seen.Count, $"не все биомы на суше, seed {seed}");
        }
    }

    [Test]
    public void BiomeMap_ManyBiomesSmallIsland_EveryBiomePresent()
    {
        // Биомов много, суши мало — искривление границ может «съесть» маленькую область целиком
        BiomeConfig config = CreateBiomes(8);
        config.borderWarp = 12f;
        IslandSettings settings = IslandSettings.Default;
        settings.LandFraction = 0.15f;

        foreach (int seed in Seeds)
        {
            bool[,] land = IslandLayout.Generate(60, 60, seed, settings);
            BiomeMap map = BiomeMap.Generate(config, land, seed);
            var seen = new HashSet<Biome>();
            for (int x = 0; x < 60; x++)
                for (int y = 0; y < 60; y++)
                    if (map.Get(x, y) != null) seen.Add(map.Get(x, y));

            Assert.AreEqual(config.biomes.Count, seen.Count, $"seed {seed}");
        }
    }

    [Test]
    public void BiomeMap_SameSeed_SameLayout()
    {
        BiomeConfig config = CreateBiomes(3);
        bool[,] land = Island(42);
        BiomeMap a = BiomeMap.Generate(config, land, 42);
        BiomeMap b = BiomeMap.Generate(config, land, 42);

        for (int x = 0; x < Size; x++)
            for (int y = 0; y < Size; y++)
                Assert.AreSame(a.Get(x, y), b.Get(x, y), $"({x}, {y})");
    }

    // ─── Helpers ──────────────────────────────────────────────

    private BiomeConfig CreateBiomes(int count)
    {
        BiomeConfig config = ScriptableObject.CreateInstance<BiomeConfig>();
        _created.Add(config);
        for (int i = 0; i < count; i++)
            config.biomes.Add(new Biome { name = "Biome" + i, weight = 1f + i });
        return config;
    }

    // Размер связной области суши, в которой лежит первый найденный тайл суши
    private static int CountConnected(bool[,] land)
    {
        int width = land.GetLength(0);
        int height = land.GetLength(1);
        var visited = new bool[width, height];
        var queue = new Queue<Vector2Int>();

        for (int x = 0; x < width && queue.Count == 0; x++)
            for (int y = 0; y < height && queue.Count == 0; y++)
                if (land[x, y])
                {
                    queue.Enqueue(new Vector2Int(x, y));
                    visited[x, y] = true;
                }

        int count = 0;
        Vector2Int[] offsets = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
        while (queue.Count > 0)
        {
            Vector2Int tile = queue.Dequeue();
            count++;
            foreach (Vector2Int offset in offsets)
            {
                Vector2Int next = tile + offset;
                if (next.x < 0 || next.y < 0 || next.x >= width || next.y >= height) continue;
                if (!land[next.x, next.y] || visited[next.x, next.y]) continue;
                visited[next.x, next.y] = true;
                queue.Enqueue(next);
            }
        }

        return count;
    }
}
