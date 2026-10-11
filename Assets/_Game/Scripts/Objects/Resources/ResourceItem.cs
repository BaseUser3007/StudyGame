using UnityEngine;

/// <summary>
/// Базовый класс для всех ресурсов игры.
/// </summary>
public abstract class ResourceItem : ScriptableObject
{
    [Header("Base")]
    public string itemId;
    public string displayName;
    public Sprite sprite;

    [Header("Harvest")]
    [Tooltip("Прочность: сколько энергии тратится на добычу одной единицы ресурса")]
    [Min(0f)] public float durability = 1f;

    [Header("Inventory")]
    public int maxStackSize = 20;

    public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;

    /// <summary>
    /// Подходит ли название из Lua (itemId, отображаемое имя или имя ассета, без учёта регистра).
    /// </summary>
    public bool MatchesName(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return false;
        query = query.Trim();

        return string.Equals(query, itemId, System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(query, displayName, System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(query, name, System.StringComparison.OrdinalIgnoreCase);
    }
}