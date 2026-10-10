using UnityEngine;

/// <summary>
/// Дерево — требует топор для рубки.
/// </summary>
public class TreeObject : ExtractableResource
{
    public override ToolType? RequiredTool => ToolType.Axe;
}