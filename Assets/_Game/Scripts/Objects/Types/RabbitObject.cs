/// <summary>
///  ролик Ч требует топор дл€ охоты.
/// </summary>
public class RabbitObject : ExtractableResource
{
    public override ToolType? RequiredTool => ToolType.Axe;
}