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

    [Header("Inventory")]
    public int maxStackSize = 20;
}