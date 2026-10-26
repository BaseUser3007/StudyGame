using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[CreateAssetMenu(fileName = "MapGenerationConfig", menuName = "CodeRogue/Map/MapGenerationConfig")]
public class MapGenerationConfig : ScriptableObject
{
    [Header("Map Padding")]
    [Tooltip("Extra tiles rendered outside the playable area")]
    public int mapPadding = 20;

    [Header("Map Size")]
    [Tooltip("Width of the map in tiles")]
    public int mapWidth = 100;

    [Tooltip("Height of the map in tiles")]
    public int mapHeight = 100;

    [Header("Noise Settings")]
    [Tooltip("Scale of the Perlin noise — higher values produce smoother terrain")]
    public float noiseScale = 20f;

    [Tooltip("Random seed for map generation — same seed always produces same map")]
    public int seed = 0;

    [Tooltip("If true, a random seed is used every time")]
    public bool randomSeed = true;

    [Header("Tile Thresholds")]
    [Tooltip("Noise values below this threshold become Water tiles")]
    [Range(0f, 1f)]
    public float waterThreshold = 0.35f;

    [Header("Tiles")]
    [Tooltip("Rule Tile used for grass surface")]
    public TileBase grassTile;

    [Tooltip("Rule Tile used for water surface")]
    public TileBase waterTile;

    [Header("Fog of War")]
    [Tooltip("Tile used to cover unexplored areas")]
    public TileBase fogTile;

    [Header("Biomes")]
    [Tooltip("Биомы и ресурсы в них. Раскладка биомов случайная при каждой генерации")]
    public BiomeConfig biomeConfig;

    [Header("Starter Resources (Run only)")]
    [Tooltip("Гарантированный набор в обзоре со спавна: после обычной генерации ObjectGenerator досыпает " +
             "объекты каждого типа до count (уже лежащие в пределах starterMaxRadius засчитываются). " +
             "Строки без префаба пропускаются. Только в режиме Run")]
    public List<StarterResource> starterResources = new List<StarterResource>
    {
        // Цепочка §6.1: кремень = 2 камня, топор = 2 палки + кремень, верстак — брёвна и палки с 2 деревьев
        new StarterResource { count = 4 }, // палка
        new StarterResource { count = 4 }, // камень
        new StarterResource { count = 2 }  // дерево
    };

    [Tooltip("Внутренний радиус кольца (в тайлах, от центра тайла спавна до позиции объекта). " +
             "Не меньше spawnClearRadius в ObjectGenerator — иначе берётся он")]
    [Min(0f)] public float starterMinRadius = 3f;

    [Tooltip("Внешний радиус кольца. Обзор — квадрат ±visionRadius тайлов (4) вокруг тайла игрока, " +
             "видна любая точка с |dx|, |dy| < visionRadius + 0.5, поэтому 4 — ещё целиком в обзоре")]
    [Min(0f)] public float starterMaxRadius = 4f;

    [Header("Run Location: portal (Run only)")]
    [Tooltip("Вид портала забега (опорная точка — основание, Collider2D у основания). Компонент RunPortal " +
             "добавится сам. Пусто — рисованный овал")]
    public GameObject portalPrefab;

    [Tooltip("Размер портала в тайлах")]
    public Vector2Int portalSize = Vector2Int.one;

    [Tooltip("Поле суши вокруг портала (тайлы): под ним и вокруг нет воды и края карты")]
    [Min(0)] public int portalLandMargin = 1;

    [Tooltip("Минимальная площадь связной суши (тайлы), где начинается забег. Нет такой — самая большая")]
    [Min(1)] public int portalMinRegionTiles = RandomPortalLocationGenerator.DefaultMinRegionTiles;

    [Tooltip("Ресурсы не создаются ближе этого (тайлы) к краю портала")]
    [Min(0f)] public float portalClearRadius = 1.5f;
}

/// <summary>
/// Строка стартового набора: префаб объекта и сколько таких должно быть у спавна.
/// </summary>
[Serializable]
public class StarterResource
{
    public ResourceObject prefab;
    [Min(0)] public int count = 1;
}