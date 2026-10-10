/// <summary>
/// Кролик — требует топор для охоты.
/// </summary>
public class RabbitObject : ExtractableResource
{
    public override ToolType? RequiredTool => ToolType.Axe;
}