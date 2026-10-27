using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Какой биом в каком тайле. Суша делится на области Вороного со случайными центрами на суше,
/// каждой области случайно (по весу) достаётся биом; границы искривляются шумом. Вода — без биома.
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

    /// <summary>
    /// land[x, y] = true — суша. Центры областей берутся только на суше (разные тайлы), вода — биом −1
    /// (Get → null). Каждый биом встречается на суше хотя бы раз, если тайлов суши не меньше, чем биомов.
    /// </summary>
    public static BiomeMap Generate(BiomeConfig config, bool[,] land, int seed)
    {
        int width = land.GetLength(0);
        int height = land.GetLength(1);
        int[,] indices = new int[width, height];
        List<Biome> biomes = new List<Biome>();

        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                indices[x, y] = -1;

        if (config != null)
            foreach (Biome biome in config.biomes)
                if (biome != null && biome.weight > 0f)
                    biomes.Add(biome);

        List<Vector2Int> landTiles = new List<Vector2Int>();
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                if (land[x, y]) landTiles.Add(new Vector2Int(x, y));

        if (biomes.Count == 0 || landTiles.Count == 0)
            return new BiomeMap(indices, biomes);

        System.Random random = new System.Random(seed);

        int cellCount = Mathf.Max(biomes.Count,
            Mathf.CeilToInt(landTiles.Count / (config.biomeSize * config.biomeSize)));
        cellCount = Mathf.Min(cellCount, landTiles.Count);

        // Центры — разные тайлы суши (частичное перемешивание Фишера–Йетса)
        Vector2[] centers = new Vector2[cellCount];
        int[] cellBiome = new int[cellCount];
        for (int i = 0; i < cellCount; i++)
        {
            int j = i + random.Next(landTiles.Count - i);
            (landTiles[i], landTiles[j]) = (landTiles[j], landTiles[i]);
            centers[i] = landTiles[i];
            cellBiome[i] = i < biomes.Count ? i : PickWeighted(biomes, random);
        }

        // Каждый биом встречается хотя бы раз, но на случайном месте
        Shuffle(cellBiome, random);

        float noiseOffset = (float)random.NextDouble() * 10000f;
        int[] biomeTiles = new int[biomes.Count];

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (!land[x, y]) continue;
                Vector2 point = new Vector2(x, y) + Warp(x, y, config, noiseOffset);
                int biome = cellBiome[Nearest(centers, point)];
                indices[x, y] = biome;
                biomeTiles[biome]++;
            }
        }

        EnsureEveryBiome(indices, land, centers, cellBiome, biomeTiles);
        return new BiomeMap(indices, biomes);
    }

    // Искривление границ может «съесть» маленькую область целиком. Тогда биом получает её область
    // без искривления: её центр — тайл суши, ближе всего к самому себе, так что хотя бы он достанется биому
    private static void EnsureEveryBiome(int[,] indices, bool[,] land, Vector2[] centers, int[] cellBiome,
        int[] biomeTiles)
    {
        for (int biome = 0; biome < biomeTiles.Length; biome++)
        {
            if (biomeTiles[biome] > 0) continue;

            int cell = System.Array.IndexOf(cellBiome, biome);
            if (cell < 0) continue;

            for (int x = 0; x < indices.GetLength(0); x++)
            {
                for (int y = 0; y < indices.GetLength(1); y++)
                {
                    if (!land[x, y] || Nearest(centers, new Vector2(x, y)) != cell) continue;
                    // Последний тайл другого биома не забираем — иначе пропадёт уже он
                    if (biomeTiles[indices[x, y]] <= 1) continue;
                    biomeTiles[indices[x, y]]--;
                    indices[x, y] = biome;
                    biomeTiles[biome]++;
                }
            }
        }
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
