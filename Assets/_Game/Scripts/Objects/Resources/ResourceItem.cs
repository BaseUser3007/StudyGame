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
}