using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Какой биом в каком тайле. Карта делится на области Вороного со случайными центрами,
/// каждой области случайно (по весу) достаётся биом; границы искривляются шумом.
/// </summary>
public class BiomeMap
{
    private readonly int[,] _biomeIndex;
    private readonly IReadOnlyList<Biome> _biomes;

    public IReadOnlyList<Biome> Biomes => _biomes;

    private BiomeMap(int[,] biomeIndex, IReadOnlyList<Biome> biomes)
    {
        _biomeIndex = biomeIndex;
        _biomes = biomes;
    }

    public Biome Get(int x, int y)
    {
        if (x < 0 || y < 0 || x >= _biomeIndex.GetLength(0) || y >= _biomeIndex.GetLength(1))
            return null;
        int index = _biomeIndex[x, y];
        return index >= 0 ? _biomes[index] : null;
    }

    // ─── Generation ───────────────────────────────────────────

    public static BiomeMap Generate(BiomeConfig config, int width, int height, int seed)
    {
        int[,] indices = new int[width, height];
        List<Biome> biomes = new List<Biome>();

        if (config != null)
            foreach (Biome biome in config.biomes)
                if (biome != null && biome.weight > 0f)
                    biomes.Add(biome);

        if (biomes.Count == 0)
        {
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    indices[x, y] = -1;
            return new BiomeMap(indices, biomes);
        }

        System.Random random = new System.Random(seed);

        int cellCount = Mathf.Max(biomes.Count,
            Mathf.CeilToInt(width * height / (config.biomeSize * config.biomeSize)));

        Vector2[] centers = new Vector2[cellCount];
        int[] cellBiome = new int[cellCount];

        for (int i = 0; i < cellCount; i++)
        {
            centers[i] = new Vector2(
                (float)random.NextDouble() * width,
                (float)random.NextDouble() * height);
            cellBiome[i] = i < biomes.Count ? i : PickWeighted(biomes, random);
        }

        // Каждый биом встречается хотя бы раз, но на случайном месте
        Shuffle(cellBiome, random);

        float noiseOffset = (float)random.NextDouble() * 10000f;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Vector2 point = new Vector2(x, y) + Warp(x, y, config, noiseOffset);
                indices[x, y] = cellBiome[Nearest(centers, point)];
            }
        }

        return new BiomeMap(indices, biomes);
    }

    private static Vector2 Warp(int x, int y, BiomeConfig config, float offset)
    {
        if (config.borderWarp <= 0f) return Vector2.zero;

        float sx = x / config.borderNoiseScale;
        float sy = y / config.borderNoiseScale;
        float dx = Mathf.PerlinNoise(sx + offset, sy) * 2f - 1f;
        float dy = Mathf.PerlinNoise(sx, sy + offset) * 2f - 1f;
        return new Vector2(dx, dy) * config.borderWarp;
    }

    private static int Nearest(Vector2[] centers, Vector2 point)
    {
        int nearest = 0;
        float minDistance = float.MaxValue;

        for (int i = 0; i < centers.Length; i++)
        {
            float distance = (centers[i] - point).sqrMagnitude;
            if (distance < minDistance)
            {
                minDistance = distance;
                nearest = i;
            }
        }

        return nearest;
    }

    private static int PickWeighted(List<Biome> biomes, System.Random random)
    {
        float total = 0f;
        foreach (Biome biome in biomes) total += biome.weight;

        float roll = (float)random.NextDouble() * total;
        for (int i = 0; i < biomes.Count; i++)
        {
            roll -= biomes[i].weight;
            if (roll <= 0f) return i;
        }

        return biomes.Count - 1;
    }

    private static void Shuffle(int[] array, System.Random random)
    {
        for (int i = array.Length - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (array[i], array[j]) = (array[j], array[i]);
        }
    }
}
