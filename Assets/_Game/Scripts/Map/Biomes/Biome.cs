using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Ресурс, который порождается внутри биома.
/// </summary>
[Serializable]
public class BiomeResource
{
    public ResourceObject prefab;

    [Tooltip("Сколько объектов на 100 тайлов суши этого биома")]
    [Min(0f)] public float density = 1f;
}

/// <summary>
/// Биом: область суши со своим цветом земли и набором ресурсов.
/// </summary>
[Serializable]
public class Biome
{
    public string name = "Biome";

    [Tooltip("Насколько часто биом выпадает относительно других")]
    [Min(0f)] public float weight = 1f;

    [Tooltip("Тайл земли этого биома (LandRuleTile — тогда между биомами нет береговой кромки). " +
             "Пусто — MapGenerationConfig.grassTile")]
    public TileBase groundTile;

    public List<BiomeResource> resources = new List<BiomeResource>();
}
