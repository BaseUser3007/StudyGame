using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Все постройки хаба и размер его сетки — один ассет для Level (этап 4).
/// Порядок списка — порядок в панели построек.
/// </summary>
[CreateAssetMenu(fileName = "HubBuildingCatalog", menuName = "CodeRogue/Hub/HubBuildingCatalog")]
public class HubBuildingCatalog : ScriptableObject
{
    public static readonly Vector2Int DefaultGridSize = new Vector2Int(8, 6);

    [Tooltip("Размер сетки хаба в клетках (ширина × высота)")]
    [SerializeField] private Vector2Int gridSize = new Vector2Int(8, 6);
    [SerializeField] private List<HubBuildingDefinition> buildings = new List<HubBuildingDefinition>();

    public Vector2Int GridSize => new Vector2Int(Mathf.Max(1, gridSize.x), Mathf.Max(1, gridSize.y));
    public IReadOnlyList<HubBuildingDefinition> Buildings => buildings;

    /// <summary>Определение по id (первое с таким id) или null.</summary>
    public HubBuildingDefinition Find(string id)
    {
        if (string.IsNullOrEmpty(id) || buildings == null) return null;

        foreach (HubBuildingDefinition definition in buildings)
            if (definition != null && definition.Id == id)
                return definition;
        return null;
    }

    /// <summary>Id всех построек — для UnlockRegistry.SetKnownHubBuildings.</summary>
    public IEnumerable<string> GetIds()
    {
        if (buildings == null) yield break;
        foreach (HubBuildingDefinition definition in buildings)
            if (definition != null)
                yield return definition.Id;
    }

    // ─── Validation ───────────────────────────────────────────

    /// <summary>Проблемы каталога: пустые элементы, дубли id, постройки больше сетки, проблемы определений.</summary>
    public List<string> Validate()
    {
        var problems = new List<string>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        Vector2Int grid = GridSize;

        if (buildings == null) return problems;

        for (int i = 0; i < buildings.Count; i++)
        {
            HubBuildingDefinition definition = buildings[i];
            if (definition == null)
            {
                problems.Add($"element #{i + 1} is empty");
                continue;
            }

            if (!ids.Add(definition.Id))
                problems.Add($"duplicate building id '{definition.Id}' ({definition.name}) — only the first is used");

            if (definition.Size.x > grid.x || definition.Size.y > grid.y)
                problems.Add($"{definition.name}: size {definition.Size} does not fit the grid {grid}");

            foreach (string problem in definition.Validate())
                problems.Add($"{definition.name}: {problem}");
        }

        return problems;
    }

    private void OnValidate()
    {
        gridSize = new Vector2Int(Mathf.Max(1, gridSize.x), Mathf.Max(1, gridSize.y));
        buildings ??= new List<HubBuildingDefinition>();
        foreach (string problem in Validate())
            Debug.LogWarning($"HubBuildingCatalog: {problem}", this);
    }
}
