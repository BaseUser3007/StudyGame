using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Список биомов и параметры их раскладки по карте.
/// </summary>
[CreateAssetMenu(fileName = "BiomeConfig", menuName = "CodeRogue/Map/BiomeConfig")]
public class BiomeConfig : ScriptableObject
{
    public List<Biome> biomes = new List<Biome>();

    [Header("Layout")]
    [Tooltip("Средний размер одного биома (в тайлах)")]
    [Min(4f)] public float biomeSize = 30f;

    [Tooltip("Насколько сильно изгибаются границы биомов (в тайлах)")]
    [Min(0f)] public float borderWarp = 6f;

    [Tooltip("Масштаб шума границ — больше значение, плавнее изгибы")]
    [Min(1f)] public float borderNoiseScale = 10f;

    private void OnValidate()
    {
        foreach (Biome biome in biomes)
        {
            if (biome == null) continue;
            if (string.IsNullOrWhiteSpace(biome.name)) biome.name = "Biome";
        }
    }
}
