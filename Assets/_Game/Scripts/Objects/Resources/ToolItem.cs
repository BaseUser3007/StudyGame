using UnityEngine;

/// <summary>
/// Инструмент — топор, кирка и т.д. Получается крафтом.
/// </summary>
[CreateAssetMenu(fileName = "ToolItem", menuName = "CodeRogue/Resources/ToolItem")]
public class ToolItem : ResourceItem
{
    [Header("Tool")]
    public ToolType toolType;
}
