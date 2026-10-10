using UnityEngine;

/// <summary>
/// ƒерево Ч требует топор дл€ рубки.
/// </summary>
public class TreeObject : ExtractableResource
{
    public override ToolType? RequiredTool => ToolType.Axe;
}