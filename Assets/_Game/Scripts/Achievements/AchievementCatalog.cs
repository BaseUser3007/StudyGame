using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Все достижения игры — один ассет для Level. Порядок списка — порядок в панели достижений.
/// </summary>
[CreateAssetMenu(fileName = "AchievementCatalog", menuName = "CodeRogue/Achievements/AchievementCatalog")]
public class AchievementCatalog : ScriptableObject
{
    [SerializeField] private List<AchievementDefinition> achievements = new List<AchievementDefinition>();

    public IReadOnlyList<AchievementDefinition> Achievements => achievements;

    // ─── Validation ───────────────────────────────────────────

    /// <summary>
    /// Проблемы каталога: пустые элементы, дубли id и проблемы самих определений.
    /// </summary>
    public List<string> Validate()
    {
        var problems = new List<string>();
        var ids = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < achievements.Count; i++)
        {
            AchievementDefinition definition = achievements[i];
            if (definition == null)
            {
                problems.Add($"element #{i + 1} is empty");
                continue;
            }

            if (!ids.Add(definition.Id))
                problems.Add($"duplicate achievement id '{definition.Id}' ({definition.name}) — only the first is used");

            foreach (string problem in definition.Validate())
                problems.Add($"{definition.name}: {problem}");
        }

        return problems;
    }

    private void OnValidate()
    {
        achievements ??= new List<AchievementDefinition>();
        foreach (string problem in Validate())
            Debug.LogWarning($"AchievementCatalog: {problem}", this);
    }
}
