using UnityEngine;

/// <summary>
/// Инструмент — топор, кирка и т.д. Получается крафтом.
/// Нужен для добычи объектов с RequiredTool; каждая добыча изнашивает его,
/// при нулевой прочности инструмент ломается и исчезает из инвентаря.
/// </summary>
[CreateAssetMenu(fileName = "ToolItem", menuName = "CodeRogue/Resources/ToolItem")]
public class ToolItem : ResourceItem
{
    [Header("Tool")]
    public ToolType toolType;

    [Tooltip("Сколько добыч выдержит новый инструмент (при wearPerUse = 1)")]
    [Min(1)] public int maxDurability = 20;

    [Tooltip("Сколько прочности теряется за одну добычу")]
    [Min(1)] public int wearPerUse = 1;

    public int MaxDurability => Mathf.Max(1, maxDurability);
}
