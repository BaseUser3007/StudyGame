/// <summary>
/// Камень — требует кирку для добычи.
/// </summary>
public class StoneObject : ExtractableResource
{
    public override ToolType? RequiredTool => ToolType.Pickaxe;
}