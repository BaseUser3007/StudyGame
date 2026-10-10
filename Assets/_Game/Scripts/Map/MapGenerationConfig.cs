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
}