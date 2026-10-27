using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Параметры формы острова. Отдельная структура, чтобы EditMode-тесты обходились без ScriptableObject.
/// </summary>
[Serializable]
public struct IslandSettings
{
    // Доля суши от всей карты (0..1)
    public float LandFraction;
    // Масштаб основного шума высоты (тайлы на период первой октавы)
    public float NoiseScale;
    // Сколько октав fBm
    public int Octaves;
    // Сила радиального спада высоты к краю карты
    public float Falloff;
    // Степень спада: больше — плоский центр и крутой берег
    public float FalloffPower;
    // Масштаб низкочастотного шума, искажающего форму острова
    public float CoastNoiseScale;
    // На сколько тайлов шум сдвигает точку при расчёте спада
    public float CoastWarp;
    // Кольцо тайлов у края карты, где всегда вода
    public int BorderWater;

    public static IslandSettings Default => new IslandSettings
    {
        LandFraction = 0.5f,
        NoiseScale = 20f,
        Octaves = 4,
        Falloff = 0.9f,
        FalloffPower = 2.2f,
        CoastNoiseScale = 25f,
        CoastWarp = 10f,
        BorderWater = 3
    };
}

/// <summary>
/// Раскладка суши острова: fBm-высота минус радиальный спад (искажённый шумом) → одна связная (4-связность)
/// область суши ровно из LandFraction тайлов карты, по краю — кольцо воды. Озёра внутри остаются.
/// Чистая логика, детерминирована от seed: смещения шума берутся из System.Random(seed).
/// </summary>
public static class IslandLayout
{
    // Смещения шума — в [0, NoiseOffsetRange): точность float там ещё достаточная
    private const double NoiseOffsetRange = 10000.0;

    // ─── Public API ───────────────────────────────────────────

    /// <summary>
    /// true = суша. Порог воды — перцентиль: суша растёт от самой высокой точки, каждый раз забирая
    /// самый высокий тайл на границе, пока не наберётся нужная доля. Так область всегда одна и связная,
    /// мелких недостижимых островков нет, а низины, окружённые сушей, остаются озёрами.
    /// </summary>
    public static bool[,] Generate(int width, int height, int seed, IslandSettings settings)
    {
        bool[,] land = new bool[Math.Max(0, width), Math.Max(0, height)];
        int border = Math.Max(0, settings.BorderWater);
        if (width - 2 * border <= 0 || height - 2 * border <= 0)
            return land;

        float[,] heights = ComputeHeights(width, height, seed, settings);

        int interior = (width - 2 * border) * (height - 2 * border);
        int target = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(settings.LandFraction) * width * height),
            0, interior);
        if (target == 0) return land;

        GrowFromPeak(heights, land, border, target);
        return land;
    }

    public static int CountLand(bool[,] land)
    {
        int count = 0;
        foreach (bool isLand in land)
            if (isLand) count++;
        return count;
    }

    // ─── Height ───────────────────────────────────────────────

    private static float[,] ComputeHeights(int width, int height, int seed, IslandSettings settings)
    {
        System.Random random = new System.Random(seed);

        int octaves = Math.Max(1, settings.Octaves);
        Vector2[] octaveOffsets = new Vector2[octaves];
        for (int i = 0; i < octaves; i++)
            octaveOffsets[i] = NextOffset(random);
        Vector2 coastOffsetX = NextOffset(random);
        Vector2 coastOffsetY = NextOffset(random);

        float noiseScale = Mathf.Max(0.01f, settings.NoiseScale);
        float coastScale = Mathf.Max(0.01f, settings.CoastNoiseScale);
        float halfW = width * 0.5f;
        float halfH = height * 0.5f;

        float[,] heights = new float[width, height];
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                float noise = Fbm(x, y, noiseScale, octaveOffsets);

                // Спад считается от искажённой точки — берег получается неровным, а не кругом
                float wx = (Mathf.PerlinNoise(x / coastScale + coastOffsetX.x, y / coastScale + coastOffsetX.y) * 2f - 1f)
                         * settings.CoastWarp;
                float wy = (Mathf.PerlinNoise(x / coastScale + coastOffsetY.x, y / coastScale + coastOffsetY.y) * 2f - 1f)
                         * settings.CoastWarp;
                float dx = (x + 0.5f + wx - halfW) / halfW;
                float dy = (y + 0.5f + wy - halfH) / halfH;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);

                heights[x, y] = noise - settings.Falloff * Mathf.Pow(distance, Mathf.Max(0.01f, settings.FalloffPower));
            }
        }

        return heights;
    }

    private static Vector2 NextOffset(System.Random random)
    {
        return new Vector2(
            (float)(random.NextDouble() * NoiseOffsetRange),
            (float)(random.NextDouble() * NoiseOffsetRange));
    }

    // Сумма октав, нормированная в 0..1
    private static float Fbm(int x, int y, float scale, Vector2[] offsets)
    {
        float sum = 0f;
        float amplitude = 1f;
        float totalAmplitude = 0f;
        float frequency = 1f / scale;

        for (int i = 0; i < offsets.Length; i++)
        {
            sum += amplitude * Mathf.PerlinNoise(x * frequency + offsets[i].x, y * frequency + offsets[i].y);
            totalAmplitude += amplitude;
            amplitude *= 0.5f;
            frequency *= 2f;
        }

        return sum / totalAmplitude;
    }

    // ─── Land growth ──────────────────────────────────────────

    private static readonly Vector2Int[] Neighbours =
    {
        Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down
    };

    // Рост суши от самого высокого тайла внутри кольца: всегда берётся самый высокий тайл границы
    private static void GrowFromPeak(float[,] heights, bool[,] land, int border, int target)
    {
        int width = heights.GetLength(0);
        int height = heights.GetLength(1);

        Vector2Int peak = new Vector2Int(border, border);
        for (int x = border; x < width - border; x++)
            for (int y = border; y < height - border; y++)
                if (IsHigher(heights, x, y, peak.x, peak.y, width))
                    peak = new Vector2Int(x, y);

        bool[,] queued = new bool[width, height];
        TileHeap frontier = new TileHeap(heights, width);
        frontier.Push(peak);
        queued[peak.x, peak.y] = true;

        int count = 0;
        while (count < target && frontier.Count > 0)
        {
            Vector2Int tile = frontier.Pop();
            land[tile.x, tile.y] = true;
            count++;

            foreach (Vector2Int offset in Neighbours)
            {
                int nx = tile.x + offset.x;
                int ny = tile.y + offset.y;
                if (nx < border || ny < border || nx >= width - border || ny >= height - border) continue;
                if (queued[nx, ny]) continue;
                queued[nx, ny] = true;
                frontier.Push(new Vector2Int(nx, ny));
            }
        }
    }

    // Порядок без ничьих: при равной высоте — меньший индекс, чтобы результат не зависел от реализации кучи
    private static bool IsHigher(float[,] heights, int ax, int ay, int bx, int by, int width)
    {
        float a = heights[ax, ay];
        float b = heights[bx, by];
        if (a != b) return a > b;
        return ay * width + ax < by * width + bx;
    }

    /// <summary>
    /// Двоичная куча тайлов: сверху самый высокий (в .NET Standard 2.1 нет PriorityQueue).
    /// </summary>
    private sealed class TileHeap
    {
        private readonly List<Vector2Int> _items = new List<Vector2Int>();
        private readonly float[,] _heights;
        private readonly int _width;

        public int Count => _items.Count;

        public TileHeap(float[,] heights, int width)
        {
            _heights = heights;
            _width = width;
        }

        public void Push(Vector2Int tile)
        {
            _items.Add(tile);
            int i = _items.Count - 1;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (!Higher(_items[i], _items[parent])) break;
                (_items[i], _items[parent]) = (_items[parent], _items[i]);
                i = parent;
            }
        }

        public Vector2Int Pop()
        {
            Vector2Int top = _items[0];
            int last = _items.Count - 1;
            _items[0] = _items[last];
            _items.RemoveAt(last);

            int i = 0;
            while (true)
            {
                int left = 2 * i + 1;
                int right = left + 1;
                int best = i;
                if (left < _items.Count && Higher(_items[left], _items[best])) best = left;
                if (right < _items.Count && Higher(_items[right], _items[best])) best = right;
                if (best == i) break;
                (_items[i], _items[best]) = (_items[best], _items[i]);
                i = best;
            }

            return top;
        }

        private bool Higher(Vector2Int a, Vector2Int b) => IsHigher(_heights, a.x, a.y, b.x, b.y, _width);
    }
}
